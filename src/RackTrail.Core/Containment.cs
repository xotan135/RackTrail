namespace RackTrail.Core;

/// <summary>Model-level geometry constraints shared by gestures, properties and layout.</summary>
public static class Containment
{
    public const double Padding=20;
    public const double Header=70;
    public static IEnumerable<Node> Subtree(Project p,Node node) => p.Nodes.Where(n=>n.Id==node.Id||p.Ancestors(n).Any(a=>a.Id==node.Id));
    private static double FullHeight(Node n)=>n.CanContainNodes?n.Height:n.DisplayHeight;
    public static bool IsInside(Node node,Node parent)=>node.X>=parent.X+Padding-.001&&node.Y>=parent.Y+Header-.001&&node.X+node.Width<=parent.X+parent.Width-Padding+.001&&node.Y+FullHeight(node)<=parent.Y+parent.Height-Padding+.001;
    private static void Translate(Project p,Node node,double dx,double dy)
    {
        if(Math.Abs(dx)<.00001&&Math.Abs(dy)<.00001)return;
        var ids=Subtree(p,node).Select(n=>n.Id).ToHashSet();foreach(var n in p.Nodes.Where(n=>ids.Contains(n.Id))){n.X+=dx;n.Y+=dy;}
        foreach(var c in p.Connections.Where(c=>ids.Contains(c.From)||ids.Contains(c.To)))c.Route=[];
    }
    private static void FitParent(Project p,Node node)
    {
        ProjectLimits.ValidateHierarchy(p);
        var nodes=p.Nodes.ToDictionary(n=>n.Id,StringComparer.Ordinal);
        int remaining=ProjectLimits.MaximumContainmentDepth;
        while(node.ParentId is {} parentId&&nodes.TryGetValue(parentId,out var parent))
        {
            if(remaining--==0)throw new InvalidDataException("Container nesting limit exceeded.");
            double oldWidth=parent.Width,oldHeight=parent.Height;
            parent.Width=Math.Max(parent.Width,node.Width+2*Padding);parent.Height=Math.Max(parent.Height,FullHeight(node)+Header+Padding);
            if(parent.Width!=oldWidth||parent.Height!=oldHeight)foreach(var c in p.Connections.Where(c=>c.From==parent.Id||c.To==parent.Id))c.Route=[];
            double x=Math.Clamp(node.X,parent.X+Padding,parent.X+parent.Width-Padding-node.Width);
            double y=Math.Clamp(node.Y,parent.Y+Header,parent.Y+parent.Height-Padding-FullHeight(node));
            Translate(p,node,x-node.X,y-node.Y);
            node=parent;
        }
    }
    public static void Assign(Project p,Node node,string? parentId)
    {
        var parent=p.Nodes.Find(n=>n.Id==parentId);
        if(parentId!=null&&(parent==null||!parent.CanContainNodes||parent.Id==node.Id||p.Ancestors(parent).Any(n=>n.Id==node.Id)))throw new InvalidOperationException("Choose a valid container outside the device's own subtree.");
        var depths=ProjectLimits.ValidateHierarchy(p);
        if(parent!=null)
        {
            var children=p.Nodes.ToLookup(n=>n.ParentId??"");
            var pending=new Queue<Node>();pending.Enqueue(node);int subtreeHeight=0;
            while(pending.TryDequeue(out var descendant)){subtreeHeight=Math.Max(subtreeHeight,depths[descendant.Id]-depths[node.Id]);foreach(var child in children[descendant.Id])pending.Enqueue(child);}
            if(depths[parent.Id]+1+subtreeHeight>ProjectLimits.MaximumContainmentDepth)throw new InvalidDataException($"Container nesting supports at most {ProjectLimits.MaximumContainmentDepth} levels.");
        }
        bool changed=node.ParentId!=parentId;node.ParentId=parentId;if(parent==null)return;
        bool outside=!IsInside(node,parent);FitParent(p,node);
        if(outside||changed)
        {
            var siblings=p.Nodes.Where(n=>n.ParentId==parent.Id&&n.Id!=node.Id).ToList();
            bool Overlap(Node other)=>node.X<other.X+other.Width+10&&node.X+node.Width+10>other.X&&node.Y<other.Y+FullHeight(other)+10&&node.Y+FullHeight(node)+10>other.Y;
            if(siblings.Any(Overlap))
            {
                double y=siblings.Max(n=>n.Y+FullHeight(n))+Padding;
                parent.Height=Math.Max(parent.Height,y-parent.Y+FullHeight(node)+Padding);
                Translate(p,node,parent.X+Padding-node.X,y-node.Y);FitParent(p,parent);
            }
        }
    }
    public static void Move(Project p,Node node,double dx,double dy)
    {
        if(!double.IsFinite(dx+dy))return;
        var parent=p.Nodes.Find(n=>n.Id==node.ParentId);
        if(parent!=null)
        {
            FitParent(p,node);
            dx=Math.Clamp(node.X+dx,parent.X+Padding,parent.X+parent.Width-Padding-node.Width)-node.X;
            dy=Math.Clamp(node.Y+dy,parent.Y+Header,parent.Y+parent.Height-Padding-FullHeight(node))-node.Y;
        }
        Translate(p,node,dx,dy);
    }
    public static void Resize(Project p,Node node,double width,double height)
    {
        if(!double.IsFinite(width+height))return;
        var children=p.Nodes.Where(n=>n.ParentId==node.Id).ToList();
        double minWidth=node.CanContainNodes?220:100,minHeight=node.CanContainNodes?140:60;
        if(children.Count>0){minWidth=Math.Max(minWidth,children.Max(n=>n.X+n.Width-node.X+Padding));minHeight=Math.Max(minHeight,children.Max(n=>n.Y+FullHeight(n)-node.Y+Padding));}
        node.Width=Math.Max(width,minWidth);node.Height=Math.Max(height,minHeight);
        if(!node.CanContainNodes&&!node.UseDeviceImage&&node.Shape==DeviceShape.Circle)node.Height=node.Width=Math.Max(node.Width,node.Height);
        FitParent(p,node);
        foreach(var c in p.Connections.Where(c=>c.From==node.Id||c.To==node.Id))c.Route=[];
    }
    public static void Repair(Project p)
    {
        var depths=ProjectLimits.ValidateHierarchy(p);
        var nodes=p.Nodes.ToDictionary(n=>n.Id,StringComparer.Ordinal);
        var ordered=p.Nodes.OrderBy(n=>depths[n.Id]).ToList();
        var changed=new HashSet<string>();
        foreach(var node in ordered.AsEnumerable().Reverse())
        {
            if(node.ParentId is not {} id)continue;var parent=nodes[id];
            double width=Math.Max(parent.Width,node.Width+2*Padding),height=Math.Max(parent.Height,FullHeight(node)+Header+Padding);
            if(width!=parent.Width||height!=parent.Height)changed.Add(id);
            parent.Width=width;parent.Height=height;
        }
        var offsets=new Dictionary<string,(double X,double Y)>();
        foreach(var node in ordered)
        {
            double dx=0,dy=0;
            if(node.ParentId is {} id)
            {
                var parent=nodes[id];var inherited=offsets[id];node.X+=inherited.X;node.Y+=inherited.Y;
                dx=Math.Clamp(node.X,parent.X+Padding,parent.X+parent.Width-Padding-node.Width)-node.X;
                dy=Math.Clamp(node.Y,parent.Y+Header,parent.Y+parent.Height-Padding-FullHeight(node))-node.Y;
                node.X+=dx;node.Y+=dy;dx+=inherited.X;dy+=inherited.Y;
            }
            offsets[node.Id]=(dx,dy);if(dx!=0||dy!=0)changed.Add(node.Id);
        }
        foreach(var connection in p.Connections)if(changed.Contains(connection.From)||changed.Contains(connection.To))connection.Route=[];
    }
}
