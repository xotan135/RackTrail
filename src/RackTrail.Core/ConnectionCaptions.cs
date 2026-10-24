namespace RackTrail.Core;

public record ConnectionCaption(string Text,VisualBox Box);

/// <summary>Place short captions in actual free space, never across device or container names.</summary>
public static class ConnectionCaptions
{
    public static bool Overlaps(VisualBox a,VisualBox b)=>a.X<b.X+b.Width&&a.X+a.Width>b.X&&a.Y<b.Y+b.Height&&a.Y+a.Height>b.Y;
    public static bool Crosses(IReadOnlyList<MapPoint> route,VisualBox box)
    {
        for(int i=1;i<route.Count;i++)
        {
            var a=route[i-1];var b=route[i];
            if(a.Y==b.Y&&a.Y>box.Y&&a.Y<box.Y+box.Height&&Math.Max(a.X,b.X)>box.X&&Math.Min(a.X,b.X)<box.X+box.Width)return true;
            if(a.X==b.X&&a.X>box.X&&a.X<box.X+box.Width&&Math.Max(a.Y,b.Y)>box.Y&&Math.Min(a.Y,b.Y)<box.Y+box.Height)return true;
        }
        return false;
    }
    public static Dictionary<string,ConnectionCaption> Create(Project p,IEnumerable<Connection> candidates)
    {
        var result=new Dictionary<string,ConnectionCaption>();var wanted=candidates.Where(c=>c.Kind!=ConnectionKind.Hosting&&c.LabelFor(p).Length>0).ToList();
        if(wanted.Count==0)return result;
        var obstacles=p.Nodes.Where(p.Visible).Select(ConnectionRouter.ProtectedBounds).ToList();
        var routes=p.Connections.ToDictionary(c=>c.Id,c=>MapLayout.Route(p,c));
        var bounds=VisualExporter.Bounds(p);
        foreach(var c in wanted)
        {
            var route=routes[c.Id];string text=string.Join(" ",c.LabelFor(p).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
            if(text.Length>40)text=text[..39]+"…";
            double width=Math.Max(60,text.Length*6+12),height=20;
            var segments=Enumerable.Range(1,Math.Max(0,route.Count-1)).OrderByDescending(i=>Math.Abs(route[i].X-route[i-1].X)+Math.Abs(route[i].Y-route[i-1].Y));
            bool placed=false;
            foreach(int i in segments)
            {
                var a=route[i-1];var b=route[i];
                foreach(double ratio in new[]{.5,.25,.75})
                {
                    double x=a.X+(b.X-a.X)*ratio,y=a.Y+(b.Y-a.Y)*ratio;
                    var boxes=a.Y==b.Y?new[]{new VisualBox(x-width/2,y-height-6,width,height),new(x-width/2,y+6,width,height)}:
                        new[]{new VisualBox(x+8,y-height/2,width,height),new(x-width-8,y-height/2,width,height)};
                    foreach(var box in boxes)
                    {
                        if(box.X<bounds.X||box.Y<bounds.Y||box.X+box.Width>bounds.X+bounds.Width||box.Y+box.Height>bounds.Y+bounds.Height)continue;
                        if(obstacles.Any(n=>Overlaps(n,box))||result.Values.Any(n=>Overlaps(n.Box,box))||routes.Values.Any(r=>Crosses(r,box)))continue;
                        result[c.Id]=new(text,box);placed=true;break;
                    }
                    if(placed)break;
                }
                if(placed)break;
            }
        }
        return result;
    }
}
