using System.Text.RegularExpressions;
namespace RackTrail.Core;

public enum ClassificationConfidence { Low, Medium, High }
public enum EvidenceSource { Hostname, SnmpSystem, SnmpCapabilities, Interfaces, Lldp, Cdp, Services, MacVendor, Inventory, Previous }
public sealed record DeviceEvidence(EvidenceSource Source, string Detail, NodeKind SuggestedType = NodeKind.Device, int Weight = 0, string Role = "");
public sealed class DeviceClassification
{
    internal DeviceClassification SnapshotCopy(){var copy=(DeviceClassification)MemberwiseClone();copy.Roles=Roles.ToList();copy.Evidence=Evidence.ToList();copy.Limitations=Limitations.ToList();copy.Neighbors=Neighbors.ToList();return copy;}
    public NodeKind AutomaticType { get; set; } = NodeKind.Device;
    public NodeKind? ManualType { get; set; }
    public ClassificationConfidence Confidence { get; set; }
    public string Explanation { get; set; } = "Insufficient evidence.";
    public string Vendor { get; set; } = "";
    public string Model { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    public List<DeviceEvidence> Evidence { get; set; } = [];
    public List<string> Limitations { get; set; } = [];
    public List<DiscoveryNeighbor> Neighbors { get; set; } = [];
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore] public NodeKind EffectiveType => ManualType ?? AutomaticType;
    [System.Text.Json.Serialization.JsonIgnore] public string Summary => ManualType.HasValue?$"{DeviceClassifier.Label(EffectiveType)} · Manual (automatic: {DeviceClassifier.Label(AutomaticType)}, {Confidence} confidence)":$"{DeviceClassifier.Label(EffectiveType)} · {Confidence} confidence";
}
public sealed record DiscoveryNeighbor(string Protocol, string Name, string Description, string Address, string MacAddress, int Capabilities, string LocalPort = "");
public sealed class DiscoveryFacts
{
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool ManagementResponded { get; set; }
    public string Hostname { get; set; } = "";
    public string SystemDescription { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Model { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string MacVendor { get; set; } = "";
    public bool Bridge { get; set; }
    public bool Routing { get; set; }
    public bool WirelessInterface { get; set; }
    public bool Printer { get; set; }
    public int InterfaceCount { get; set; }
    public int LldpCapabilities { get; set; }
    public List<int> OpenPorts { get; set; } = [];
    public List<DeviceEvidence> NeighborEvidence { get; set; } = [];
    public List<DiscoveryNeighbor> Neighbors { get; set; } = [];
    public List<string> Limitations { get; set; } = [];
}
public static class DeviceClassifier
{
    public static readonly NodeKind[] Types = [NodeKind.Device,NodeKind.Server,NodeKind.Switch,NodeKind.Router,NodeKind.Firewall,NodeKind.AccessPoint,NodeKind.Printer,NodeKind.Workstation,NodeKind.Phone,NodeKind.IoT,NodeKind.Storage,NodeKind.Hypervisor,NodeKind.VirtualMachine,NodeKind.Phones];
    public static string Label(NodeKind kind) => kind switch { NodeKind.Device=>"Unknown",NodeKind.AccessPoint=>"Access point",NodeKind.Phone=>"Phone",NodeKind.IoT=>"IoT",NodeKind.Phones=>"Phones",_=>kind.ToString() };
    public static string Mac(string value) => new(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
    private static NodeKind DescriptionType(string text)
    {
        foreach(var (pattern,kind) in new (string,NodeKind)[] {
            (@"\b(fortigate|firewall|palo alto|pan-os|sophos xg|security appliance|cisco asa)\b",NodeKind.Firewall),
            (@"\b(ip phone|voip phone|sip phone|telephone)\b",NodeKind.Phone),
            (@"\b(access point|unifi ap|uap-|aironet|aruba instant|wireless ap)\b",NodeKind.AccessPoint),
            (@"\b(printer|laserjet|officejet|jetdirect)\b",NodeKind.Printer),
            (@"\b(switch|catalyst|procurve|powerconnect)\b",NodeKind.Switch),
            (@"\b(router|isr[ -]?[0-9]|asr[ -]?[0-9])\b",NodeKind.Router),
            (@"\b(windows server|server edition)\b",NodeKind.Server),
            (@"\b(windows (10|11)|workstation|desktop computer)\b",NodeKind.Workstation),
            (@"\b(ip camera|network camera|sensor|smart plug|thermostat)\b",NodeKind.IoT)
        }) if(Regex.IsMatch(text,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))return kind;
        return NodeKind.Device;
    }
    public static DeviceClassification Classify(DiscoveryFacts facts, Node? inventory=null)
    {
        var result=new DeviceClassification { ObservedAt=facts.CollectedAt,Vendor=facts.Vendor.Length>0?facts.Vendor:facts.MacVendor,Model=facts.Model,OperatingSystem=facts.OperatingSystem,MacAddress=facts.MacAddress,Limitations=[..facts.Limitations],Neighbors=[..facts.Neighbors] };
        void Add(EvidenceSource source,string detail,NodeKind type=NodeKind.Device,int weight=0,string role="") => result.Evidence.Add(new(source,detail,type,weight,role));
        var described=DescriptionType(facts.SystemDescription+" "+facts.Model);
        if(facts.SystemDescription.Length>0||facts.Model.Length>0)Add(EvidenceSource.SnmpSystem,string.Join(" · ",new[]{facts.SystemDescription,facts.Model}.Where(t=>t.Length>0)),described,described==NodeKind.Device?0:6);
        if(facts.MacVendor.Length>0)Add(EvidenceSource.MacVendor,$"MAC organization: {facts.MacVendor}; does not establish device type.");
        if(facts.Routing)Add(EvidenceSource.SnmpCapabilities,"IP forwarding enabled",NodeKind.Device,0,"Routing");
        if(facts.Bridge)Add(EvidenceSource.SnmpCapabilities,"Bridge MIB reports bridge ports",NodeKind.Switch,2,"Bridging");
        if(facts.WirelessInterface)Add(EvidenceSource.Interfaces,"Wireless interface reported; may be a wireless client",NodeKind.AccessPoint,1,"Wireless");
        if(facts.InterfaceCount>=4&&facts.Bridge)Add(EvidenceSource.Interfaces,$"{facts.InterfaceCount} interfaces and bridge support",NodeKind.Switch,1);
        if(facts.Printer)Add(EvidenceSource.SnmpCapabilities,"HOST-RESOURCES reports a printer resource (may be attached)",NodeKind.Printer,3,"Printing");
        AddCapabilities(facts.LldpCapabilities,EvidenceSource.Lldp,"Local LLDP enabled capabilities",result.Evidence);
        result.Evidence.AddRange(facts.NeighborEvidence);
        if(facts.Hostname.Length>0) {
            var tokens=facts.Hostname.Split('.')[0].ToLowerInvariant();
            var kind=Regex.IsMatch(tokens,@"(^|[-_])(ap|wap)([-_0-9]|$)")?NodeKind.AccessPoint:
                Regex.IsMatch(tokens,@"(^|[-_])(sw|switch)([-_0-9]|$)")?NodeKind.Switch:
                Regex.IsMatch(tokens,@"(^|[-_])(fw|firewall)([-_0-9]|$)")?NodeKind.Firewall:
                Regex.IsMatch(tokens,@"(^|[-_])(srv|server)([-_0-9]|$)")?NodeKind.Server:
                Regex.IsMatch(tokens,@"(^|[-_])(pc|desktop|laptop)([-_0-9]|$)")?NodeKind.Workstation:
                Regex.IsMatch(tokens,@"(^|[-_])(printer|prn)([-_0-9]|$)")?NodeKind.Printer:NodeKind.Device;
            Add(EvidenceSource.Hostname,$"Hostname: {facts.Hostname}",kind,kind==NodeKind.Device?0:2);
        }
        foreach(var port in facts.OpenPorts.Distinct()) {
            var (kind,role)=port switch {631 or 9100=>(NodeKind.Printer,"Printing"),5060 or 5061=>(NodeKind.Phone,"SIP"),1883 or 8883=>(NodeKind.Device,"MQTT"),53=>(NodeKind.Device,"DNS"),445=>(NodeKind.Device,"File sharing"),3389=>(NodeKind.Device,"Remote desktop"),22=>(NodeKind.Device,"SSH"),80 or 443=>(NodeKind.Device,"Web management"),_=>(NodeKind.Device,"")};
            Add(EvidenceSource.Services,$"TCP {port} accepts connections; protocol/type not verified",kind,kind==NodeKind.Device?0:2,role.Length>0?$"Possible {role}":"");
        }
        bool identityChanged=inventory?.Classification?.MacAddress is {Length:>0} oldMac&&facts.MacAddress.Length>0&&Mac(oldMac)!=Mac(facts.MacAddress);
        if(inventory!=null&&!identityChanged) {
            if(inventory.Classification==null&&Types.Contains(inventory.Kind)&&inventory.Kind!=NodeKind.Device)
                Add(EvidenceSource.Inventory,"Existing inventory type (operator-entered)",inventory.Kind,6);
            else if(inventory.Classification is {} previous&&previous.AutomaticType!=NodeKind.Device&&DateTimeOffset.UtcNow-previous.ObservedAt<TimeSpan.FromDays(30))
                Add(EvidenceSource.Previous,$"Previous result from {previous.ObservedAt:u}; historical, not fresh proof",previous.AutomaticType,3);
            result.ManualType=inventory.Classification?.ManualType;
            if(result.Vendor.Length==0)result.Vendor=inventory.Vendor;
            if(result.Model.Length==0)result.Model=inventory.Model;
            if(result.OperatingSystem.Length==0)result.OperatingSystem=inventory.OperatingSystem;
        }
        if(identityChanged)result.Limitations.Add("MAC changed at this address: previous automatic evidence was not reused. Review identity before updating inventory.");
        // Existing cards are operator-owned: never silently replace their chosen type.
        if(inventory!=null&&inventory.Classification==null&&inventory.Kind!=NodeKind.Device)result.ManualType=inventory.Kind;
        if(inventory?.Classification?.ManualType is {} manual)result.ManualType=manual;
        result.Roles=result.Evidence.Where(e=>e.Role.Length>0).Select(e=>e.Role).Distinct().Order().ToList();
        var ranked=result.Evidence.Where(e=>e.SuggestedType!=NodeKind.Device).GroupBy(e=>e.SuggestedType)
            .Select(g=>new{Type=g.Key,Score=g.GroupBy(e=>e.Source).Sum(s=>s.Max(e=>e.Weight)),FreshScore=g.Where(e=>e.Source is not EvidenceSource.Previous and not EvidenceSource.Inventory).GroupBy(e=>e.Source).Sum(s=>s.Max(e=>e.Weight)),Groups=g.Where(e=>e.Weight>=2&&e.Source is not EvidenceSource.Previous and not EvidenceSource.Inventory).Select(e=>e.Source).Distinct().Count(),Strong=g.Any(e=>e.Weight>=6&&e.Source!=EvidenceSource.Previous)}).OrderByDescending(x=>x.Score).ToList();
        // Routing is a role of an L3 switch, not evidence that its primary type must be router.
        if(ranked.Any(r=>r.Type==NodeKind.Switch&&r.Score>=6)&&ranked.Any(r=>r.Type==NodeKind.Router))
            ranked.RemoveAll(r=>r.Type==NodeKind.Router&& !result.Evidence.Any(e=>e.SuggestedType==NodeKind.Router&&e.Source==EvidenceSource.SnmpSystem));
        var winner=ranked.FirstOrDefault();
        bool conflict=winner!=null&&ranked.Skip(1).Any(r=>r.Strong);
        if(winner!=null&&winner.Score>=6&&winner.Strong&&!conflict) {
            result.AutomaticType=winner.Type;
            result.Confidence=winner.FreshScore>=10&&winner.Groups>=2?ClassificationConfidence.High:ClassificationConfidence.Medium;
            result.Explanation=string.Join("; ",result.Evidence.Where(e=>e.SuggestedType==winner.Type).Select(e=>e.Detail).Distinct().Take(3));
        } else result.Explanation=conflict?"Conflicting evidence: "+string.Join(", ",ranked.Where(r=>r.Score>=6).Select(r=>Label(r.Type)))+". Review device details.":
            winner!=null?$"Insufficient independent evidence for {Label(winner.Type)}.":"Insufficient evidence to identify device type.";

        return result;
    }
    public static void AddCapabilities(int mask,EvidenceSource source,string detail,List<DeviceEvidence> evidence) {
        // IEEE BITS order: bridge=2, WLAN AP=3, router=4, telephone=5, station=7.
        if((mask&0x20)!=0)evidence.Add(new(source,detail+": bridge",(mask&0x14)==0?NodeKind.Switch:NodeKind.Device,(mask&0x14)==0?6:0,"Bridging"));
        if((mask&0x10)!=0)evidence.Add(new(source,detail+": WLAN access point",NodeKind.AccessPoint,6,"Wireless"));
        if((mask&0x08)!=0)evidence.Add(new(source,detail+": router",(mask&0x34)==0?NodeKind.Router:NodeKind.Device,(mask&0x34)==0?6:0,"Routing"));
        if((mask&0x04)!=0)evidence.Add(new(source,detail+": telephone",NodeKind.Phone,6,"Telephony"));
        // Station capability alone does not distinguish a workstation from a server.
    }
    public static void Apply(Node node, DeviceClassification classification) {
        node.Classification=classification;
        if(!node.CanContainNodes&&!node.IsStack&&node.HostId==null)node.Kind=classification.EffectiveType;
        if(node.Vendor.Length==0)node.Vendor=classification.Vendor;
        if(node.Model.Length==0)node.Model=classification.Model;
        if(node.OperatingSystem.Length==0)node.OperatingSystem=classification.OperatingSystem;
    }
    public static void SetManual(Node node,NodeKind kind) {
        node.Classification??=new(){AutomaticType=node.Kind,Explanation="Existing inventory; no automatic assessment."};
        node.Classification.ManualType=kind;node.Kind=kind;
    }
    public static void RestoreAutomatic(Node node) {
        if(node.Classification is not {} classification)return;
        classification.ManualType=null;node.Kind=classification.AutomaticType;
    }
}
