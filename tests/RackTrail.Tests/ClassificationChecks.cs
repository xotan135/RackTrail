using RackTrail.Core;
using System.Formats.Asn1;
using System.Net;
using System.Text;

static class ClassificationChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        void Is(DiscoveryFacts facts,NodeKind kind,string why) => check(DeviceClassifier.Classify(facts).AutomaticType==kind,why);
        Is(new(),NodeKind.Device,"Empty discovery remains Unknown");
        Is(new(){MacVendor="Cisco",OpenPorts=[443]},NodeKind.Device,"Vendor and one web port do not establish type");
        Is(new(){OpenPorts=[631,9100]},NodeKind.Device,"Several ports from one weak source do not become definitive");
        Is(new(){Hostname="printer-01",OpenPorts=[631]},NodeKind.Device,"Weak name and port clues remain tentative");
        Is(new(){SystemDescription="Linux 6.8",Bridge=true,InterfaceCount=8},NodeKind.Device,"Linux software bridge is not mistaken for a hardware switch");
        foreach(var pair in new (string,NodeKind)[]{("Windows Server 2025",NodeKind.Server),("Windows 11",NodeKind.Workstation),("Cisco Catalyst switch",NodeKind.Switch),("FortiGate firewall",NodeKind.Firewall),("branch router",NodeKind.Router),("wireless access point",NodeKind.AccessPoint),("HP LaserJet printer",NodeKind.Printer),("Cisco IP Phone 7965",NodeKind.Phone),("network camera",NodeKind.IoT)})
            Is(new(){SystemDescription=pair.Item1},pair.Item2,$"Management description identifies {pair.Item2}");
        var l3=DeviceClassifier.Classify(new(){SystemDescription="Catalyst switch",Bridge=true,Routing=true,LldpCapabilities=0x28});
        check(l3.AutomaticType==NodeKind.Switch&&l3.Confidence==ClassificationConfidence.High&&l3.Roles.Contains("Routing"),"Layer 3 switch keeps switch primary type and routing role");
        var ap=DeviceClassifier.Classify(new(){SystemDescription="wireless access point",LldpCapabilities=0x30});
        check(ap.AutomaticType==NodeKind.AccessPoint&&ap.Confidence==ClassificationConfidence.High&&ap.Roles.Contains("Bridging"),"AP bridge capability is a role rather than conflicting switch proof");
        Is(new(){LldpCapabilities=0x08},NodeKind.Router,"Router-only LLDP capabilities identify router");
        Is(new(){LldpCapabilities=0x24},NodeKind.Phone,"Phone with bridge capability remains a phone");
        var conflict=DeviceClassifier.Classify(new(){SystemDescription="FortiGate firewall",LldpCapabilities=0x04});
        check(conflict.AutomaticType==NodeKind.Device&&conflict.Confidence==ClassificationConfidence.Low&&conflict.Explanation.Contains("Conflicting"),"Conflicting strong evidence becomes Unknown with explanation");
        Is(new(){SystemDescription="Windows Server",Printer=true},NodeKind.Server,"Attached printer resource does not change print server primary type");
        var previousPrinter=new Node{Kind=NodeKind.Printer,Classification=new(){AutomaticType=NodeKind.Printer}};
        var weakHistory=DeviceClassifier.Classify(new(){Hostname="printer-01",OpenPorts=[631]},previousPrinter);
        check(weakHistory.AutomaticType==NodeKind.Device,"Historical guess plus weak name and port cannot establish type");
        var priorStrong=new Node{Kind=NodeKind.Server,Classification=new(){AutomaticType=NodeKind.Server}};
        check(DeviceClassifier.Classify(new(){SystemDescription="Windows Server",Hostname="srv-01"},priorStrong).Confidence==ClassificationConfidence.Medium,"Historical result cannot inflate confidence to High");
        var server=DeviceClassifier.Classify(new(){SystemDescription="Windows Server",OpenPorts=[53,445]});
        check(server.AutomaticType==NodeKind.Server&&server.Roles.Contains("Possible DNS"),"DNS and file services are roles, not primary device types");
        var unknown=DeviceClassifier.Classify(new(){Limitations=["No SNMP credential configured."]});
        check(unknown.Limitations.Count==1&&unknown.Explanation.Length>0,"Missing methods are exposed alongside Unknown result");

        var node=new Node{Id="classified",Kind=NodeKind.Device,Addresses="192.0.2.10",Name="My printer",Role="Office",X=170,Y=220,Notes="Keep me"};
        DeviceClassifier.Apply(node,ap);DeviceClassifier.SetManual(node,NodeKind.Printer);
        var project=ProjectStorage.Deserialize(ProjectStorage.Serialize(new(){Nodes=[node]}));node=project.Nodes[0];
        check(node.Kind==NodeKind.Printer&&node.Classification?.ManualType==NodeKind.Printer&&node.Classification.Evidence.Count>0,"Manual override and full evidence survive JSON round trip");
        var nextFacts=new DiscoveryFacts{SystemDescription="wireless access point",LldpCapabilities=0x10};
        DiscoveryImport.Update(project,[new("192.0.2.10","ap.example",3){Facts=nextFacts}]);
        check(node.Kind==NodeKind.Printer&&node.Classification?.AutomaticType==NodeKind.AccessPoint&&node.Classification.ManualType==NodeKind.Printer,"Rescan updates automatic assessment while retaining manual correction");
        check(node.X==170&&node.Y==220&&node.Role=="Office"&&node.Notes=="Keep me"&&node.Name=="My printer","Rescan preserves manual inventory and map geometry");
        DeviceClassifier.RestoreAutomatic(node);
        check(node.Kind==NodeKind.AccessPoint&&node.Classification?.ManualType==null,"Restore automatic uses latest assessment");
        var legacy=new Node{Kind=NodeKind.Switch,Addresses="192.0.2.20"};
        var legacyProject=new Project{Nodes=[legacy]};
        DiscoveryImport.Update(legacyProject,[new("192.0.2.20","",1){Facts=new(){SystemDescription="network camera"}}]);
        check(legacy.Kind==NodeKind.Switch&&legacy.Classification?.ManualType==NodeKind.Switch,"Legacy inventory type is preserved as operator-owned during discovery");
        check(ProjectStorage.Deserialize("""{"FormatVersion":1,"Nodes":[{"Id":"old","Kind":"Server"}],"Connections":[]}""").Nodes[0].Kind==NodeKind.Server,"Existing project without classification stays backward compatible");
        var historic=new Node{Kind=NodeKind.Server,Classification=new(){AutomaticType=NodeKind.Server,MacAddress="00:11:22:33:44:55",ObservedAt=DateTimeOffset.UtcNow}};
        var historical=DeviceClassifier.Classify(new(),historic);
        check(historical.AutomaticType==NodeKind.Device&&historical.Evidence.Any(e=>e.Source==EvidenceSource.Previous),"Previous automatic discovery is weak historical evidence, not fresh proof");
        var reused=DeviceClassifier.Classify(new(){MacAddress="00:11:22:33:44:99"},historic);
        check(!reused.Evidence.Any(e=>e.Source==EvidenceSource.Previous)&&reused.Limitations.Any(l=>l.Contains("MAC changed")),"Changed MAC prevents reuse of prior automatic identification");
        var csv=OuiRegistry.Parse("OUI,vendor\n00-11-22,Acme Networks\nnotvalid,Ignore\nAA:BB:CC,Example, Inc.");
        check(csv.Count==2&&csv["AABBCC"]=="Example, Inc.","Local OUI parser accepts normalized prefixes without treating vendor as device type");

        var observer=new DiscoveredHost("192.0.2.1","sw.example",1){Facts=new(){Neighbors=[new("LLDP","ap.example","wireless access point","192.0.2.2","",0x10),new("CDP","outside","Cisco IP Phone","198.51.100.1","",0x04)]}};
        var target=new DiscoveredHost("192.0.2.2","",1){Facts=new()};
        DiscoverySession.MatchNeighbors([observer,target],null);
        check(target.Classification?.AutomaticType==NodeKind.AccessPoint&&observer.Classification?.AutomaticType==NodeKind.Device,"Neighbor data classifies only matched target, not observing switch");
        check(target.Classification!.Evidence.Any(e=>e.Source==EvidenceSource.Lldp),"Neighbor evidence retains its protocol and observing address");
        var unmatched=new DiscoveredHost("192.0.2.3","ap",1){Facts=new(){Hostname="ap"}};
        DiscoverySession.MatchNeighbors([observer,unmatched],null);
        check(unmatched.Classification?.AutomaticType==NodeKind.Device,"Ambiguous short neighbor names are not matched");
        var clock=new TestClock();int collections=0;
        var session=new DiscoverySession(new(){CheckServices=true,OuiVendors=csv},(ip,name,ct)=>{collections++;return Task.FromResult(new DiscoveryFacts{Hostname=name,MacAddress="00:11:22:33:44:55",OpenPorts=[443]});},clock);
        var first=await session.CollectAsync(IPAddress.Parse("192.0.2.5"),"device",default);first.OpenPorts.Add(9100);
        var second=await session.CollectAsync(IPAddress.Parse("192.0.2.5"),"device",default);
        check(collections==1&&!second.OpenPorts.Contains(9100)&&second.MacVendor=="Acme Networks","Session cache bounds repeat work and returns independent snapshots");
        clock.Now+=TimeSpan.FromMinutes(6);await session.CollectAsync(IPAddress.Parse("192.0.2.5"),"",default);
        check(collections==2,"Discovery cache expires after five minutes");
        var scan=await NetworkDiscovery.ScanAsync(ScanTargets.Parse("192.0.2.5"),probe:(ip,ct)=>Task.FromResult<long?>(null),discovery:session);
        check(scan.Hosts.Count==1&&scan.Hosts[0].ResponseTime==-1&&scan.Hosts[0].Classification?.AutomaticType==NodeKind.Device,"Opted-in service discovery can find ping-blocked hosts without guessing type");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool cancelled=false;try{await session.CollectAsync(IPAddress.Parse("192.0.2.9"),"",cancel.Token);}catch(OperationCanceledException){cancelled=true;}check(cancelled,"Discovery collection honors cancellation");}

        // Wire fixtures exercise BER validation without sending any packets.
        var data=new Dictionary<string,SnmpValue>{
            ["1.3.6.1.2.1.47.1.1.1.1.5.1"]=new("1.3.6.1.2.1.47.1.1.1.1.5.1",[],3),
            ["1.3.6.1.2.1.47.1.1.1.1.12.1"]=Text("1.3.6.1.2.1.47.1.1.1.1.12.1","Cisco"),
            ["1.3.6.1.2.1.47.1.1.1.1.13.1"]=Text("1.3.6.1.2.1.47.1.1.1.1.13.1","Catalyst 9300"),
            ["1.3.6.1.2.1.1.1.0"]=Text("1.3.6.1.2.1.1.1.0","Cisco Catalyst switch"),
            ["1.3.6.1.2.1.1.5.0"]=Text("1.3.6.1.2.1.1.5.0","core.example"),
            ["1.3.6.1.2.1.4.1.0"]=new("1.3.6.1.2.1.4.1.0",[],1),
            ["1.3.6.1.2.1.17.1.2.0"]=new("1.3.6.1.2.1.17.1.2.0",[],24),
            ["1.3.6.1.2.1.2.1.0"]=new("1.3.6.1.2.1.2.1.0",[],24),
            ["1.3.6.1.2.1.2.2.1.3.1"]=new("1.3.6.1.2.1.2.2.1.3.1",[],71),
            ["1.0.8802.1.1.2.1.3.6.0"]=new("1.0.8802.1.1.2.1.3.6.0",[0x28]),
            ["1.0.8802.1.1.2.1.4.1.1.9.1.2.1"]=Text("1.0.8802.1.1.2.1.4.1.1.9.1.2.1","ap.example"),
            ["1.0.8802.1.1.2.1.4.1.1.10.1.2.1"]=Text("1.0.8802.1.1.2.1.4.1.1.10.1.2.1","wireless access point"),
            ["1.0.8802.1.1.2.1.4.1.1.12.1.2.1"]=new("1.0.8802.1.1.2.1.4.1.1.12.1.2.1",[0x10]),
            ["1.0.8802.1.1.2.1.4.2.1.3.1.2.1.1.4.192.0.2.2"]=new("1.0.8802.1.1.2.1.4.2.1.3.1.2.1.1.4.192.0.2.2",[],2),
            ["1.3.6.1.4.1.9.9.23.1.2.1.1.6.3.1"]=Text("1.3.6.1.4.1.9.9.23.1.2.1.1.6.3.1","phone.example"),
            ["1.3.6.1.4.1.9.9.23.1.2.1.1.8.3.1"]=Text("1.3.6.1.4.1.9.9.23.1.2.1.1.8.3.1","Cisco IP Phone"),
            ["1.3.6.1.4.1.9.9.23.1.2.1.1.4.3.1"]=new("1.3.6.1.4.1.9.9.23.1.2.1.1.4.3.1",[192,0,2,3]),
            ["1.3.6.1.4.1.9.9.23.1.2.1.1.9.3.1"]=new("1.3.6.1.4.1.9.9.23.1.2.1.1.9.3.1",[0,0,0,128])
        };
        int reads=0;bool readOnly=true;
        Task<byte[]> Exchange(byte[] bytes,CancellationToken ct){
            reads++;var request=ReadRequest(bytes);readOnly&=request.Next||request.Tag==0;
            var values=request.Oids.Select(oid=>request.Next?data.Where(p=>Compare(p.Key,oid)>0).OrderBy(p=>p.Key,new OidComparer()).Select(p=>p.Value).FirstOrDefault():data.GetValueOrDefault(oid)).Where(v=>v!=null).Cast<SnmpValue>();
            return Task.FromResult(Response(request.Id,values));
        }
        var snmp=new SnmpReader(IPAddress.Loopback,"test-only",Exchange);var management=new DiscoveryFacts();
        await snmp.ReadFactsAsync(management,default);
        check(management.SystemDescription.Contains("Catalyst")&&management.Routing&&management.Bridge&&management.WirelessInterface,"SNMP wire collector reads descriptions, forwarding, bridge and interface data");
        check(management.Model=="Catalyst 9300"&&management.Vendor=="Cisco","ENTITY-MIB identity uses matched chassis manufacturer and model");
        check(management.Neighbors.Any(n=>n.Protocol=="LLDP"&&n.Address=="192.0.2.2"&&n.Capabilities==0x10)&&management.Neighbors.Any(n=>n.Protocol=="CDP"&&n.Address=="192.0.2.3"&&n.Capabilities==0x04),"SNMP neighbor columns join LLDP/CDP identities and capabilities correctly");
        check(readOnly&&reads<=96,"Management collection uses only bounded GET and GETNEXT reads");
        bool wrongId=false;try{SnmpReader.Parse(Response(7,[]),"test-only",8);}catch(InvalidDataException){wrongId=true;}check(wrongId,"SNMP mismatched request IDs are rejected");
        bool malformed=false;try{SnmpReader.Parse(new byte[]{48,255},"test-only",1);}catch(AsnContentException){malformed=true;}check(malformed,"Malformed SNMP BER response is rejected");
        bool wrongCommunity=false;try{SnmpReader.Parse(Response(1,[]),"wrong",1);}catch(InvalidDataException){wrongCommunity=true;}check(wrongCommunity,"SNMP mismatched community responses are rejected");
        int repeated=0;
        var looping=new SnmpReader(IPAddress.Loopback,"test-only",(bytes,ct)=>{repeated++;return Task.FromResult(Response(ReadRequest(bytes).Id,[new("1.3.6.1.2.1.2.2.1.3.1",[],6)]));});
        var bounded=await looping.WalkAsync("1.3.6.1.2.1.2.2.1.3",8,default);
        check(bounded.Count==1&&repeated==2,"Non-advancing SNMP agent cannot loop table walks");
        var v1Request=ReadRequest(SnmpReader.Request("test-only",1,["1.3.6.1.2.1.1.1.0"],version:SnmpVersion.V1));
        check(v1Request.Version==0&&v1Request.Tag==0,"SNMPv1 selection uses v1 GET wire format");
        check(SnmpReader.Parse(Response(1,[Text("1.3.6.1.2.1.1.1.0","legacy switch")],version:0),"test-only",1,SnmpVersion.V1).Count==1,"SNMPv1 response decoding accepts configured version");
        var v1=new SnmpReader(IPAddress.Loopback,"test-only",(bytes,ct)=>{
            var request=ReadRequest(bytes);
            return Task.FromResult(request.Oids[0]=="1.3.6.1.2.1.1.1.0"
                ?Response(request.Id,[Text(request.Oids[0],"legacy switch")],version:0)
                :Response(request.Id,[],version:0,error:2));
        },SnmpVersion.V1);
        var v1Facts=new DiscoveryFacts();await v1.ReadFactsAsync(v1Facts,default);
        check(v1Facts.SystemDescription=="legacy switch"&&v1Facts.Limitations.Count>0,"SNMPv1 unsupported objects do not discard available system description");
        var badV1=new SnmpReader(IPAddress.Loopback,"test-only",(bytes,ct)=>Task.FromResult(Response(ReadRequest(bytes).Id,[],version:1)),SnmpVersion.V1);
        bool invalidVersionRejected=false;
        try{await badV1.ReadFactsAsync(new(),default);}catch(InvalidDataException){invalidVersionRejected=true;}
        check(invalidVersionRejected,"SNMPv1 does not treat mismatched-version responses as unsupported objects");
        int beforeLimited=reads;
        var limited=new SnmpReader(IPAddress.Loopback,"test-only",Exchange);
        for(int i=0;i<100;i++)await limited.GetAsync(["1.3.6.1.2.1.1.1.0"],false,default);
        check(limited.Truncated&&reads-beforeLimited==96,"SNMP per-host request budget is enforced");
    }
    static SnmpValue Text(string oid,string text)=>new(oid,Encoding.UTF8.GetBytes(text));
    static (int Id,bool Next,int Tag,List<string> Oids,int Version) ReadRequest(byte[] bytes) {
        var packet=new AsnReader(bytes,AsnEncodingRules.BER).ReadSequence();int version=(int)packet.ReadInteger();packet.ReadOctetString();var tag=packet.PeekTag();var pdu=packet.ReadSequence(tag);int id=(int)pdu.ReadInteger();pdu.ReadInteger();pdu.ReadInteger();var bindings=pdu.ReadSequence();var oids=new List<string>();
        while(bindings.HasData){var binding=bindings.ReadSequence();oids.Add(binding.ReadObjectIdentifier());binding.ReadNull();}
        return(id,tag.TagValue==1,tag.TagValue,oids,version);
    }
    static byte[] Response(int id,IEnumerable<SnmpValue> values,int version=1,int error=0) {
        var w=new AsnWriter(AsnEncodingRules.BER);
        using(w.PushSequence()){w.WriteInteger(version);w.WriteOctetString(Encoding.UTF8.GetBytes("test-only"));using(w.PushSequence(new Asn1Tag(TagClass.ContextSpecific,2,true))){w.WriteInteger(id);w.WriteInteger(error);w.WriteInteger(error==0?0:1);using(w.PushSequence())foreach(var value in values)using(w.PushSequence()){w.WriteObjectIdentifier(value.Oid);if(value.Number.HasValue)w.WriteInteger(value.Number.Value);else if(value.ObjectId.Length>0)w.WriteObjectIdentifier(value.ObjectId);else w.WriteOctetString(value.Bytes);}}}
        return w.Encode();
    }
    static int Compare(string a,string b){var x=a.Split('.').Select(long.Parse).ToArray();var y=b.Split('.').Select(long.Parse).ToArray();for(int i=0;i<Math.Min(x.Length,y.Length);i++){int c=x[i].CompareTo(y[i]);if(c!=0)return c;}return x.Length.CompareTo(y.Length);}
    sealed class OidComparer : IComparer<string>{public int Compare(string? x,string? y)=>ClassificationChecks.Compare(x!,y!);}
    sealed class TestClock : TimeProvider {public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
}
