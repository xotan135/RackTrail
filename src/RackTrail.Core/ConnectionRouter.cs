using System.Runtime.CompilerServices;

namespace RackTrail.Core;

/// <summary>Shared, deterministic orthogonal routing for the editor and both visual exports.</summary>
public static class ConnectionRouter
{
    private sealed class Cache { public int Stamp; public bool Ready; public RoutingSnapshot? Snapshot; }
    private static readonly ConditionalWeakTable<Project,Cache> Caches = new();
    private record Box(double Left,double Top,double Right,double Bottom);
    private record Port(MapPoint Edge,MapPoint Stub,int Axis);
    private record Ends(Connection Link,Node A,Node B,List<Port> Sources,List<Port> Targets);

    public static VisualBox ProtectedBounds(Node n)
    {
        double height=n.CanContainNodes&&!n.Collapsed?
            Math.Max(56,NodePresentation.Create(n).Labels.Max(l=>l.Y+l.Size*1.4)-n.Y+8):n.DisplayHeight;
        return new(n.X-8,n.Y-8,n.Width+16,height+16);
    }

    public static void Invalidate(Project p) => Caches.Remove(p);
    public static bool ShowLabel(Project p) => p.Connections.Count <= 30;
    public static MapPoint LabelPoint(IReadOnlyList<MapPoint> route)
    {
        if(route.Count==0)return new(0,0);
        int best=1;double length=-1;
        for(int i=1;i<route.Count;i++){double d=Distance(route[i-1],route[i]);if(d>length){length=d;best=i;}}
        return route.Count<2?route[0]:new((route[best-1].X+route[best].X)/2,(route[best-1].Y+route[best].Y)/2);
    }
    private static double Distance(MapPoint a,MapPoint b)=>Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y);
    // Compatibility for callers mutating POCOs directly. UI uses revision-owned snapshots instead.
    public static List<MapPoint> Route(Project p,Connection c) => Snapshot(p).Route(c.Id).ToList();
    public static RoutingSnapshot Snapshot(Project p,CancellationToken cancellation=default)
    {
        var cache=Caches.GetOrCreateValue(p);int stamp=Fingerprint(p);
        lock(cache){if(!cache.Ready||cache.Stamp!=stamp){cache.Snapshot=Compute(p,cancellation);cache.Stamp=stamp;cache.Ready=true;}return cache.Snapshot!;}
    }
    private static int Fingerprint(Project p)
    {
        var hash=new HashCode();
        foreach(var n in p.Nodes){hash.Add(n.Id);hash.Add(n.Kind);hash.Add(n.ParentId);hash.Add(n.X);hash.Add(n.Y);hash.Add(n.Width);hash.Add(n.DisplayHeight);hash.Add(n.Collapsed);hash.Add(n.CanContainNodes);hash.Add(n.Name);hash.Add(n.Addresses);hash.Add(n.Model);hash.Add(n.ImageSize);hash.Add(n.UseDeviceImage);hash.Add(n.ShowDeviceInfo);hash.Add(n.ShowStackMembers);foreach(var m in n.StackMembers){hash.Add(m.Id);hash.Add(m.Number);}}
        foreach(var link in p.Connections){hash.Add(link.Id);hash.Add(link.From);hash.Add(link.To);hash.Add(link.Kind);hash.Add(link.FromStackMemberId);hash.Add(link.ToStackMemberId);foreach(var point in link.Route)hash.Add(point);}
        return hash.ToHashCode();

    }
    public static RoutingSnapshot Compute(Project ownedSnapshot,CancellationToken cancellation=default) => new(Build(ownedSnapshot,cancellation));
    public static RoutingSnapshot Preview(Project p,SceneIndex? index=null) => new(p.Connections.ToDictionary(c=>c.Id,c=>MapLayout.LegacyRoute(p,c,index)));
    private static Dictionary<string,List<MapPoint>> Build(Project p,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var result=new Dictionary<string,List<MapPoint>>();var work=new List<Ends>();
        var scene=new SceneIndex(p);
        var visible=scene.PaintOrder.ToList();
        var links=p.Connections.OrderBy(c=>c.Id,StringComparer.Ordinal).ToList();
        var endpoints=links.Select(c=>(Link:c,A:scene.Endpoints.GetValueOrDefault(c.From),B:scene.Endpoints.GetValueOrDefault(c.To))).ToList();
        var incident=new Dictionary<string,List<string>>();
        foreach(var e in endpoints.Where(e=>e.A!=null&&e.B!=null&&e.A.Id!=e.B.Id))foreach(var n in new[]{e.A!,e.B!})
        {if(!incident.TryGetValue(n.Id,out var list))incident[n.Id]=list=[];list.Add(e.Link.Id);}
        List<Port> Ports(Node n,string? memberId,string linkId)
        {
            var list=incident[n.Id];double ratio=(list.IndexOf(linkId)+1d)/(list.Count+1d);
            double x=n.X+Math.Min(18,n.Width/4)+(n.Width-2*Math.Min(18,n.Width/4))*ratio;
            double y=n.Y+Math.Min(18,n.DisplayHeight/4)+(n.DisplayHeight-2*Math.Min(18,n.DisplayHeight/4))*ratio;
            bool member=n.IsStack&&n.ShowStackMembers&&memberId!=null;
            int row=member?n.StackMembers.OrderBy(m=>m.Number).ToList().FindIndex(m=>m.Id==memberId):-1;
            if(row>=0)y=n.Y+78+24*row;
            List<Port> ports=[new(new(n.X,y),new(n.X-12,y),0),new(new(n.X+n.Width,y),new(n.X+n.Width+12,y),0)];
            if(row<0){ports.Add(new(new(x,n.Y),new(x,n.Y-12),1));ports.Add(new(new(x,n.Y+n.DisplayHeight),new(x,n.Y+n.DisplayHeight+12),1));}
            return ports;
        }
        foreach(var e in endpoints)
        {
            cancellation.ThrowIfCancellationRequested();
            var c=e.Link;var a=e.A;var b=e.B;
            if(a==null||b==null||a.Id==b.Id){result[c.Id]=[];continue;}
            if(c.Route.Count>0&&a.Id==c.From&&b.Id==c.To||c.Kind==ConnectionKind.Hosting&&b.ParentId==a.Id){result[c.Id]=MapLayout.LegacyRoute(p,c);continue;}
            work.Add(new(c,a,b,Ports(a,c.FromStackMemberId,c.Id),Ports(b,c.ToStackMemberId,c.Id)));
        }
        if(work.Count==0)return result;
        var obstacles=new List<Box>();
        foreach(var n in visible)
        {
            var box=ProtectedBounds(n);obstacles.Add(new(box.X,box.Y,box.X+box.Width,box.Y+box.Height));
        }
        var xs=new SortedSet<double>();var ys=new SortedSet<double>();
        foreach(var b in obstacles){xs.UnionWith([b.Left-8,b.Left,b.Right,b.Right+8]);ys.UnionWith([b.Top-8,b.Top,b.Bottom,b.Bottom+8]);}
        foreach(var n in visible.Where(n=>n.CanContainNodes)){xs.UnionWith([n.X-20,n.X+n.Width+20]);ys.UnionWith([n.Y-20,n.Y+n.DisplayHeight+20]);}
        foreach(var e in work)foreach(var port in e.Sources.Concat(e.Targets)){xs.Add(port.Stub.X);ys.Add(port.Stub.Y);}
        xs.Add(xs.Min-40);xs.Add(xs.Max+40);ys.Add(ys.Min-40);ys.Add(ys.Max+40);
        // Bound routing work on exceptionally large projects; never allocate an unbounded grid.
        var regions=visible.Where(n=>n.CanContainNodes&&n.ParentId==null&&!n.Collapsed).ToList();
        bool local=(long)xs.Count*ys.Count>600_000;
        var grid=local?null:new Grid(xs.ToArray(),ys.ToArray(),obstacles,regions,cancellation);
        var reserved=new List<List<MapPoint>>();
        // Short local links reserve lanes first, leaving long inter-site links to the gutters.
        foreach(var e in work.OrderBy(e=>Math.Abs(e.A.X-e.B.X)+Math.Abs(e.A.Y-e.B.Y)).ThenBy(e=>e.Link.Id,StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            var allowed=scene.Parents[e.A.Id].Append(e.A).Concat(scene.Parents[e.B.Id].Append(e.B)).Select(n=>n.Id).ToHashSet();
            var allowedRegions=regions.Select((n,i)=>(n,i)).Where(pair=>allowed.Contains(pair.n.Id)).Select(pair=>pair.i).ToHashSet();
            var route=grid?.Find(e,allowedRegions);
            if(route==null)
            {
                // Build a smaller visibility grid for this connection instead of abandoning obstacle avoidance.
                foreach(double margin in new[]{80d,320d,1280d,5120d})
                {
                    var ports=e.Sources.Concat(e.Targets).ToList();
                    double left=ports.Min(p=>p.Stub.X)-margin,right=ports.Max(p=>p.Stub.X)+margin,top=ports.Min(p=>p.Stub.Y)-margin,bottom=ports.Max(p=>p.Stub.Y)+margin;
                    var nearby=obstacles.Where(b=>b.Right>=left&&b.Left<=right&&b.Bottom>=top&&b.Top<=bottom).ToList();
                    var lx=new SortedSet<double>{left,right};var ly=new SortedSet<double>{top,bottom};
                    foreach(var box in nearby){lx.UnionWith([box.Left,box.Right]);ly.UnionWith([box.Top,box.Bottom]);}
                    foreach(var port in ports){lx.Add(port.Stub.X);ly.Add(port.Stub.Y);}
                    if((long)lx.Count*ly.Count>600_000)continue;
                    var localGrid=new Grid(lx.ToArray(),ly.ToArray(),nearby,regions,cancellation);
                    foreach(var previous in reserved)localGrid.Reserve(previous);
                    route=localGrid.Find(e,allowedRegions);
                    if(route!=null&&visible.Where(n=>n.Id!=e.A.Id&&n.Id!=e.B.Id).Any(n=>ConnectionCaptions.Crosses(route,ProtectedBounds(n))))route=null;
                    if(route!=null)break;
                }
            }
            result[e.Link.Id]=route??MapLayout.LegacyRoute(p,e.Link);
            if(route!=null)reserved.Add(route);
        }
        return result;
    }
    private sealed class Grid
    {
        private readonly CancellationToken cancellation;
        private readonly double[] xs,ys;
        private readonly int width,count;
        private readonly bool[] horizontal,vertical;
        private readonly int[] usedH,usedV;
        private readonly int[] nearbyH,nearbyV;
        private readonly int[] regionH,regionV;
        public Grid(double[] x,double[] y,List<Box> obstacles,List<Node> regions,CancellationToken cancellation)
        {
            this.cancellation=cancellation;
            xs=x;ys=y;width=x.Length;count=width*y.Length;horizontal=new bool[count];vertical=new bool[count];usedH=new int[count];usedV=new int[count];
            nearbyH=new int[count];nearbyV=new int[count];
            regionH=new int[count];regionV=new int[count];Array.Fill(regionH,-1);Array.Fill(regionV,-1);
            for(int i=0;i<regions.Count;i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var n=regions[i];int left=Math.Max(0,Lower(xs,n.X)-1),right=Math.Min(width-1,Lower(xs,n.X+n.Width)),top=Math.Max(0,Lower(ys,n.Y)-1),bottom=Math.Min(ys.Length-1,Lower(ys,n.Y+n.DisplayHeight));
                for(int row=top;row<=bottom;row++)if(ys[row]>n.Y&&ys[row]<n.Y+n.DisplayHeight)
                    for(int col=left;col<right;col++)if(xs[col+1]>n.X&&xs[col]<n.X+n.Width)regionH[row*width+col]=i;
                for(int col=left;col<=right;col++)if(xs[col]>n.X&&xs[col]<n.X+n.Width)
                    for(int row=top;row<bottom;row++)if(ys[row+1]>n.Y&&ys[row]<n.Y+n.DisplayHeight)regionV[row*width+col]=i;
            }
            foreach(var b in obstacles)
            {
                cancellation.ThrowIfCancellationRequested();
                int left=Array.BinarySearch(xs,b.Left),right=Array.BinarySearch(xs,b.Right),top=Array.BinarySearch(ys,b.Top),bottom=Array.BinarySearch(ys,b.Bottom);
                for(int row=top+1;row<bottom;row++)for(int col=left;col<right;col++)horizontal[row*width+col]=true;
                for(int row=top;row<bottom;row++)for(int col=left+1;col<right;col++)vertical[row*width+col]=true;
            }
        }
        private int Index(MapPoint point)=>Array.BinarySearch(ys,point.Y)*width+Array.BinarySearch(xs,point.X);
        private MapPoint Point(int index)=>new(xs[index%width],ys[index/width]);
        private static int Lower(double[] values,double value){int i=Array.BinarySearch(values,value);return i<0?~i:i;}
        private void ReserveSegment(MapPoint a,MapPoint b)
        {
            if(a.Y==b.Y)
            {
                for(int row=Lower(ys,a.Y-10);row<ys.Length&&ys[row]<=a.Y+10;row++)
                    for(int col=Math.Max(0,Lower(xs,Math.Min(a.X,b.X))-1);col+1<width&&xs[col]<Math.Max(a.X,b.X);col++)
                        nearbyH[row*width+col]++;
            }
            else if(a.X==b.X)
            {
                for(int col=Lower(xs,a.X-10);col<width&&xs[col]<=a.X+10;col++)
                    for(int row=Math.Max(0,Lower(ys,Math.Min(a.Y,b.Y))-1);row+1<ys.Length&&ys[row]<Math.Max(a.Y,b.Y);row++)
                        nearbyV[row*width+col]++;
            }
        }
        public void Reserve(List<MapPoint> route){for(int i=1;i<route.Count;i++)ReserveSegment(route[i-1],route[i]);}
        public List<MapPoint>? Find(Ends ends,HashSet<int> allowedRegions)
        {
            var costs=new double[count*2];Array.Fill(costs,double.PositiveInfinity);
            var previous=new int[count*2];Array.Fill(previous,-1);
            var sourcePort=new Dictionary<int,Port>();var targets=ends.Targets.ToDictionary(p=>Index(p.Stub));
            var queue=new PriorityQueue<int,double>();
            double Estimate(int index)
            {
                double x=xs[index%width],y=ys[index/width],distance=double.PositiveInfinity;
                foreach(var port in ends.Targets)distance=Math.Min(distance,Math.Abs(x-port.Stub.X)+Math.Abs(y-port.Stub.Y));
                return distance+12;
            }
            foreach(var p in ends.Sources)
            {
                int index=Index(p.Stub),state=index*2+p.Axis;costs[state]=12;sourcePort[state]=p;queue.Enqueue(state,12+Estimate(index));
            }
            int finish=-1;double best=double.PositiveInfinity;int visited=0;
            while(queue.TryDequeue(out int state,out double priority))
            {
                if((visited&255)==0)cancellation.ThrowIfCancellationRequested();
                if(priority>=best)break;
                int index=state/2,axis=state%2;
                if(priority>costs[state]+Estimate(index)+.001)continue;
                if(++visited>count*6)break;
                if(targets.TryGetValue(index,out var target))
                {double total=costs[state]+12+(axis==target.Axis?0:24);if(total<best){best=total;finish=state;}}
                int col=index%width,row=index/width;
                void Visit(int next,int direction,int edge)
                {
                    if((direction==0?horizontal:vertical)[edge])return;
                    double length=direction==0?Math.Abs(xs[index%width]-xs[next%width]):Math.Abs(ys[index/width]-ys[next/width]);int usage=(direction==0?usedH:usedV)[edge];
                    double crossing=direction==0?usedV[index]+(row>0?usedV[index-width]:0):usedH[index]+(col>0?usedH[index-1]:0);
                    int region=(direction==0?regionH:regionV)[edge];
                    double sitePenalty=region>=0&&!allowedRegions.Contains(region)?length*2:0;
                    int nearby=(direction==0?nearbyH:nearbyV)[edge];
                    double cost=costs[state]+length+sitePenalty+(axis==direction?0:40)+usage*(length*2+24)+nearby*(length*.65)+crossing*40;
                    int nextState=next*2+direction;if(cost>=costs[nextState])return;
                    costs[nextState]=cost;previous[nextState]=state;queue.Enqueue(nextState,cost+Estimate(next));
                }
                if(col>0)Visit(index-1,0,index-1);if(col+1<width)Visit(index+1,0,index);
                if(row>0)Visit(index-width,1,index-width);if(row+1<ys.Length)Visit(index+width,1,index);
            }
            if(finish<0)return null;
            var steps=new List<int>();int current=finish;while(current>=0){steps.Add(current);current=previous[current];}steps.Reverse();
            for(int i=1;i<steps.Count;i++){int a=steps[i-1]/2,b=steps[i]/2;if(a/width==b/width)usedH[Math.Min(a,b)]++;else usedV[Math.Min(a,b)]++;}
            var route=new List<MapPoint>{sourcePort[steps[0]].Edge};route.AddRange(steps.Select(s=>Point(s/2)));route.Add(targets[finish/2].Edge);
            var simplified=Simplify(route);Reserve(simplified);return simplified;
        }
    }
    private static List<MapPoint> Simplify(List<MapPoint> points)
    {
        var result=new List<MapPoint>();
        foreach(var point in points)
        {
            if(result.Count>0&&result[^1]==point)continue;
            if(result.Count>1){var a=result[^2];var b=result[^1];if(a.X==b.X&&b.X==point.X||a.Y==b.Y&&b.Y==point.Y)result.RemoveAt(result.Count-1);}
            result.Add(point);
        }
        return result;
    }
}
