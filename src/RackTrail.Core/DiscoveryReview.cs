using System.Net;
using System.Net.Sockets;
using System.Text.Json;
namespace RackTrail.Core;

public enum DiscoveryPlacement { KeepExisting, AssignContainer, NewSubnet }
public sealed record DiscoverySelection(DiscoveredHost Host,NodeKind? ManualType=null,bool UseAutomatic=false);
public sealed record DiscoveryDestination(DiscoveryPlacement Placement=DiscoveryPlacement.KeepExisting,string? ContainerId=null,string SubnetName="",string Cidr="",string? ParentId=null);
public sealed record DiscoveryApplied(IReadOnlyList<Node> Devices,Node? CreatedSubnet);
public static class DiscoveryReview
{
    public static readonly NodeKind[] EditableTypes=[NodeKind.Device,NodeKind.Server,NodeKind.Switch,NodeKind.Router,NodeKind.Firewall,NodeKind.AccessPoint,NodeKind.Printer,NodeKind.Workstation,NodeKind.Phone,NodeKind.IoT,NodeKind.Storage,NodeKind.Phones];
    public static bool TypeLocked(Project project,Node node)=>node.IsStack||node.CanContainNodes||node.HostId!=null||project.Nodes.Any(n=>n.ParentId==node.Id||n.HostId==node.Id);
    public static Node? Existing(Project project,string address)=>project.Nodes.FirstOrDefault(n=>!n.IsContainer&&n.Ips.Any(ip=>IPAddress.TryParse(ip,out var parsed)&&IPAddress.TryParse(address,out var candidate)&&parsed.Equals(candidate)));
    // Caller owns the undo transaction (EditorViewModel.Edit); validate inputs before changing the map.
    public static DiscoveryApplied Apply(Project project,IEnumerable<DiscoverySelection> selections,DiscoveryDestination destination)
    {
        ProjectLimits.ValidateHierarchy(project);
        var choices=selections.ToList();
        if(choices.Count==0)throw new ArgumentException("Select at least one device.");
        if(!Enum.IsDefined(destination.Placement))throw new ArgumentException("Choose a valid destination.");
        var addresses=new HashSet<string>();
        foreach(var choice in choices){
            if(!IPAddress.TryParse(choice.Host.Address,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork||!addresses.Add(ip.ToString()))throw new ArgumentException("Select unique IPv4 devices.");
            var existing=Existing(project,choice.Host.Address);
            if(choice.ManualType is {} kind&&!EditableTypes.Contains(kind)&&kind!=existing?.Kind)throw new ArgumentException("Choose a supported device type.");
            if(existing!=null&&TypeLocked(project,existing)&&(choice.ManualType is {} selected&&selected!=existing.Kind||choice.UseAutomatic))
                throw new ArgumentException($"{existing.Name}: type is protected by existing stack, host, or child relationships. Edit those on the map first.");
        }
        Node? target=null,parent=null;IpNetwork? subnet=null;
        if(destination.Placement==DiscoveryPlacement.AssignContainer){
            target=project.Nodes.Find(n=>n.Id==destination.ContainerId&&n.IsContainer)??throw new ArgumentException("Choose an existing container.");
            if(target.Kind==NodeKind.Subnet&&(!IpNetwork.TryParse(target.Cidr,out subnet)||subnet!.Bits!=32))throw new ArgumentException("Choose a subnet with a valid IPv4 CIDR.");
            // A site/building nested inside a subnet inherits its address constraint.
            var ancestor=project.Ancestors(target).FirstOrDefault(n=>n.Kind==NodeKind.Subnet);
            if(subnet==null&&ancestor!=null&&(!IpNetwork.TryParse(ancestor.Cidr,out subnet)||subnet!.Bits!=32))throw new ArgumentException("The destination's subnet has an invalid IPv4 CIDR.");
        }
        if(destination.Placement==DiscoveryPlacement.NewSubnet){
            if(string.IsNullOrWhiteSpace(destination.SubnetName))throw new ArgumentException("Enter a name for the new subnet.");
            if(!IpNetwork.TryParse(destination.Cidr.Trim(),out subnet)||subnet!.Bits!=32)throw new ArgumentException("Enter a valid IPv4 subnet, for example 192.168.20.0/24.");
            if(destination.ParentId!=null)parent=project.Nodes.Find(n=>n.Id==destination.ParentId&&n.IsContainer)??throw new ArgumentException("Choose a valid parent container for the new subnet.");
            if(parent!=null&&(parent.Kind==NodeKind.Subnet||project.Ancestors(parent).Any(n=>n.Kind==NodeKind.Subnet)))throw new ArgumentException("Create the subnet under a site, building, VLAN or security zone outside an existing subnet.");
        }
        if(subnet!=null){
            var outside=choices.Where(c=>!subnet.Contains(IPAddress.Parse(c.Host.Address))).Select(c=>c.Host.Address).ToList();
            if(outside.Count>0)throw new ArgumentException($"These selected addresses are outside the destination subnet: {string.Join(", ",outside.Take(5))}. Change the destination or deselect them.");
        }
        int newCount=choices.Count(c=>Existing(project,c.Host.Address)==null);
        if(project.Nodes.Count+newCount+(destination.Placement==DiscoveryPlacement.NewSubnet?1:0)>ProjectLimits.MaximumNodes)throw new InvalidDataException($"Projects support at most {ProjectLimits.MaximumNodes} nodes.");
        Node? created=null;
        if(destination.Placement==DiscoveryPlacement.NewSubnet){
            created=new(){Kind=NodeKind.Subnet,Name=destination.SubnetName.Trim(),Cidr=destination.Cidr.Trim(),Width=460,Height=240,X=parent?.X+20??(project.Nodes.Count==0?100:project.Nodes.Max(n=>n.X+n.Width)+60),Y=parent?.Y+70??100};
            project.Nodes.Add(created);if(parent!=null)Containment.Assign(project,created,parent.Id);target=created;
        }
        var newHosts=choices.Where(c=>Existing(project,c.Host.Address)==null).Select(c=>c.Host).ToList();
        // Add as a batch so new free-standing cards keep distinct positions.
        var added=DiscoveryImport.Add(project,newHosts);
        var changed=new List<Node>();
        foreach(var choice in choices){
            var node=Existing(project,choice.Host.Address)!;
            var previous=added.Contains(node)?null:node;
            var assessment=choice.Host.Facts!=null?DeviceClassifier.Classify(choice.Host.Facts,previous):
                choice.Host.Classification!=null?JsonSerializer.Deserialize<DeviceClassification>(JsonSerializer.Serialize(choice.Host.Classification))!:DeviceClassifier.Classify(new(){Hostname=choice.Host.Hostname},previous);
            if(previous?.Classification?.ManualType is {} saved&&!choice.UseAutomatic&&choice.ManualType==null)assessment.ManualType=saved;
            if(choice.UseAutomatic)assessment.ManualType=null;
            if(choice.ManualType is {} manual)assessment.ManualType=manual;
            if(previous!=null&&TypeLocked(project,previous))assessment.ManualType=previous.Kind;
            DeviceClassifier.Apply(node,assessment);
            if(node.Hostname.Length==0)node.Hostname=choice.Host.Hostname;
            if(target!=null)Containment.Assign(project,node,target.Id);
            changed.Add(node);
        }
        return new(changed,created);
    }
}
