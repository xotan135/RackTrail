using System.Formats.Asn1;
using System.Net;
using System.Net.Sockets;
using System.Text;
namespace RackTrail.Core;

public enum SnmpVersion { V1=0, V2c=1 }
public sealed class SnmpAgentException(int status) : IOException($"SNMP agent returned error {status}.")
{
    public int Status { get; } = status;
}
public sealed record SnmpValue(string Oid, byte[] Bytes, long? Number=null, string ObjectId="")
{
    public string Text => new(Encoding.UTF8.GetString(Bytes).Where(c=>!char.IsControl(c)||c==' ').Take(512).ToArray());
}
/// <summary>Read-only SNMPv1/v2c. No SET, retries, credential guessing, or neighbor traversal.</summary>
public sealed class SnmpReader
{
    private readonly IPAddress address;
    private readonly string community;
    private readonly SnmpVersion version;
    private int requests;
    private readonly Func<byte[],CancellationToken,Task<byte[]>>? exchange;
    public bool Truncated { get; private set; }
    public SnmpReader(IPAddress address,string community,Func<byte[],CancellationToken,Task<byte[]>>? exchange=null,SnmpVersion version=SnmpVersion.V2c) { if(!Enum.IsDefined(version))throw new ArgumentException("Unsupported SNMP version.");this.version=version;this.address=address;this.community=community;this.exchange=exchange; }
    public static byte[] Request(string community,int id,IEnumerable<string> oids,bool next=false,SnmpVersion version=SnmpVersion.V2c)
    {
        var w=new AsnWriter(AsnEncodingRules.BER);
        using(w.PushSequence()) {
            w.WriteInteger((int)version);w.WriteOctetString(Encoding.UTF8.GetBytes(community));
            using(w.PushSequence(new Asn1Tag(TagClass.ContextSpecific,next?1:0,true))) {
                w.WriteInteger(id);w.WriteInteger(0);w.WriteInteger(0);
                using(w.PushSequence())foreach(var oid in oids)using(w.PushSequence()){w.WriteObjectIdentifier(oid);w.WriteNull();}
            }
        }
        return w.Encode();
    }
    public static IReadOnlyList<SnmpValue> Parse(ReadOnlyMemory<byte> bytes,string community,int id,SnmpVersion version=SnmpVersion.V2c)
    {
        if(bytes.Length>65507)throw new InvalidDataException("SNMP response too large.");
        var root=new AsnReader(bytes,AsnEncodingRules.BER);var packet=root.ReadSequence();root.ThrowIfNotEmpty();
        if(packet.ReadInteger()!=(int)version||!packet.ReadOctetString().AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(community)))throw new InvalidDataException("Unexpected SNMP response.");
        var pdu=packet.ReadSequence(new Asn1Tag(TagClass.ContextSpecific,2,true));packet.ThrowIfNotEmpty();
        if(pdu.ReadInteger()!=id)throw new InvalidDataException("Unexpected SNMP request ID.");
        var error=pdu.ReadInteger();pdu.ReadInteger();if(error<0||error>int.MaxValue)throw new InvalidDataException("Invalid SNMP error status.");if(error!=0)throw new SnmpAgentException((int)error);
        var variables=pdu.ReadSequence();pdu.ThrowIfNotEmpty();var result=new List<SnmpValue>();int bindings=0;
        while(variables.HasData) {
            if(bindings++>=128)throw new InvalidDataException("Too many SNMP bindings.");
            var binding=variables.ReadSequence();string oid=binding.ReadObjectIdentifier();var tag=binding.PeekTag();
            if(tag.TagClass==TagClass.Universal&&tag.TagValue==(int)UniversalTagNumber.OctetString)result.Add(new(oid,binding.ReadOctetString()));
            else if(tag.TagClass==TagClass.Universal&&tag.TagValue==(int)UniversalTagNumber.Integer){var number=binding.ReadInteger();result.Add(new(oid,[],number>=long.MinValue&&number<=long.MaxValue?(long)number:null));}
            else if(tag.TagClass==TagClass.Universal&&tag.TagValue==(int)UniversalTagNumber.ObjectIdentifier)result.Add(new(oid,[],null,binding.ReadObjectIdentifier()));
            else if(tag.TagClass==TagClass.Application&&tag.TagValue==0)result.Add(new(oid,binding.ReadOctetString(new Asn1Tag(TagClass.Application,0))));
            else binding.ReadEncodedValue(); // unsupported syntax / noSuchObject / endOfMibView
            binding.ThrowIfNotEmpty();
        }
        return result;
    }
    public async Task<IReadOnlyList<SnmpValue>> GetAsync(IEnumerable<string> oids,bool next,CancellationToken ct)
    {
        if(requests++>=96){Truncated=true;return [];}

        int id=Random.Shared.Next(1,int.MaxValue);
        var request=Request(community,id,oids,next,version);
        if(exchange!=null)return Parse(await exchange(request,ct).WaitAsync(TimeSpan.FromMilliseconds(800),ct),community,id,version);
        using var udp=new UdpClient(AddressFamily.InterNetwork);udp.Connect(address,161);
        await udp.SendAsync(request,ct);
        var reply=await udp.ReceiveAsync(ct).AsTask().WaitAsync(TimeSpan.FromMilliseconds(800),ct);
        return Parse(reply.Buffer,community,id,version);
    }
    public async Task<List<SnmpValue>> WalkAsync(string root,int limit,CancellationToken ct)
    {
        var values=new List<SnmpValue>();string cursor=root;
        for(int i=0;i<limit;i++) {
            var reply=await GetAsync([cursor],true,ct);
            var value=reply.FirstOrDefault();
            if(value==null||!value.Oid.StartsWith(root+".",StringComparison.Ordinal)||CompareOid(value.Oid,cursor)<=0)return values;
            values.Add(value);cursor=value.Oid;
        }
        Truncated=true;return values;
    }
    private static int CompareOid(string a,string b) {
        var left=a.Split('.').Select(long.Parse).ToArray();var right=b.Split('.').Select(long.Parse).ToArray();
        for(int i=0;i<Math.Min(left.Length,right.Length);i++){int c=left[i].CompareTo(right[i]);if(c!=0)return c;}
        return left.Length.CompareTo(right.Length);
    }
    public async Task ReadFactsAsync(DiscoveryFacts facts,CancellationToken ct)
    {
        const string sys="1.3.6.1.2.1.1.";
        string[] scalarOids=[sys+"1.0",sys+"2.0",sys+"5.0","1.3.6.1.2.1.4.1.0","1.3.6.1.2.1.17.1.2.0","1.3.6.1.2.1.2.1.0","1.0.8802.1.1.2.1.3.6.0"];
        IReadOnlyList<SnmpValue> scalar;
        if(version==SnmpVersion.V1){
            // v1 noSuchName rejects the entire PDU: isolate unsupported scalar objects.
            var available=new List<SnmpValue>();
            foreach(var oid in scalarOids){
                try{available.AddRange(await GetAsync([oid],false,ct));}
                catch(SnmpAgentException ex)when(ex.Status==2){facts.Limitations.Add("Some SNMPv1 objects are unavailable.");}
            }
            scalar=available;
        }else scalar=await GetAsync(scalarOids,false,ct);
        facts.ManagementResponded=true;
        if(scalar.Count==0){facts.Limitations.Add("SNMP returned no supported values.");return;}
        facts.SystemDescription=scalar.FirstOrDefault(v=>v.Oid==sys+"1.0")?.Text??"";
        var name=scalar.FirstOrDefault(v=>v.Oid==sys+"5.0")?.Text??"";if(name.Length>0)facts.Hostname=name;
        facts.Routing=scalar.Any(v=>v.Oid=="1.3.6.1.2.1.4.1.0"&&v.Number==1);
        facts.Bridge=scalar.Any(v=>v.Oid=="1.3.6.1.2.1.17.1.2.0"&&v.Number>0);
        facts.InterfaceCount=(int)(scalar.FirstOrDefault(v=>v.Oid=="1.3.6.1.2.1.2.1.0")?.Number??0);
        facts.LldpCapabilities=scalar.FirstOrDefault(v=>v.Oid=="1.0.8802.1.1.2.1.3.6.0")?.Bytes.FirstOrDefault()??0;
        string objectId=scalar.FirstOrDefault(v=>v.Oid==sys+"2.0")?.ObjectId??"";
        facts.Vendor=objectId.StartsWith("1.3.6.1.4.1.9.",StringComparison.Ordinal)?"Cisco":objectId.StartsWith("1.3.6.1.4.1.12356.",StringComparison.Ordinal)?"Fortinet":objectId.StartsWith("1.3.6.1.4.1.41112.",StringComparison.Ordinal)?"Ubiquiti":"";
        var description=facts.SystemDescription;
        foreach(var os in new[]{"Windows Server","Windows 11","Windows 10","FortiOS","JUNOS","Cisco IOS","Linux","FreeBSD"})if(description.Contains(os,StringComparison.OrdinalIgnoreCase)){facts.OperatingSystem=os;break;}
        async Task<List<SnmpValue>> Walk(string oid,int limit=8) {
            try{return await WalkAsync(oid,limit,ct);}catch(Exception ex)when(ex is TimeoutException or SocketException or InvalidDataException or AsnContentException or SnmpAgentException){facts.Limitations.Add("Some SNMP tables unavailable or timed out.");return [];}
        }
        var types=await Walk("1.3.6.1.2.1.2.2.1.3");facts.WirelessInterface=types.Any(v=>v.Number==71);
        var macs=await Walk("1.3.6.1.2.1.2.2.1.6");
        if(facts.MacAddress.Length==0)facts.MacAddress=string.Join(":",(macs.FirstOrDefault(v=>v.Bytes.Length==6&&v.Bytes.Any(b=>b!=0))?.Bytes??[]).Select(b=>b.ToString("X2")));
        var classes=await Walk("1.3.6.1.2.1.47.1.1.1.1.5",4);
        var manufacturers=await Walk("1.3.6.1.2.1.47.1.1.1.1.12",4);
        var models=await Walk("1.3.6.1.2.1.47.1.1.1.1.13",4);
        var modelIndex=classes.FirstOrDefault(v=>v.Number==3)?.Oid.Split('.').Last();
        var manufacturer=modelIndex==null?null:manufacturers.FirstOrDefault(v=>v.Oid.Split('.').Last()==modelIndex);
        if(manufacturer?.Text.Length>0)facts.Vendor=manufacturer.Text;
        facts.Model=modelIndex==null?"":models.FirstOrDefault(v=>v.Oid.Split('.').Last()==modelIndex)?.Text??"";
        if(modelIndex==null&&models.Any(v=>v.Text.Length>0))facts.Limitations.Add("ENTITY-MIB model could not be matched to a chassis; module models were not used as device identity.");
        var hardware=await Walk("1.3.6.1.2.1.25.3.2.1.2");
        facts.Printer=hardware.Any(v=>v.ObjectId=="1.3.6.1.2.1.25.3.1.5");
        // Each table is bounded; rows are joined by the complete remote-neighbor index.
        async Task<Dictionary<string,SnmpValue>> Column(string oid) => (await Walk(oid)).ToDictionary(v=>v.Oid[(oid.Length+1)..]);
        const string lldp="1.0.8802.1.1.2.1.4.1.1.";
        var names=await Column(lldp+"9");var descriptions=await Column(lldp+"10");var capabilities=await Column(lldp+"12");
        var chassis=await Column(lldp+"5");var chassisTypes=await Column(lldp+"4");
        var management=await Column("1.0.8802.1.1.2.1.4.2.1.3");
        var neighborAddresses=new Dictionary<string,string>();
        foreach(var entry in management.Keys){
            var parts=entry.Split('.');
            // LLDP remote index followed by IANA address family, address length and address octets.
            if(parts.Length==9&&parts[3]=="1"&&parts[4]=="4"&&parts.Skip(5).All(p=>byte.TryParse(p,out _)))
                neighborAddresses[string.Join(".",parts.Take(3))]=new IPAddress(parts.Skip(5).Select(byte.Parse).ToArray()).ToString();
        }
        foreach(var pair in names) {
            var mac=chassisTypes.GetValueOrDefault(pair.Key)?.Number==4?Convert.ToHexString(chassis.GetValueOrDefault(pair.Key)?.Bytes??[]):"";
            facts.Neighbors.Add(new("LLDP",pair.Value.Text,descriptions.GetValueOrDefault(pair.Key)?.Text??"",neighborAddresses.GetValueOrDefault(pair.Key)??"",mac,capabilities.GetValueOrDefault(pair.Key)?.Bytes.FirstOrDefault()??0,pair.Key));
        }
        const string cdp="1.3.6.1.4.1.9.9.23.1.2.1.1.";
        var cdpNames=await Column(cdp+"6");var platforms=await Column(cdp+"8");var addresses=await Column(cdp+"4");var caps=await Column(cdp+"9");
        foreach(var pair in cdpNames) {
            var raw=addresses.GetValueOrDefault(pair.Key)?.Bytes??[];
            var capBytes=caps.GetValueOrDefault(pair.Key)?.Bytes??[];int cdpCap=0;foreach(byte b in capBytes.Take(4))cdpCap=(cdpCap<<8)|b;
            // Convert CDP bits to IEEE BITS for the shared capability rules.
            int mask=((cdpCap&8)!=0?0x20:0)|((cdpCap&1)!=0?0x08:0)|((cdpCap&128)!=0?0x04:0);
            facts.Neighbors.Add(new("CDP",pair.Value.Text,platforms.GetValueOrDefault(pair.Key)?.Text??"",raw.Length==4?new IPAddress(raw).ToString():"","",mask,pair.Key));
        }
        if(facts.Neighbors.Count==0)facts.Limitations.Add("No accessible LLDP/CDP neighbor rows.");
        if(Truncated)facts.Limitations.Add("SNMP tables reached row/request limits; evidence may be incomplete.");
    }
}
