using RackTrail.Core;

static class EditingChecks
{
    public static void Run(Action<bool,string> check)
    {
        var p=new Project();
        var a=new Node{Id="a",Name="BACK-A",X=0,Y=0,Width=180,Height=76};
        var b=new Node{Id="b",Name="FRONT-B",X=0,Y=0,Width=180,Height=76};p.Nodes.AddRange([a,b]);
        check(MapEditing.PaintOrder(p).Last()==b,"Default overlap order retains newest item on top");
        MapEditing.Reorder(p,[a],true);check(MapEditing.PaintOrder(p).Last()==a,"Bring to front changes persistent paint order");
        var clone=ProjectStorage.Clone(p);check(MapEditing.PaintOrder(clone).Last().Id==a.Id,"Layer order survives save and reopen");
        check(VisualExporter.Svg(p).IndexOf("BACK-A",StringComparison.Ordinal)>VisualExporter.Svg(p).IndexOf("FRONT-B",StringComparison.Ordinal),"SVG follows persistent front/back order");
        MapEditing.Reorder(p,[a],false);check(MapEditing.PaintOrder(p).First()==a,"Send to back reverses overlapping order");
        var pos=MapEditing.OpenPosition(p,90,38,180,76);check(pos.X+180<=0||pos.X>=180||pos.Y+76<=0||pos.Y>=76,"New item placement avoids existing card");
        var empty=MapEditing.OpenPosition(new(),-500,900,180,76);check(empty==new MapPoint(-590,862),"Empty-view placement centers correctly at negative coordinates");

        var site=new Node{Id="site",Kind=NodeKind.Site,X=10,Y=10,Width=1000,Height=900};
        var subnet=new Node{Id="subnet",Kind=NodeKind.Subnet,ParentId=site.Id,X=100,Y=120,Width=700,Height=500};
        var device=new Node{Id="device",ParentId=subnet.Id,X=180,Y=220,Width=180,Height=76};
        var tree=new Project{Nodes=[site,subnet,device]};
        MapEditing.Reorder(tree,[site],true);check(MapEditing.PaintOrder(tree).SequenceEqual(new[]{site,subnet,device}),"Raised parent never hides its descendants");
        double dx=device.X-subnet.X,dy=device.Y-subnet.Y;MapEditing.FitContents(tree,subnet);
        check(subnet.Width==220&&subnet.Height==166&&Containment.IsInside(device,subnet),"Fit contents removes margins and preserves a safe header");
        check(device.X==subnet.X+20&&device.Y==subnet.Y+70,"Fit contents anchors children at content padding");
        device.Locked=true;var before=ProjectStorage.Serialize(tree);Containment.Move(tree,site,90,90);Containment.Move(tree,device,90,90);Containment.Resize(tree,device,500,500);
        check(ProjectStorage.Serialize(tree)==before,"Locked descendant prevents direct moves, ancestor moves and resizing");
        bool rejected=false;try{MapLayout.Arrange(tree);}catch(InvalidDataException){rejected=true;}
        check(rejected&&ProjectStorage.Serialize(tree)==before,"Auto layout refuses to disturb locked positions");
        device.Locked=false;site.Locked=true;check(MapEditing.IsLocked(tree,device),"Container locks are inherited by descendants");
        before=ProjectStorage.Serialize(tree);Containment.Resize(tree,subnet,900,900);check(before==ProjectStorage.Serialize(tree),"Inherited lock also prevents resizing a child container");
        site.Locked=false;subnet.AccentColor="#69C7BF";device.Locked=true;
        clone=ProjectStorage.Clone(tree);check(clone.Nodes[1].AccentColor=="#69C7BF"&&clone.Nodes[2].Locked,"Colors and locks round-trip through JSON");
        check(VisualExporter.Svg(tree).Contains("stroke=\"#69C7BF\""),"SVG includes custom subnet color");
        check(!MapEditing.ValidColor("red\"/><script>")&&!MapEditing.ValidColor("#12345G")&&MapEditing.ValidColor("#aBcDeF"),"Only six-digit hexadecimal custom colors are accepted");

        var items=new List<Node>{new(){X=10,Y=20,Width=100,Height=60},new(){X=220,Y=150,Width=140,Height=80},new(){X=500,Y=300,Width=200,Height=100}};
        var arrangement=new Project{Nodes=items};MapEditing.ArrangeSelection(arrangement,items,Alignment.Bottom);
        check(items.All(n=>n.Y+n.DisplayHeight==400),"Bottom alignment handles different item heights");
        MapEditing.ArrangeSelection(arrangement,items,null,true);
        check(Math.Abs((items[1].X-items[0].X-items[0].Width)-(items[2].X-items[1].X-items[1].Width))<.001,"Horizontal distribution creates equal gaps for different widths");
        MapEditing.ArrangeSelection(arrangement,items,Alignment.Center);
        check(items.Select(n=>n.X+n.Width/2).Distinct().Count()==1,"Center alignment handles unequal widths");
        var selected=new HashSet<string>{items[0].Id};var guides=MapEditing.Guides(arrangement,selected,.01,_=>true);
        check(guides.Any(g=>g.Vertical)&&guides.Any(g=>!g.Vertical),"Alignment guides report matching centers and bottoms");
        items[1].Locked=true;before=ProjectStorage.Serialize(arrangement);rejected=false;
        try{MapEditing.ArrangeSelection(arrangement,items,Alignment.Left);}catch(InvalidDataException){rejected=true;}
        check(rejected&&before==ProjectStorage.Serialize(arrangement),"Alignment rejects locked selections without partial edits");
        subnet.Collapsed=true;check(!tree.Visible(device)&&tree.VisibleEndpoint(device.Id)==subnet,"Compact containers hide children and reroute endpoints");
        subnet.Collapsed=false;check(tree.Visible(device)&&device.X==subnet.X+20,"Expanding preserves saved child positions");
        var legacy=ProjectStorage.Deserialize("{\"FormatVersion\":1,\"Nodes\":[{\"Id\":\"old\",\"Kind\":\"Subnet\",\"Height\":120}],\"Connections\":[]}");
        check(!legacy.Nodes[0].Locked&&legacy.Nodes[0].ZOrder==0&&legacy.Nodes[0].AccentColor=="","Existing map files load with safe defaults for new fields");
        var west=new Node{Id="west",Name="Site West",Kind=NodeKind.Site};var east=new Node{Id="east",Name="Site East",Kind=NodeKind.Site};
        var westData=new Node{Id="west-data",Name="Data",Kind=NodeKind.Subnet,ParentId=west.Id,VlanId="5",Cidr="192.0.2.0/24"};
        var eastData=new Node{Id="east-data",Name="Data",Kind=NodeKind.Subnet,ParentId=east.Id,VlanId="5",Cidr="203.0.113.0/24"};
        var choicesProject=new Project{Nodes=[westData,west,eastData,east]};var choices=ContainerChoices.Create(choicesProject,choicesProject.Nodes,"No container");
        check(choices.Select(c=>c.Id).SequenceEqual(new string?[]{null,"east","east-data","west","west-data"}),"Container choices group children under sorted parents regardless of file order");
        check(choices.Single(c=>c.Id==westData.Id) is {Path:"Site West › Data",Depth:1}&&choices.Single(c=>c.Id==eastData.Id).Path=="Site East › Data","Duplicate container names have distinct full hierarchy paths");
        check(choices.Single(c=>c.Id==westData.Id).Details.Contains("VLAN 5 · 192.0.2.0/24"),"Container choices include VLAN and CIDR context");
        var sameName=new Node{Id="west-data-copy",Name="Data",Kind=NodeKind.Subnet,ParentId=west.Id,VlanId="5",Cidr="192.0.2.0/24"};choicesProject.Nodes.Add(sameName);
        choices=ContainerChoices.Create(choicesProject,choicesProject.Nodes);
        check(choices.Where(c=>c.Path=="Site West › Data").Select(c=>c.Details).Distinct().Count()==2,"Identical names and networks within one parent remain distinguishable by ID");
    }
}
