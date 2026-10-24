namespace RackTrail.Core;

public static class MapLayout
{
    public static void Arrange(Project p)
    {
        if(p.Nodes.Any(n=>n.Locked))throw new InvalidDataException("Unlock items before running Auto layout. Locked positions have been preserved.");
        double Size(Node n) { if (!n.CanContainNodes) return n.Width = n.IsStack?260:180; var children = p.Nodes.Where(x => x.ParentId == n.Id).ToList(); n.Width = Math.Max(240, children.Sum(Size) + Math.Max(0, children.Count - 1) * 30 + 40); n.Height = Math.Max(160, children.Select(x => x.CanContainNodes?x.Height:x.DisplayHeight).DefaultIfEmpty(80).Max() + 100); return n.Width; }
        void Place(Node n, double x, double y) { n.X = x; n.Y = y; double cx = x + 20; foreach (var child in p.Nodes.Where(z => z.ParentId == n.Id)) { Place(child, cx, y + 70); cx += child.Width + 30; } }
        var roots = p.Nodes.Where(n => n.ParentId == null).ToList(); foreach (var n in roots) Size(n);
        double row = 40;
        foreach (var n in roots.Where(n => !n.CanContainNodes)) { Place(n, 480, row); row += n.DisplayHeight+54; }
        double col = 60; foreach (var n in roots.Where(n => n.CanContainNodes)) { Place(n, col, row); col += n.Width + 40; }
        foreach (var c in p.Connections) c.Route = [];
    }
    public static List<MapPoint> Route(Project p, Connection c)
        => ConnectionRouter.Route(p,c);
    public static List<MapPoint> PreviewRoute(Project p,Connection c) => LegacyRoute(p,c);
    internal static List<MapPoint> LegacyRoute(Project p, Connection c)
    {
        var a = p.VisibleEndpoint(c.From); var b = p.VisibleEndpoint(c.To);
        if (a == null || b == null || a.Id == b.Id) return [];
        if (c.Route.Count > 0 && a.Id == c.From && b.Id == c.To) return c.Route;
        if(c.Kind==ConnectionKind.Hosting&&b.ParentId==a.Id)return [new(a.X+10,a.Y+55),new(a.X+10,b.Y+b.DisplayHeight/2),new(b.X,b.Y+b.DisplayHeight/2)];
        MapPoint? MemberPoint(Node node,string? memberId,bool right)
        {
            if(!node.IsStack||!node.ShowStackMembers||memberId==null)return null;
            int row=node.StackMembers.OrderBy(m=>m.Number).ToList().FindIndex(m=>m.Id==memberId);
            return row<0?null:new(node.X+(right?node.Width:0),node.Y+78+24*row);
        }
        var fromMember=MemberPoint(a,c.FromStackMemberId,b.X>=a.X);var toMember=MemberPoint(b,c.ToStackMemberId,a.X>=b.X);
        if(fromMember!=null||toMember!=null)
        {
            var source=fromMember??new MapPoint(a.X+(b.X>=a.X?a.Width:0),a.Y+38);var target=toMember??new MapPoint(b.X+(a.X>=b.X?b.Width:0),b.Y+38);double mid=(source.X+target.X)/2;
            return [source,new(mid,source.Y),new(mid,target.Y),target];
        }
        var start = new MapPoint(a.X + a.Width / 2, a.Y + (a.DisplayHeight)); var end = new MapPoint(b.X + b.Width / 2, b.Y);
        if (end.Y < start.Y) { start = new(a.X + a.Width, a.Y + 38); end = new(b.X, b.Y + 38); var mid = (start.X + end.X) / 2; return [start, new(mid, start.Y), new(mid, end.Y), end]; }
        var y = (start.Y + end.Y) / 2; return [start, new(start.X, y), new(end.X, y), end];
    }
}
public static class SampleNetwork
{
    public static Project Create()
    {
        var p = new Project { Name = "RackTrail · Headquarters" };
        Node Add(string id, string name, NodeKind kind, string? parent = null, string ips = "", string role = "") { var n = new Node { Id = id, Name = name, Kind = kind, ParentId = parent, Addresses = ips, Role = role }; p.Nodes.Add(n); return n; }
        Add("wan", "Internet", NodeKind.Internet); Add("fw", "Edge firewall", NodeKind.Firewall, ips: "203.0.113.2", role: "Internet gateway"); Add("core", "Core switch", NodeKind.Switch, role: "Distribution");
        var site = Add("hq", "HEADQUARTERS", NodeKind.Site);
        var server = Add("servers", "Server subnet", NodeKind.Subnet, site.Id); server.Cidr = "10.10.10.0/24"; server.Gateway = "10.10.10.1"; server.VlanId = "10";
        Add("ad", "AD / DNS", NodeKind.Server, server.Id, "10.10.10.10", "Directory & DNS").OperatingSystem = "Windows Server 2025";
        Add("app", "Application server", NodeKind.Server, server.Id, "10.10.10.20", "Internal applications").OperatingSystem = "Ubuntu 24.04";
        Add("nas", "Backup storage", NodeKind.Storage, server.Id, "10.10.10.30", "Nightly backups");
        var users = Add("users", "User subnet", NodeKind.Subnet, site.Id); users.Cidr = "10.10.20.0/24"; users.Gateway = "10.10.20.1"; users.VlanId = "20";
        Add("pc", "Workstation 01", NodeKind.Workstation, users.Id, "10.10.20.101", "Engineering"); Add("ap", "Office Wi-Fi", NodeKind.AccessPoint, users.Id, "10.10.20.2", "Wireless access");
        void Link(string a, string b, string bandwidth, string purpose) => p.Connections.Add(new() { From = a, To = b, Bandwidth = bandwidth, Purpose = purpose });
        Link("wan", "fw", "1 Gbps", "WAN"); Link("fw", "core", "10 Gbps", "LAN trunk"); foreach (var id in new[] { "ad", "app", "nas", "pc", "ap" }) Link("core", id, id == "nas" ? "10 Gbps" : "1 Gbps", "Ethernet");
        MapLayout.Arrange(p); return p;
    }
}
