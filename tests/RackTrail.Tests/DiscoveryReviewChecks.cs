using RackTrail.Core;
using System.Text.Json;
static class DiscoveryReviewChecks
{
    public static void Run(Action<bool,string> check)
    {
        DiscoveredHost Host(string ip)=>new(ip,"device.example",2){Facts=new(){SystemDescription="wireless access point",LldpCapabilities=0x10}};
        var project=new Project{Nodes=[new(){Id="site",Kind=NodeKind.Site,Name="Main site",Width=800,Height=500}]};
        var result=DiscoveryReview.Apply(project,[new(Host("192.0.2.10"),NodeKind.Printer),new(Host("192.0.2.11"))],new(DiscoveryPlacement.NewSubnet,SubnetName:"Office",Cidr:"192.0.2.0/24",ParentId:"site"));
        check(result.CreatedSubnet?.ParentId=="site"&&result.Devices.All(n=>n.ParentId==result.CreatedSubnet.Id),"Scan review creates subnet inside chosen parent and assigns selected devices");
        check(result.Devices[0].Kind==NodeKind.Printer&&result.Devices[0].Classification?.ManualType==NodeKind.Printer&&result.Devices[0].Classification?.AutomaticType==NodeKind.AccessPoint,"Scan review type correction retains automatic evidence");
        var restored=ProjectStorage.Deserialize(ProjectStorage.Serialize(project));
        check(restored.Nodes.First(n=>n.Addresses=="192.0.2.10").Classification?.ManualType==NodeKind.Printer,"Scan review correction persists in project JSON");
        var updated=DiscoveryReview.Apply(project,[new(Host("192.0.2.10"))],new());
        check(updated.Devices[0].Kind==NodeKind.Printer&&project.Nodes.Count==4,"Review rescan keeps saved correction and avoids duplicate cards");
        DiscoveryReview.Apply(project,[new(Host("192.0.2.10"),UseAutomatic:true)],new());
        check(result.Devices[0].Kind==NodeKind.AccessPoint&&result.Devices[0].Classification?.ManualType==null,"Automatic choice in scan review clears previous override");
        var second=new Node{Id="zone",Kind=NodeKind.SecurityZone,Name="Servers",X=900,Width=500,Height=400};project.Nodes.Add(second);
        DiscoveryReview.Apply(project,[new(Host("192.0.2.10"),NodeKind.Server)],new(DiscoveryPlacement.AssignContainer,"zone"));
        check(result.Devices[0].ParentId=="zone"&&Containment.IsInside(result.Devices[0],second),"Review destination reparents an existing device to chosen container");
        var free=new Project();DiscoveryReview.Apply(free,[new(Host("192.0.2.20")),new(Host("192.0.2.21"))],new());
        check(free.Nodes.Select(n=>(n.X,n.Y)).Distinct().Count()==2,"New unassigned scan results get distinct initial map positions");
        void Reject(DiscoveryDestination destination,string message,DiscoverySelection[]? selected=null){
            var before=ProjectStorage.Serialize(project);bool rejected=false;
            try{DiscoveryReview.Apply(project,selected??[new(Host("198.51.100.10"))],destination);}catch(Exception ex)when(ex is ArgumentException or InvalidDataException){rejected=true;}
            check(rejected&&ProjectStorage.Serialize(project)==before,message);
        }
        Reject(new(DiscoveryPlacement.NewSubnet,SubnetName:"Invalid",Cidr:"wrong"),"Invalid new subnet is rejected before map changes");
        Reject(new(DiscoveryPlacement.NewSubnet,SubnetName:"",Cidr:"198.51.100.0/24"),"New subnet requires a name");
        Reject(new(DiscoveryPlacement.NewSubnet,SubnetName:"Office",Cidr:"192.0.2.0/24"),"Out-of-subnet devices are rejected before creating subnet");
        Reject(new(DiscoveryPlacement.AssignContainer,"missing"),"Missing destination container is rejected");
        Reject(new(DiscoveryPlacement.NewSubnet,SubnetName:"Nested",Cidr:"198.51.100.0/24",ParentId:result.CreatedSubnet!.Id),"New subnets cannot be nested accidentally inside existing subnets");
        Reject(new(), "Empty selection cannot create or modify the map",[]);
        var full=new Project{Nodes=Enumerable.Range(0,ProjectLimits.MaximumNodes).Select(i=>new Node{Id="capacity-"+i,Kind=NodeKind.Device}).ToList()};
        bool limited=false;try{DiscoveryReview.Apply(full,[new(Host("192.0.2.12"))],new(DiscoveryPlacement.NewSubnet,SubnetName:"Full",Cidr:"192.0.2.0/24"));}catch(InvalidDataException){limited=true;}
        check(limited&&full.Nodes.Count==ProjectLimits.MaximumNodes,"Scan review counts both new subnet and devices against node budget");
    }
}
