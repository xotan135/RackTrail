using RackTrail.Core;

internal static class RoutingChecks
{
    public static void Run(Action<bool,string> check)
    {
        Node Device(string id,double x,double y)=>new(){Id=id,Name=id,X=x,Y=y,Width=100,Height=60};
        var a=Device("route-a",0,100);var b=Device("route-b",600,100);var obstacle=Device("route-obstacle",280,50);obstacle.Height=200;
        var link=new Connection{Id="route-link",From=a.Id,To=b.Id};var p=new Project{Nodes=[a,b,obstacle],Connections=[link]};
        bool Orthogonal(List<MapPoint> r)=>r.Count>=2&&r.Zip(r.Skip(1)).All(pair=>pair.First.X==pair.Second.X||pair.First.Y==pair.Second.Y);
        bool Hits(List<MapPoint> r,Node n)=>r.Zip(r.Skip(1)).Any(pair=>
            pair.First.Y==pair.Second.Y?pair.First.Y>n.Y&&pair.First.Y<n.Y+n.DisplayHeight&&Math.Max(pair.First.X,pair.Second.X)>n.X&&Math.Min(pair.First.X,pair.Second.X)<n.X+n.Width:
            pair.First.X>n.X&&pair.First.X<n.X+n.Width&&Math.Max(pair.First.Y,pair.Second.Y)>n.Y&&Math.Min(pair.First.Y,pair.Second.Y)<n.Y+n.DisplayHeight);
        var route=MapLayout.Route(p,link);
        check(Orthogonal(route)&&!Hits(route,obstacle),"Automatic route avoids a blocking device using orthogonal segments");
        bool OnEdge(MapPoint point,Node n)=>((point.X==n.X||point.X==n.X+n.Width)&&point.Y>=n.Y&&point.Y<=n.Y+n.DisplayHeight)||((point.Y==n.Y||point.Y==n.Y+n.DisplayHeight)&&point.X>=n.X&&point.X<=n.X+n.Width);
        check(OnEdge(route[0],a)&&OnEdge(route[^1],b),"Router attaches to actual device edges");
        check(ReferenceEquals(route,MapLayout.Route(p,link)),"Unchanged maps reuse computed routes");
        var before=ProjectStorage.Serialize(p);ConnectionRouter.Invalidate(p);
        check(route.SequenceEqual(MapLayout.Route(p,link))&&ProjectStorage.Serialize(p)==before,"Rerouting is deterministic without changing saved geometry");
        obstacle.Y=-100;obstacle.Height=400;var moved=MapLayout.Route(p,link);
        check(!Hits(moved,obstacle)&&!ReferenceEquals(route,moved),"Moving and resizing an obstacle invalidates routes");
        link.Route=[new(100,120),new(600,120)];check(MapLayout.Route(p,link).SequenceEqual(link.Route),"Custom routes are preserved exactly");
        link.Route=[];p.Connections.Add(new(){Id="parallel-route",From=a.Id,To=b.Id});
        var parallel=MapLayout.Route(p,p.Connections[1]);
        check(!parallel.SequenceEqual(MapLayout.Route(p,link))&&Orthogonal(parallel),"Parallel connections fan out into distinct attachment points");
        var container=new Node{Id="route-site",Kind=NodeKind.Site,X=550,Y=0,Width=300,Height=400};p.Nodes.Add(container);b.ParentId=container.Id;container.Collapsed=true;
        var collapsed=MapLayout.Route(p,link);check(OnEdge(collapsed[^1],container),"Collapsed containers receive external routes at the visible boundary");
        a.ParentId=container.Id;check(MapLayout.Route(p,link).Count==0,"Routes inside the same collapsed container disappear");
        a.ParentId=null;container.Collapsed=false;
        p.Connections.AddRange(Enumerable.Range(0,31).Select(i=>new Connection{Id="dense-"+i,From=a.Id,To=b.Id,Purpose="Dense route label"}));
        check(!ConnectionRouter.ShowLabel(p)&&!VisualExporter.Svg(p).Contains("Dense route label"),"Large SVG maps hide connection label clutter by default");
        check(VisualExporter.Svg(p,true).Contains("Dense route label"),"Explicit all-label SVG export retains connection details");
        check(ConnectionRouter.LabelPoint([new(0,0),new(0,10),new(200,10)])==new MapPoint(100,10),"Labels sit at the midpoint of the longest segment");
        var siteA=Device("site-route-a",0,100);var siteB=Device("site-route-b",700,100);
        var unrelatedSite=new Node{Id="unrelated-route-site",Kind=NodeKind.Site,X=180,Y=80,Width=400,Height=120};
        var siteLink=new Connection{Id="site-link",From=siteA.Id,To=siteB.Id};var siteProject=new Project{Nodes=[siteA,siteB,unrelatedSite],Connections=[siteLink]};
        check(!Hits(MapLayout.Route(siteProject,siteLink),unrelatedSite),"Long connections prefer the gutter around an unrelated site");
        var fixedRoute=new List<MapPoint>{new(1,2),new(3,2)};siteLink.Route=fixedRoute;
        check(MapLayout.PreviewRoute(siteProject,siteLink).SequenceEqual(fixedRoute),"Fast drag preview preserves saved custom route points");
        var tiny=Device("tiny-route",300,500);tiny.UseDeviceImage=true;tiny.Width=32;tiny.Height=32;p.Nodes.Add(tiny);
        var tinyLink=new Connection{From=tiny.Id,To=a.Id};p.Connections.Add(tinyLink);check(Orthogonal(MapLayout.Route(p,tinyLink)),"Compact image-only devices retain valid routing attachment points");
        var dense=new Project();
        for(int i=0;i<60;i++)dense.Nodes.Add(Device("grid-"+i,(i%10)*160,(i/10)*120));
        for(int i=1;i<60;i++)dense.Connections.Add(new(){Id="grid-link-"+i,From=dense.Nodes[0].Id,To=dense.Nodes[i].Id});
        var watch=System.Diagnostics.Stopwatch.StartNew();var routes=dense.Connections.Select(c=>MapLayout.Route(dense,c)).ToList();watch.Stop();
        check(routes.All(Orthogonal)&&watch.Elapsed.TotalSeconds<15,"Dense 60-device map routes within the interactive routing budget");
        check(routes.Select((r,i)=>dense.Nodes.Where(n=>n.Id!=dense.Connections[i].From&&n.Id!=dense.Connections[i].To).All(n=>!Hits(r,n))).All(x=>x),"Dense routes avoid every unrelated device");
        var irregular=new Project();var left=Device("irregular-left",0,100);var right=Device("irregular-right",600,100);var blocker=Device("irregular-blocker",280,50);blocker.Height=200;
        irregular.Nodes.AddRange([left,right,blocker]);
        for(int i=0;i<225;i++)irregular.Nodes.Add(Device("irregular-"+i,1200+i*140.273,1000+i*.371));
        var irregularLink=new Connection{Id="irregular-link",From=left.Id,To=right.Id,Purpose="Connection label near names"};irregular.Connections.Add(irregularLink);
        var irregularBefore=ProjectStorage.Serialize(irregular);var irregularRoute=MapLayout.Route(irregular,irregularLink);
        check(Orthogonal(irregularRoute)&&!Hits(irregularRoute,blocker),"Irregular maps beyond the global grid limit retain obstacle avoidance");
        check(ProjectStorage.Serialize(irregular)==irregularBefore,"Local routing does not move devices or rewrite saved paths");
        var captions=ConnectionCaptions.Create(irregular,irregular.Connections);
        check(captions.Count>0&&captions.Values.All(c=>irregular.Nodes.All(n=>!ConnectionCaptions.Overlaps(c.Box,ConnectionRouter.ProtectedBounds(n)))),"Connection captions occupy free space outside device names");
        check(captions.Values.All(c=>!ConnectionCaptions.Crosses(irregularRoute,c.Box)),"Connection captions do not obscure routed segments");
        var exportBounds=VisualExporter.Bounds(irregular);
        check(captions.Values.All(c=>c.Box.X>=exportBounds.X&&c.Box.Y>=exportBounds.Y&&c.Box.X+c.Box.Width<=exportBounds.X+exportBounds.Width&&c.Box.Y+c.Box.Height<=exportBounds.Y+exportBounds.Height),"Free-space captions stay inside export bounds");
        var header=new Node{Id="header-check",Kind=NodeKind.Subnet,X=20,Y=20,Width=300,Height=120,Cidr="192.0.2.0/24",VlanId="5"};
        var protectedHeader=ConnectionRouter.ProtectedBounds(header);
        check(NodePresentation.Create(header).Labels.All(l=>l.Y+l.Size*1.4<=protectedHeader.Y+protectedHeader.Height),"Container routing protection covers both name and subnet text lines");
    }
}
