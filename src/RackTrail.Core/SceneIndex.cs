namespace RackTrail.Core;

/// <summary>Built once at a geometry boundary, never on pan, zoom or hover.</summary>
public sealed class SceneIndex
{
    public IReadOnlyDictionary<string, Node> Nodes { get; }
    public IReadOnlyDictionary<string, Node[]> Parents { get; }
    public IReadOnlyDictionary<string, Node> Endpoints { get; }
    public ILookup<string,Node> Children {get;}
    public IReadOnlyDictionary<string,Connection[]> Incident {get;}
    public IReadOnlyDictionary<string,bool> Locked {get;}
    public IReadOnlyDictionary<string,bool> Movable {get;}
    public IReadOnlyDictionary<string, long> Layers { get; }
    public IReadOnlyList<Node> PaintOrder { get; }
    public IReadOnlyDictionary<string, int> PaintRanks { get; }
    public SpatialIndex<string> NodeBounds { get; private set; } = new();
    public SceneIndex(Project project)
    {
        var depths = ProjectLimits.ValidateHierarchy(project);
        var nodes = project.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var parents = new Dictionary<string, Node[]>();
        var endpoints = new Dictionary<string, Node>();
        var layers = new Dictionary<string, long>();
        foreach (var n in project.Nodes.OrderBy(n => depths[n.Id]))
        {
            var ancestors = n.ParentId is { } id ? new[] { nodes[id] }.Concat(parents[id]).ToArray() : [];
            parents[n.Id] = ancestors;
            endpoints[n.Id] = ancestors.LastOrDefault(a => a.Collapsed) ?? n;
            layers[n.Id] = ancestors.Select(a => a.ZOrder).Append(n.ZOrder).Max();
        }
        var incident=nodes.Keys.ToDictionary(id=>id,_=>new List<Connection>());
        foreach(var c in project.Connections){if(incident.TryGetValue(c.From,out var from))from.Add(c);if(c.To!=c.From&&incident.TryGetValue(c.To,out var to))to.Add(c);}
        Incident=incident.ToDictionary(pair=>pair.Key,pair=>pair.Value.ToArray());
        Locked=nodes.Values.ToDictionary(n=>n.Id,n=>n.Locked||parents[n.Id].Any(a=>a.Locked));
        var lockedDescendants=nodes.Values.Where(n=>n.Locked).SelectMany(n=>parents[n.Id]).Select(n=>n.Id).ToHashSet();
        Movable=nodes.Keys.ToDictionary(id=>id,id=>!Locked[id]&&!lockedDescendants.Contains(id));
        Children=project.Nodes.ToLookup(n=>n.ParentId??"",StringComparer.Ordinal);
        Nodes = nodes; Parents = parents; Endpoints = endpoints; Layers = layers;
        var order = project.Nodes.Where(n => endpoints[n.Id] == n).OrderBy(n => layers[n.Id])
            .ThenBy(n => n.CanContainNodes ? 0 : 1).ThenBy(n => n.CanContainNodes ? depths[n.Id] : 0).ToArray();
        PaintOrder = order;
        PaintRanks = order.Select((n, i) => (n.Id, Rank: i)).ToDictionary(x => x.Id, x => x.Rank);
        RefreshBounds();
    }
    public void RefreshBounds()
    {
        var bounds=new SpatialIndex<string>();
        foreach(var n in PaintOrder)bounds.Add(n.Id,new(n.X,n.Y,n.Width,n.DisplayHeight));
        NodeBounds=bounds;
    }
}
