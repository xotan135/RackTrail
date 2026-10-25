using System.Text.Json;
using System.Text.Json.Serialization;

namespace RackTrail.Core;

public enum NodeKind { Internet, Firewall, Router, Switch, Server, Workstation, AccessPoint, Storage, Site, Building, Vlan, Subnet, SecurityZone, Printer, Hypervisor, VirtualMachine, Phones, Device, Phone, IoT }
public enum DeviceShape { RoundedRectangle, Rectangle, Circle }
public enum ConnectionKind { Network, Hosting }
public record MapPoint(double X, double Y);
public class StackMember : SceneObservable
{
    internal StackMember SnapshotCopy(){var copy=(StackMember)MemberwiseClone();copy.DetachSceneObservers();return copy;}
    private string _sceneId = Guid.NewGuid().ToString("N");
    public string Id { get => _sceneId; set => SetSceneValue(ref _sceneId, value); }
    private int _sceneNumber = 1;
    public int Number { get => _sceneNumber; set => SetSceneValue(ref _sceneNumber, value); }
    private string _sceneName = "";
    public string Name { get => _sceneName; set => SetSceneValue(ref _sceneName, value); }
    private string _sceneModel = "";
    public string Model { get => _sceneModel; set => SetSceneValue(ref _sceneModel, value); }
    private string _sceneSerialNumber = "";
    public string SerialNumber { get => _sceneSerialNumber; set => SetSceneValue(ref _sceneSerialNumber, value); }
    private string _sceneRole = "Member";
    public string Role { get => _sceneRole; set => SetSceneValue(ref _sceneRole, value); }
    private string _sceneStatus = "Unknown";
    public string Status { get => _sceneStatus; set => SetSceneValue(ref _sceneStatus, value); }
    private string _sceneNotes = "";
    public string Notes { get => _sceneNotes; set => SetSceneValue(ref _sceneNotes, value); }
}
public class Node : SceneObservable
{
    private string _sceneId = Guid.NewGuid().ToString("N");
    public string Id { get => _sceneId; set => SetSceneValue(ref _sceneId, value); }
    private string _sceneName = "Device";
    public string Name { get => _sceneName; set => SetSceneValue(ref _sceneName, value); }
    private NodeKind _sceneKind;
    public NodeKind Kind { get => _sceneKind; set => SetSceneValue(ref _sceneKind, value); }
    private string? _sceneParentId;
    public string? ParentId { get => _sceneParentId; set => SetSceneValue(ref _sceneParentId, value); }
    private double _sceneX;
    public double X { get => _sceneX; set => SetSceneValue(ref _sceneX, value); }
    private double _sceneY;
    public double Y { get => _sceneY; set => SetSceneValue(ref _sceneY, value); }
    private double _sceneWidth = 180;
    public double Width { get => _sceneWidth; set => SetSceneValue(ref _sceneWidth, value); }
    private double _sceneHeight = 76;
    public double Height { get => _sceneHeight; set => SetSceneValue(ref _sceneHeight, value); }
    private string _sceneHostname = "";
    public string Hostname { get => _sceneHostname; set => SetSceneValue(ref _sceneHostname, value); }
    private string _sceneAddresses = "";
    public string Addresses { get => _sceneAddresses; set => SetSceneValue(ref _sceneAddresses, value); }
    private string _sceneOperatingSystem = "";
    public string OperatingSystem { get => _sceneOperatingSystem; set => SetSceneValue(ref _sceneOperatingSystem, value); }
    private string _sceneModel = "";
    public string Model { get => _sceneModel; set => SetSceneValue(ref _sceneModel, value); }
    private string _sceneHypervisorPlatform = "";
    public string HypervisorPlatform { get => _sceneHypervisorPlatform; set => SetSceneValue(ref _sceneHypervisorPlatform, value); }
    private string? _sceneHostId;
    public string? HostId { get => _sceneHostId; set => SetSceneValue(ref _sceneHostId, value); }
    private DeviceShape _sceneShape;
    public DeviceShape Shape { get => _sceneShape; set => SetSceneValue(ref _sceneShape, value); }
    private bool _sceneUseDeviceImage;
    public bool UseDeviceImage { get => _sceneUseDeviceImage; set => SetSceneValue(ref _sceneUseDeviceImage, value); }
    private double _sceneImageSize = 48;
    public double ImageSize { get => _sceneImageSize; set => SetSceneValue(ref _sceneImageSize, value); }
    private bool _sceneShowDeviceInfo = true;
    public bool ShowDeviceInfo { get => _sceneShowDeviceInfo; set => SetSceneValue(ref _sceneShowDeviceInfo, value); }
    private string _sceneVendor = "";
    public string Vendor { get => _sceneVendor; set => SetSceneValue(ref _sceneVendor, value); }
    private DeviceClassification? _sceneClassification;
    public DeviceClassification? Classification { get => _sceneClassification; set => SetSceneValue(ref _sceneClassification, value); }
    private string _sceneIconKey = "auto";
    public string IconKey { get => _sceneIconKey; set => SetSceneValue(ref _sceneIconKey, value); }
    private string _sceneEmbeddedIconKey = "";
    public string EmbeddedIconKey { get => _sceneEmbeddedIconKey; set => SetSceneValue(ref _sceneEmbeddedIconKey, value); }
    private string _sceneIconPngBase64 = "";
    public string IconPngBase64 { get => _sceneIconPngBase64; set => SetSceneValue(ref _sceneIconPngBase64, value); }
    private string _sceneRole = "";
    public string Role { get => _sceneRole; set => SetSceneValue(ref _sceneRole, value); }
    private string _sceneNotes = "";
    public string Notes { get => _sceneNotes; set => SetSceneValue(ref _sceneNotes, value); }
    private string _sceneManagementInterface = "";
    public string ManagementInterface { get => _sceneManagementInterface; set => SetSceneValue(ref _sceneManagementInterface, value); }
    private string _sceneStackTechnology = "";
    public string StackTechnology { get => _sceneStackTechnology; set => SetSceneValue(ref _sceneStackTechnology, value); }
    private List<StackMember> _sceneStackMembers = [];
    public List<StackMember> StackMembers { get => _sceneStackMembers; set => SetSceneValue(ref _sceneStackMembers, value); }
    private bool _sceneShowStackMembers;
    public bool ShowStackMembers { get => _sceneShowStackMembers; set => SetSceneValue(ref _sceneShowStackMembers, value); }
    [JsonIgnore] public bool IsStack => Kind == NodeKind.Switch && StackMembers.Count > 0;
    [JsonIgnore] public string DisplayType => IsStack ? $"Switch stack ({StackMembers.Count} members)" : DeviceClassifier.Label(Kind);
    [JsonIgnore] public string VisualDetail => IsStack ? $"{StackMembers.Count} members | {Addresses}" : Addresses;
    [JsonIgnore] public double DisplayHeight => CanContainNodes && Collapsed ? 60 : IsStack && ShowStackMembers ? Math.Max(Height, 86 + StackMembers.Count * 24) : !CanContainNodes&&!UseDeviceImage&&Shape==DeviceShape.Circle?Math.Max(Height,Width):Height;
    private string _sceneCidr = "";
    public string Cidr { get => _sceneCidr; set => SetSceneValue(ref _sceneCidr, value); }
    private string _sceneGateway = "";
    public string Gateway { get => _sceneGateway; set => SetSceneValue(ref _sceneGateway, value); }
    private string _sceneVlanId = "";
    public string VlanId { get => _sceneVlanId; set => SetSceneValue(ref _sceneVlanId, value); }
    private bool _sceneCollapsed;
    public bool Collapsed { get => _sceneCollapsed; set => SetSceneValue(ref _sceneCollapsed, value); }
    private long _sceneZOrder;
    public long ZOrder { get => _sceneZOrder; set => SetSceneValue(ref _sceneZOrder, value); }
    private bool _sceneLocked;
    public bool Locked { get => _sceneLocked; set => SetSceneValue(ref _sceneLocked, value); }
    private string _sceneAccentColor = "";
    public string AccentColor { get => _sceneAccentColor; set => SetSceneValue(ref _sceneAccentColor, value); }
    private bool _sceneAllowValidationExceptions;
    public bool AllowValidationExceptions { get => _sceneAllowValidationExceptions; set => SetSceneValue(ref _sceneAllowValidationExceptions, value); }
    [JsonIgnore] public bool IsContainer => Kind is NodeKind.Site or NodeKind.Building or NodeKind.Vlan or NodeKind.Subnet or NodeKind.SecurityZone;
    [JsonIgnore] public bool CanContainNodes => IsContainer || Kind == NodeKind.Hypervisor;
    [JsonIgnore] public IEnumerable<string> Ips => Addresses.Split(new[] { ',', ';', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    internal Node SnapshotCopy()
    {
        var copy=(Node)MemberwiseClone();
        copy.DetachSceneObservers();
        copy.StackMembers=StackMembers.Select(m=>m.SnapshotCopy()).ToList();
        copy.Classification=Classification?.SnapshotCopy();
        return copy;
    }
    public override string ToString() => Name;
}
public class Connection : SceneObservable
{
    internal Connection SnapshotCopy(){var copy=(Connection)MemberwiseClone();copy.DetachSceneObservers();copy.Route=Route.ToList();return copy;}
    private ConnectionKind _sceneKind;
    public ConnectionKind Kind { get => _sceneKind; set => SetSceneValue(ref _sceneKind, value); }
    private string _sceneId = Guid.NewGuid().ToString("N");
    public string Id { get => _sceneId; set => SetSceneValue(ref _sceneId, value); }
    private string _sceneFrom = "";
    public string From { get => _sceneFrom; set => SetSceneValue(ref _sceneFrom, value); }
    private string _sceneTo = "";
    public string To { get => _sceneTo; set => SetSceneValue(ref _sceneTo, value); }
    private string _scenePorts = "";
    public string Ports { get => _scenePorts; set => SetSceneValue(ref _scenePorts, value); }
    private string? _sceneFromStackMemberId;
    public string? FromStackMemberId { get => _sceneFromStackMemberId; set => SetSceneValue(ref _sceneFromStackMemberId, value); }
    private string? _sceneToStackMemberId;
    public string? ToStackMemberId { get => _sceneToStackMemberId; set => SetSceneValue(ref _sceneToStackMemberId, value); }
    private string _sceneFromPort = "";
    public string FromPort { get => _sceneFromPort; set => SetSceneValue(ref _sceneFromPort, value); }
    private string _sceneToPort = "";
    public string ToPort { get => _sceneToPort; set => SetSceneValue(ref _sceneToPort, value); }
    private string _sceneProtocol = "";
    public string Protocol { get => _sceneProtocol; set => SetSceneValue(ref _sceneProtocol, value); }
    private string _sceneBandwidth = "";
    public string Bandwidth { get => _sceneBandwidth; set => SetSceneValue(ref _sceneBandwidth, value); }
    private string _scenePurpose = "";
    public string Purpose { get => _scenePurpose; set => SetSceneValue(ref _scenePurpose, value); }
    private List<MapPoint> _sceneRoute = [];
    public List<MapPoint> Route { get => _sceneRoute; set => SetSceneValue(ref _sceneRoute, value); }
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
