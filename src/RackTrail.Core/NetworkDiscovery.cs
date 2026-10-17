using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
namespace RackTrail.Core;

public static class ScanTargets
{
    public const int MaximumHosts = 1024;
    private static uint Number(IPAddress address) { var b=address.GetAddressBytes();return ((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3]; }
    private static uint ParseAddress(string text)
    {
        var parts=text.Trim().Split('.');
        if(parts.Length!=4||parts.Any(p=>p.Length==0||!p.All(char.IsAsciiDigit)||!byte.TryParse(p,out _)))throw new ArgumentException("Enter full IPv4 addresses, for example 192.168.1.10.");
        return Number(IPAddress.Parse(string.Join('.',parts.Select(byte.Parse))));
    }
    private static (uint Start,uint End) Range(string text, bool hostsOnly)
    {
        var cidr=text.Split('/');
        if(cidr.Length==2){var ip=ParseAddress(cidr[0]);if(!int.TryParse(cidr[1],out int prefix)||prefix<0||prefix>32)throw new ArgumentException("IPv4 subnet prefixes must be 0–32.");uint mask=prefix==0?0:uint.MaxValue<<(32-prefix);uint start=ip&mask,end=start|~mask;return hostsOnly&&prefix<31?(start+1,end-1):(start,end);}
        var range=text.Split('-');
        if(range.Length==2){var start=ParseAddress(range[0]);var end=ParseAddress(range[1]);if(start>end)throw new ArgumentException("The first address must come before the last address.");return(start,end);}
        if(cidr.Length!=1||range.Length!=1)throw new ArgumentException("Use IPv4 addresses, CIDR subnets or start-end ranges.");
        var value=ParseAddress(text);return(value,value);
    }
    private static IEnumerable<string> Entries(string text)=>text.Split([',',';','\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    public static IReadOnlyList<IPAddress> Parse(string targets,string exclusions="")
    {
        var ranges=Entries(targets).Select(t=>Range(t,true)).ToList();
        if(ranges.Count==0)throw new ArgumentException("Enter a subnet, IP range or individual address to scan.");
        var skip=Entries(exclusions).Select(t=>Range(t,false)).ToList();var addresses=new SortedSet<uint>();
        foreach(var range in ranges){if((ulong)range.End-range.Start+1>MaximumHosts)throw new ArgumentException($"Limit each scan to {MaximumHosts} target addresses. Split larger networks into smaller scans.");for(ulong value=range.Start;value<=range.End;value++){uint ip=(uint)value;if(!skip.Any(r=>ip>=r.Start&&ip<=r.End))addresses.Add(ip);if(addresses.Count>MaximumHosts)throw new ArgumentException($"Limit each scan to {MaximumHosts} target addresses.");}}
        if(addresses.Count==0)throw new ArgumentException("No addresses remain after exclusions.");
        if(addresses.Any(ip=>(ip>>24)==0||(ip>>24)>=224))throw new ArgumentException("Targets must be unicast IPv4 addresses.");
        return addresses.Select(ip=>new IPAddress(new byte[]{(byte)(ip>>24),(byte)(ip>>16),(byte)(ip>>8),(byte)ip})).ToList();
    }
}

public record DiscoveredHost(string Address,string Hostname,long ResponseTime);
public record ScanProgress(int Completed,int Total,int Found,int Errors);
public record ScanReport(IReadOnlyList<DiscoveredHost> Hosts,int Completed,int Errors,bool Cancelled);
public static class NetworkDiscovery
{
    public static async Task<ScanReport> ScanAsync(IReadOnlyList<IPAddress> targets,IProgress<ScanProgress>? progress=null,CancellationToken cancellationToken=default,
        Func<IPAddress,CancellationToken,Task<long?>>? probe=null,Func<IPAddress,CancellationToken,Task<string>>? resolve=null)
    {
        if(targets.Count==0||targets.Count>ScanTargets.MaximumHosts||targets.Any(a=>a.AddressFamily!=AddressFamily.InterNetwork))throw new ArgumentException("Choose up to 1024 IPv4 targets.");
        probe??=async(address,ct)=>{using var ping=new Ping();var reply=await ping.SendPingAsync(address,800).WaitAsync(ct);return reply.Status==IPStatus.Success?reply.RoundtripTime:null;};
        resolve??=async(address,ct)=>{try{return (await Dns.GetHostEntryAsync(address.ToString()).WaitAsync(TimeSpan.FromMilliseconds(1200),ct)).HostName;}catch(Exception ex)when(ex is SocketException or TimeoutException or ArgumentException){return "";}};
        var hosts=new List<DiscoveredHost>();var gate=new object();int completed=0,errors=0;bool cancelled=false;
        try{await Parallel.ForEachAsync(targets,new ParallelOptions{MaxDegreeOfParallelism=16,CancellationToken=cancellationToken},async(address,ct)=>{
            try{var time=await probe(address,ct);if(time.HasValue){string hostname=await resolve(address,ct);lock(gate)hosts.Add(new(address.ToString(),hostname,time.Value));}}
            catch(Exception ex)when(ex is PingException or SocketException or InvalidOperationException){lock(gate)errors++;}
            finally{lock(gate){completed++;progress?.Report(new(completed,targets.Count,hosts.Count,errors));}}
        });}catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){cancelled=true;}
        return new(hosts.OrderBy(h=>IpNetwork.Number(IPAddress.Parse(h.Address))).ToList(),completed,errors,cancelled);
    }
}

public static class DiscoveryImport
{
    public static bool Exists(Project project,string address)=>project.Nodes.Where(n=>!n.IsContainer).SelectMany(n=>n.Ips).Any(ip=>IPAddress.TryParse(ip,out var parsed)&&parsed.Equals(IPAddress.Parse(address)));
    public static IReadOnlyList<Node> Add(Project project,IEnumerable<DiscoveredHost> hosts,string? subnetId=null)
    {
        var subnet=project.Nodes.Find(n=>n.Id==subnetId);
        if(subnetId!=null&&(subnet?.Kind!=NodeKind.Subnet||!IpNetwork.TryParse(subnet.Cidr,out _)))throw new ArgumentException("Choose a subnet with a valid CIDR.");
        var added=new List<Node>();
        foreach(var host in hosts){if(!IPAddress.TryParse(host.Address,out var address)||address.AddressFamily!=AddressFamily.InterNetwork)continue;if(subnet!=null&&(!IpNetwork.TryParse(subnet.Cidr,out var net)||!net!.Contains(address)))continue;if(Exists(project,host.Address))continue;
            var node=new Node{Kind=NodeKind.Device,Name=host.Hostname.Length>0?host.Hostname:host.Address,Hostname=host.Hostname,Addresses=host.Address,Notes="Discovered by ICMP ping. Device type has not been identified.",X=subnet?.X+20??100+(added.Count%4)*210,Y=subnet?.Y+70??100+(added.Count/4)*110};project.Nodes.Add(node);if(subnet!=null)Containment.Assign(project,node,subnet.Id);added.Add(node);
        }
        return added;
    }
}
