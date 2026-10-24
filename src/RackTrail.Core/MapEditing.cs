namespace RackTrail.Core;

public enum Alignment { Left, Center, Right, Top, Middle, Bottom }
public record AlignmentGuide(bool Vertical, double Position, double Start, double End);

/// <summary>Shared editor geometry and paint order, independent of UI selection.</summary>
public static class MapEditing
{
    public static bool IsLocked(Project p, Node n) => n.Locked || p.Ancestors(n).Any(a => a.Locked);
    public static bool CanMove(Project p, Node n) => !IsLocked(p,n) && !p.Nodes.Any(c => c.Locked && p.Ancestors(c).Any(a => a.Id==n.Id));
    public static long Layer(Project p, Node n) => p.Ancestors(n).Select(a => a.ZOrder).Append(n.ZOrder).Max();
    public static long ConnectionLayer(Project p, Connection c) => new[]{p.VisibleEndpoint(c.From),p.VisibleEndpoint(c.To)}.OfType<Node>().Select(n=>Layer(p,n)).DefaultIfEmpty(0).Max();
    public static List<Node> PaintOrder(Project p) => p.Nodes.Where(p.Visible).OrderBy(n=>Layer(p,n))
        .ThenBy(n=>n.CanContainNodes?0:1).ThenBy(n=>n.CanContainNodes?p.Ancestors(n).Count():0).ToList();
    public static bool ValidColor(string? value) => value is {Length:7} && value[0]=='#' && value.Skip(1).All(Uri.IsHexDigit);
    public static long Front(Project p)
    {
        if(p.Nodes.Count==0)return 0;
        if(p.Nodes.Max(n=>n.ZOrder)==long.MaxValue)
        {
            var ranks=p.Nodes.Select(n=>n.ZOrder).Distinct().Order().Select((z,i)=>(z,i)).ToDictionary(x=>x.z,x=>(long)x.i);
            foreach(var n in p.Nodes)n.ZOrder=ranks[n.ZOrder];
        }
        return p.Nodes.Max(n=>n.ZOrder)+1;
    }

    public static void Reorder(Project p, IEnumerable<Node> selection, bool front)
    {
        var chosen=selection.SelectMany(n=>Containment.Subtree(p,n)).Select(n=>n.Id).ToHashSet();
        // Normalize first so repeated front/back operations cannot exhaust the integer range.
        var levels=p.Nodes.Select(n=>n.ZOrder).Distinct().Order().Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>(long)x.i+1);
        foreach(var n in p.Nodes)n.ZOrder=levels[n.ZOrder];
        var selectedLevels=p.Nodes.Where(n=>chosen.Contains(n.Id)).Select(n=>n.ZOrder).Distinct().Order().Select((z,i)=>(z,i)).ToDictionary(x=>x.z,x=>(long)x.i);
        long target=front?Front(p):-selectedLevels.Count;
        foreach(var n in p.Nodes.Where(n=>chosen.Contains(n.Id)))n.ZOrder=target+selectedLevels[n.ZOrder];
    }

    public static MapPoint OpenPosition(Project p, double centerX, double centerY, double width, double height)
    {
        var obstacles=p.Nodes.Where(p.Visible).ToList();
        double x=centerX-width/2,y=centerY-height/2;
        bool Free(double px,double py)=>obstacles.All(n=>px+width+20<=n.X||px>=n.X+n.Width+20||py+height+20<=n.Y||py>=n.Y+n.DisplayHeight+20);
        if(Free(x,y))return new(x,y);
        // Try existing edges as well as a small grid, ordered by distance from the viewport center.
        var candidates=new List<MapPoint>();
        foreach(var n in obstacles){candidates.Add(new(n.X+n.Width+20,y));candidates.Add(new(n.X-width-20,y));candidates.Add(new(x,n.Y+n.DisplayHeight+20));candidates.Add(new(x,n.Y-height-20));}
        for(int row=-8;row<=8;row++)for(int col=-8;col<=8;col++)candidates.Add(new(x+col*(width+20),y+row*(height+20)));
        return candidates.OrderBy(a=>Math.Pow(a.X-x,2)+Math.Pow(a.Y-y,2)).First(a=>Free(a.X,a.Y));
    }

    public static void FitContents(Project p, Node n)
    {
        if(!n.CanContainNodes||IsLocked(p,n))return;
        var children=p.Nodes.Where(c=>c.ParentId==n.Id).ToList();
        if(children.Count>0 && children.All(c=>CanMove(p,c)))
        {
            double dx=n.X+Containment.Padding-children.Min(c=>c.X),dy=n.Y+Containment.Header-children.Min(c=>c.Y);
            // Translate as a group; resizing afterward preserves the relationships within it.
            foreach(var c in children)Containment.Move(p,c,dx,dy);
        }
        Containment.Resize(p,n,220,120);
    }

    public static List<Node> SelectionRoots(Project p, ISet<string> ids) => p.Nodes.Where(n=>ids.Contains(n.Id)&&!p.Ancestors(n).Any(a=>ids.Contains(a.Id))).ToList();
    public static void ArrangeSelection(Project p, List<Node> nodes, Alignment? alignment, bool horizontal=false)
    {
        if(nodes.Count<(alignment.HasValue?2:3))return;
        if(nodes.Any(n=>!CanMove(p,n)))throw new InvalidDataException("Unlock the selected items before aligning or spacing them.");
        if(nodes.Select(n=>n.ParentId).Distinct().Count()!=1)throw new InvalidDataException("Select items in the same container to align or space them.");
        double left=nodes.Min(n=>n.X),right=nodes.Max(n=>n.X+n.Width),top=nodes.Min(n=>n.Y),bottom=nodes.Max(n=>n.Y+n.DisplayHeight);
        var positions=new Dictionary<Node,MapPoint>();
        if(alignment is {} mode)
            foreach(var n in nodes)positions[n]=new(mode switch {Alignment.Left=>left,Alignment.Center=>(left+right-n.Width)/2,Alignment.Right=>right-n.Width,_=>n.X},mode switch {Alignment.Top=>top,Alignment.Middle=>(top+bottom-n.DisplayHeight)/2,Alignment.Bottom=>bottom-n.DisplayHeight,_=>n.Y});
        else
        {
            var sorted=nodes.OrderBy(n=>horizontal?n.X:n.Y).ToList();
            double gap=((horizontal?right-left:bottom-top)-nodes.Sum(n=>horizontal?n.Width:n.DisplayHeight))/(nodes.Count-1);
            if(gap<0)throw new InvalidDataException("Spread the outer items farther apart before distributing them without overlap.");
            double cursor=horizontal?left:top;
            foreach(var n in sorted){positions[n]=new(horizontal?cursor:n.X,horizontal?n.Y:cursor);cursor+=(horizontal?n.Width:n.DisplayHeight)+gap;}
        }
        foreach(var (n,pos) in positions)Containment.Move(p,n,pos.X-n.X,pos.Y-n.Y);
    }

    public static List<AlignmentGuide> Guides(Project p, ISet<string> selection, double tolerance, Func<Node,bool> visible)
    {
        var moving=SelectionRoots(p,selection).Where(n=>CanMove(p,n)).ToList();
        var excluded=moving.SelectMany(n=>Containment.Subtree(p,n)).Select(n=>n.Id).ToHashSet();
        var guides=new List<AlignmentGuide>();
        foreach(var n in moving)
        foreach(var other in p.Nodes.Where(o=>!excluded.Contains(o.Id)&&o.ParentId==n.ParentId&&visible(o)))
        {
            foreach(var x in new[]{n.X,n.X+n.Width/2,n.X+n.Width})
            foreach(var ox in new[]{other.X,other.X+other.Width/2,other.X+other.Width})
                if(Math.Abs(x-ox)<=tolerance)guides.Add(new(true,ox,Math.Min(n.Y,other.Y)-15,Math.Max(n.Y+n.DisplayHeight,other.Y+other.DisplayHeight)+15));
            foreach(var y in new[]{n.Y,n.Y+n.DisplayHeight/2,n.Y+n.DisplayHeight})
            foreach(var oy in new[]{other.Y,other.Y+other.DisplayHeight/2,other.Y+other.DisplayHeight})
                if(Math.Abs(y-oy)<=tolerance)guides.Add(new(false,oy,Math.Min(n.X,other.X)-15,Math.Max(n.X+n.Width,other.X+other.Width)+15));
        }
        return guides.Distinct().ToList();
    }
}
