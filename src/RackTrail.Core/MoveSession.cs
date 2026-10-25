namespace RackTrail.Core;

/// <summary>Hierarchy and lock checks happen once per gesture, before any model mutation.</summary>
public sealed class MoveSession
{
    private sealed record Group(Node Root, Node? Parent, Node[] Members, Connection[] Links);
    private readonly Group[] groups;
    private readonly Node[] guideNodes;
    private readonly HashSet<string> excluded;
    private Dictionary<(bool Vertical,string Parent,long Cell),List<(Node Node,double Axis)>> guideBins=[];
    private double guideTolerance;
    public IReadOnlyList<Connection> AffectedConnections {get;}

    public MoveSession(Project project, ISet<string> selection, SceneIndex index)
    {
        groups = project.Nodes.Where(n => selection.Contains(n.Id) && !index.Parents[n.Id].Any(a => selection.Contains(a.Id)))
            .Where(n => index.Movable[n.Id]).Select(root =>
            {
                var members = project.Nodes.Where(n => n == root || index.Parents[n.Id].Contains(root)).ToArray();
                var ids = members.Select(n => n.Id).ToHashSet();
                return new Group(root, root.ParentId is { } id ? index.Nodes[id] : null, members,
                    project.Connections.Where(c => ids.Contains(c.From) || ids.Contains(c.To)).ToArray());
            }).ToArray();
        excluded=groups.SelectMany(g=>g.Members).Select(n=>n.Id).ToHashSet();
        AffectedConnections=groups.SelectMany(g=>g.Links).Distinct().ToArray();
        guideNodes=index.PaintOrder.Where(n=>!excluded.Contains(n.Id)).ToArray();
    }
    public bool Move(double dx,double dy)
    {
        if(!double.IsFinite(dx+dy))return false;
        bool changed=false;foreach(var group in groups)changed|=MoveGroup(group,dx,dy);return changed;
    }
    public void Snap()
    {
        foreach(var group in groups)MoveGroup(group,Math.Round(group.Root.X/10)*10-group.Root.X,Math.Round(group.Root.Y/10)*10-group.Root.Y);
    }
    private static bool MoveGroup(Group group,double dx,double dy)
    {
        var root=group.Root;double x=dx,y=dy;
        if(group.Parent is {} parent)
        {
            double height=root.CanContainNodes?root.Height:root.DisplayHeight;
            x=Math.Clamp(root.X+dx,parent.X+Containment.Padding,parent.X+parent.Width-Containment.Padding-root.Width)-root.X;
            y=Math.Clamp(root.Y+dy,parent.Y+Containment.Header,parent.Y+parent.Height-Containment.Padding-height)-root.Y;
        }
        if(x==0&&y==0)return false;
        foreach(var node in group.Members){node.X+=x;node.Y+=y;}
        foreach(var link in group.Links)if(link.Route.Count>0)link.Route=[];
        return true;
    }
    public IEnumerable<AlignmentGuide> Guides(double tolerance,Func<Node,bool> visible)
    {
        if(guideTolerance!=tolerance)
        {
            guideTolerance=tolerance;guideBins=[];
            foreach(var n in guideNodes)
                foreach(bool vertical in new[]{true,false})
                    foreach(double axis in vertical?new[]{n.X,n.X+n.Width/2,n.X+n.Width}:new[]{n.Y,n.Y+n.DisplayHeight/2,n.Y+n.DisplayHeight})
                    {
                        var key=(vertical,n.ParentId??"",(long)Math.Floor(axis/tolerance));
                        if(!guideBins.TryGetValue(key,out var list))guideBins[key]=list=[];list.Add((n,axis));
                    }
        }
        var result=new HashSet<AlignmentGuide>();
        foreach(var group in groups)
        {
            var n=group.Root;
            foreach(bool vertical in new[]{true,false})
                foreach(double axis in vertical?new[]{n.X,n.X+n.Width/2,n.X+n.Width}:new[]{n.Y,n.Y+n.DisplayHeight/2,n.Y+n.DisplayHeight})
                    for(long cell=(long)Math.Floor(axis/tolerance)-1;cell<=(long)Math.Floor(axis/tolerance)+1;cell++)
                        if(guideBins.TryGetValue((vertical,n.ParentId??"",cell),out var matches))
                            foreach(var (other,value) in matches)
                                if(Math.Abs(axis-value)<=tolerance&&visible(other))
                                    result.Add(new(vertical,value,vertical?Math.Min(n.Y,other.Y)-15:Math.Min(n.X,other.X)-15,vertical?Math.Max(n.Y+n.DisplayHeight,other.Y+other.DisplayHeight)+15:Math.Max(n.X+n.Width,other.X+other.Width)+15));
        }
        return result;
    }

}
