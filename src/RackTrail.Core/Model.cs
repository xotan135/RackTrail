using System.Text.Json;
using System.Text.Json.Serialization;

namespace RackTrail.Core;

public enum NodeKind { Internet, Firewall, Router, Switch, Server, Workstation, AccessPoint, Storage, Site, Building, Vlan, Subnet, SecurityZone, Printer, Hypervisor, VirtualMachine, Phones, Device, Phone, IoT }
public enum DeviceShape { RoundedRectangle, Rectangle, Circle }
public enum ConnectionKind { Network, Hosting }
public record MapPoint(double X, double Y);
public class StackMember
{
    internal StackMember SnapshotCopy() => (StackMember)MemberwiseClone();
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Number { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Role { get; set; } = "Member";
    public string Status { get; set; } = "Unknown";
    public string Notes { get; set; } = "";
}
public class Node
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Device";
    public NodeKind Kind { get; set; }
    public string? ParentId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 180;
    public double Height { get; set; } = 76;
    public string Hostname { get; set; } = "";
    public string Addresses { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string Model { get; set; } = "";
    public string HypervisorPlatform { get; set; } = "";
    public string? HostId { get; set; }
    public DeviceShape Shape { get; set; }
    public bool UseDeviceImage { get; set; }
    public double ImageSize { get; set; } = 48;
    public bool ShowDeviceInfo { get; set; } = true;
    public string Vendor { get; set; } = "";
    public DeviceClassification? Classification { get; set; }
    public string IconKey { get; set; } = "auto";
    public string EmbeddedIconKey { get; set; } = "";
    public string IconPngBase64 { get; set; } = "";
    public string Role { get; set; } = "";
    public string Notes { get; set; } = "";
    public string ManagementInterface { get; set; } = "";
    public string StackTechnology { get; set; } = "";
    public List<StackMember> StackMembers { get; set; } = [];
    public bool ShowStackMembers { get; set; }
    [JsonIgnore] public bool IsStack => Kind == NodeKind.Switch && StackMembers.Count > 0;
    [JsonIgnore] public string DisplayType => IsStack ? $"Switch stack ({StackMembers.Count} members)" : DeviceClassifier.Label(Kind);
    [JsonIgnore] public string VisualDetail => IsStack ? $"{StackMembers.Count} members | {Addresses}" : Addresses;
    [JsonIgnore] public double DisplayHeight => CanContainNodes && Collapsed ? 60 : IsStack && ShowStackMembers ? Math.Max(Height, 86 + StackMembers.Count * 24) : !CanContainNodes&&!UseDeviceImage&&Shape==DeviceShape.Circle?Math.Max(Height,Width):Height;
    public string Cidr { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string VlanId { get; set; } = "";
    public bool Collapsed { get; set; }
    public long ZOrder { get; set; }
    public bool Locked { get; set; }
    public string AccentColor { get; set; } = "";
    public bool AllowValidationExceptions { get; set; }
    [JsonIgnore] public bool IsContainer => Kind is NodeKind.Site or NodeKind.Building or NodeKind.Vlan or NodeKind.Subnet or NodeKind.SecurityZone;
    [JsonIgnore] public bool CanContainNodes => IsContainer || Kind == NodeKind.Hypervisor;
    [JsonIgnore] public IEnumerable<string> Ips => Addresses.Split(new[] { ',', ';', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    internal Node SnapshotCopy()
    {
        var copy=(Node)MemberwiseClone();
        copy.StackMembers=StackMembers.Select(m=>m.SnapshotCopy()).ToList();
        copy.Classification=Classification?.SnapshotCopy();
        return copy;
    }
    public override string ToString() => Name;
}
public class Connection
{
    internal Connection SnapshotCopy(){var copy=(Connection)MemberwiseClone();copy.Route=Route.ToList();return copy;}
    public ConnectionKind Kind { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Ports { get; set; } = "";
    public string? FromStackMemberId { get; set; }
    public string? ToStackMemberId { get; set; }
    public string FromPort { get; set; } = "";
    public string ToPort { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string Bandwidth { get; set; } = "";
    public string Purpose { get; set; } = "";
    public List<MapPoint> Route { get; set; } = [];
    [JsonIgnore] public string Label => string.Join(" · ", new[] { Kind==ConnectionKind.Hosting?"Hosted VM":"", Ports, Protocol, Bandwidth, Purpose }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string LabelFor(Project project)
    {
        string End(string nodeId, string? memberId, string port)
        {
            var node = project.Nodes.Find(n => n.Id == nodeId);
            var member = node?.StackMembers.Find(m => m.Id == memberId);
            return string.Join(" / ", new[] { memberId == null ? "" : member == null ? "missing member" : $"member {member.Number}", port }.Where(s => s.Length > 0));
        }
        var from = End(From, FromStackMemberId, FromPort);
        var to = End(To, ToStackMemberId, ToPort);
        var endpointLabel = from.Length == 0 && to.Length == 0 ? "" : $"{(from.Length == 0 ? "stack/device" : from)} -> {(to.Length == 0 ? "stack/device" : to)}";
        return string.Join(" · ", new[] { endpointLabel, Label }.Where(s => s.Length > 0));
    }
}
public class Project
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "Untitled network";
    public List<Node> Nodes { get; set; } = [];
    public List<Connection> Connections { get; set; } = [];
    public IEnumerable<Node> Ancestors(Node node)
    {
        var seen = new HashSet<string> { node.Id };
        while (node.ParentId is { } id && seen.Add(id) && Nodes.Find(n => n.Id == id) is { } parent) { yield return parent; node = parent; }
    }
    public bool Visible(Node node) => !Ancestors(node).Any(n => n.Collapsed);
    public Node? VisibleEndpoint(string id) { var node = Nodes.Find(n => n.Id == id); return node is null ? null : Ancestors(node).LastOrDefault(n => n.Collapsed) ?? node; }
}
public static class ProjectStorage
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static string Serialize(Project p) => JsonSerializer.Serialize(p, Options);
    public static Project Deserialize(string text)
    {
        var p = JsonSerializer.Deserialize<Project>(text, Options) ?? throw new InvalidDataException("Empty project.");
        if (p.FormatVersion != 1) throw new InvalidDataException("Unsupported project version.");
        ProjectLimits.ValidateHierarchy(p);
        var byId=p.Nodes.ToDictionary(n=>n.Id,StringComparer.Ordinal);
        foreach (var n in p.Nodes)
        {
            if (!double.IsFinite(n.X + n.Y + n.Width + n.Height) || n.Width < 30 || n.Height < 30) throw new InvalidDataException("Invalid node geometry.");
            if(!double.IsFinite(n.ImageSize)||n.ImageSize<16||n.ImageSize>256)throw new InvalidDataException("Device image size must be 16–256.");

            if(n.HostId is {} hostId&&((!byId.TryGetValue(hostId,out var host)||host.Kind!=NodeKind.Hypervisor)||n.Kind!=NodeKind.VirtualMachine))throw new InvalidDataException("Invalid virtual machine host.");
        }
        if (p.Connections.Any(c => !byId.ContainsKey(c.From) || !byId.ContainsKey(c.To))) throw new InvalidDataException("Missing connection endpoint.");
        return p;
    }
    public static Project Clone(Project p) => Deserialize(Serialize(p));
    private static readonly SemaphoreSlim saveGate=new(1,1);
    public static async Task SaveAsync(Project p,string path,CancellationToken cancellation=default)
    {
        await saveGate.WaitAsync(cancellation).ConfigureAwait(false);
        string? temp=null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            temp=path+".tmp";
            await File.WriteAllTextAsync(temp,Serialize(p),cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();File.Move(temp,path,true);temp=null;
        }
        finally
        {
            if(temp!=null){try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
            saveGate.Release();
        }
    }
    public static async Task<Project> LoadAsync(string path) => Deserialize(await File.ReadAllTextAsync(path));
}
