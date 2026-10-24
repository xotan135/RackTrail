using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

using System.Text.Json;
namespace RackTrail.Core;

public sealed class DiscoveryOptions
{
    public SnmpVersion SnmpVersion { get; init; } = SnmpVersion.V2c;
    public bool CheckServices { get; init; }
    public Func<IPAddress,string>? FindMac { get; init; }
    // Session-only credential: never included in projects, logs, evidence, or cache keys.
    public string SnmpCommunity { get; init; } = "";
    public IReadOnlyDictionary<string,string> OuiVendors { get; init; } = new Dictionary<string,string>();
}
public sealed class DiscoverySession
{
    private readonly DiscoveryOptions options;
    private readonly Func<IPAddress,string,CancellationToken,Task<DiscoveryFacts>>? collector;
    private readonly TimeProvider clock;
    private readonly ConcurrentDictionary<string,(DateTimeOffset Time,DiscoveryFacts Facts)> cache=new();
    private static readonly int[] ports=[22,53,80,443,445,631,1883,3389,5060,5061,8883,9100];
    public DiscoverySession(DiscoveryOptions options,Func<IPAddress,string,CancellationToken,Task<DiscoveryFacts>>? collector=null,TimeProvider? clock=null) { this.collector=collector;this.clock=clock??TimeProvider.System; if(System.Text.Encoding.UTF8.GetByteCount(options.SnmpCommunity)>255)throw new ArgumentException("SNMP community must be at most 255 bytes.");if(!Enum.IsDefined(options.SnmpVersion))throw new ArgumentException("Unsupported SNMP version.");this.options=options; }
    public bool CanProbeWithoutPing => options.CheckServices||options.SnmpCommunity.Length>0;
    public async Task<DiscoveryFacts> CollectAsync(IPAddress address,string hostname,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string key=address.ToString();
        if(cache.TryGetValue(key,out var cached)&&clock.GetUtcNow()-cached.Time<TimeSpan.FromMinutes(5))
        {
            var snapshot=JsonSerializer.Deserialize<DiscoveryFacts>(JsonSerializer.Serialize(cached.Facts))!;
            snapshot.Limitations.Add($"Cached observation from {cached.Time:u}; reopen scan window for fresh probes.");
            return snapshot;
        }
        var facts=new DiscoveryFacts{Hostname=hostname,MacAddress=options.FindMac?.Invoke(address)??""};
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(TimeSpan.FromSeconds(10));
        var token=budget.Token;
        try {
            if(collector!=null)facts=await collector(address,hostname,token).WaitAsync(TimeSpan.FromSeconds(10),token);
            else {
            if(options.CheckServices) {
                // At most two TCP connects at once per target, no login or application requests.
                await Parallel.ForEachAsync(ports,new ParallelOptions{MaxDegreeOfParallelism=2,CancellationToken=token},async(port,t)=>{
                    try{using var client=new TcpClient(AddressFamily.InterNetwork);await client.ConnectAsync(address,port,t).AsTask().WaitAsync(TimeSpan.FromMilliseconds(450),t);lock(facts.OpenPorts)facts.OpenPorts.Add(port);}
                    catch(Exception ex)when(ex is SocketException or TimeoutException){}
                });
                if(facts.OpenPorts.Count==0)facts.Limitations.Add("No selected TCP service ports accepted a connection; services may be filtered or unavailable.");
            } else facts.Limitations.Add("Service checks disabled.");
            if(options.SnmpCommunity.Length>0) {
                try { await new SnmpReader(address,options.SnmpCommunity,version:options.SnmpVersion).ReadFactsAsync(facts,token); }
                catch(Exception ex)when(ex is TimeoutException or SocketException or InvalidDataException or System.Formats.Asn1.AsnContentException or SnmpAgentException){facts.Limitations.Add("SNMP unavailable: no reply, credential rejected, or unsupported response. LLDP/CDP and management fingerprints may be unavailable.");}
            } else facts.Limitations.Add("No SNMP credential configured; SNMP, LLDP/CDP and management fingerprints were not queried.");
            }
        } catch(TimeoutException){facts.Limitations.Add("Per-device discovery time budget reached; partial evidence only.");}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){facts.Limitations.Add("Per-device discovery time budget reached; partial evidence only.");}
        ct.ThrowIfCancellationRequested();
        if(facts.MacAddress.Length==0)facts.Limitations.Add("MAC unavailable: local neighbor cache is empty or target is routed.");
        var mac=DeviceClassifier.Mac(facts.MacAddress);
        if(mac.Length==12&&(Convert.ToByte(mac[..2],16)&2)==0&&options.OuiVendors.TryGetValue(mac[..6],out var vendor))facts.MacVendor=vendor;
        else if(mac.Length>0)facts.Limitations.Add("MAC vendor unavailable (no configured OUI entry or locally administered MAC).");
        if(cache.Count>=ScanTargets.MaximumHosts)cache.Clear();
        cache[key]=(clock.GetUtcNow(),facts);
        return JsonSerializer.Deserialize<DiscoveryFacts>(JsonSerializer.Serialize(facts))!;
    }
    public static void MatchNeighbors(IReadOnlyList<DiscoveredHost> hosts,Project? inventory)
    {
        foreach(var observer in hosts)foreach(var neighbor in observer.Facts?.Neighbors??[]) {
            // Never infer that the observer has its neighbor's type, or follow a neighbor address.
            var matches=hosts.Where(h=>neighbor.Address.Length>0&&neighbor.Address==h.Address||
                neighbor.MacAddress.Length>0&&h.Facts?.MacAddress.Length>0&&DeviceClassifier.Mac(neighbor.MacAddress)==DeviceClassifier.Mac(h.Facts.MacAddress)||
                neighbor.Name.Contains('.')&&h.Facts?.Hostname.Length>0&&string.Equals(neighbor.Name,h.Facts.Hostname,StringComparison.OrdinalIgnoreCase)).ToList();
            if(matches.Count!=1||matches[0].Address==observer.Address)continue;
            var target=matches[0].Facts;if(target==null)continue;
            var source=neighbor.Protocol=="CDP"?EvidenceSource.Cdp:EvidenceSource.Lldp;
            DeviceClassifier.AddCapabilities(neighbor.Capabilities,source,$"{neighbor.Protocol} neighbor on {observer.Address}: {neighbor.Description}",target.NeighborEvidence);
            if(neighbor.Description.Length>0) {
                // Description evaluated independently, preserving neighbor origin (not local SNMP).
                var inferred=DeviceClassifier.Classify(new(){SystemDescription=neighbor.Description});
                foreach(var evidence in inferred.Evidence.Where(e=>e.SuggestedType!=NodeKind.Device))target.NeighborEvidence.Add(evidence with{Source=source,Detail=$"{neighbor.Protocol} at {observer.Address}: {evidence.Detail}"});
            }
        }
        foreach(var host in hosts) {
            var existing=inventory?.Nodes.FirstOrDefault(n=>!n.IsContainer&&n.Ips.Contains(host.Address));
            host.Classification=DeviceClassifier.Classify(host.Facts??new(){Hostname=host.Hostname},existing);
        }
    }
}
