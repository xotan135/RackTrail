using RackTrail.Core;

static class PresentationChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach(var shape in Enum.GetValues<DeviceShape>())
        {
            var node=new Node{Kind=NodeKind.AccessPoint,Name="Example AP",Addresses="192.0.2.10",Shape=shape,X=50,Y=70,Width=180,Height=76};
            var p=new Project{Nodes=[node]};Containment.Resize(p,node,180,76);var layout=NodePresentation.Create(node);
            check(layout.Labels.All(l=>l.Center&&Math.Abs(l.X+l.Width/2-node.X-node.Width/2)<.001),$"{shape} text centers on the shape");
            check(layout.Labels.All(l=>l.Y>=node.Y&&l.Y+l.Size*1.4<=node.Y+node.DisplayHeight),$"{shape} text stays vertically inside the shape");
            Containment.Resize(p,node,120,120);layout=NodePresentation.Create(node);
            check(layout.Labels.All(l=>Math.Abs(l.X+l.Width/2-node.X-node.Width/2)<.001),$"{shape} text re-centers after resizing");
            check(VisualExporter.Svg(p).Contains("text-anchor=\"middle\""),$"{shape} SVG uses centered text");
        }
        var image=new Node{Kind=NodeKind.AccessPoint,UseDeviceImage=true,Model="NanoStation AC",Name="Example radio",Width=220,Height=56,ImageSize=48};
        var imageLayout=NodePresentation.Create(image);
        check(imageLayout.Labels.All(l=>l.X>imageLayout.Icon.X+imageLayout.Icon.Width),"Device information is beside the image");
        var project=new Project{Nodes=[image]};Containment.Resize(project,image,120,32);imageLayout=NodePresentation.Create(image);
        check(imageLayout.Icon.Height<=24&&imageLayout.Icon.Y>=image.Y,"Dragging image card smaller scales its picture immediately");
        image.ShowDeviceInfo=false;Containment.Resize(project,image,1,1);imageLayout=NodePresentation.Create(image);
        check(image.Width==32&&image.Height==32&&imageLayout.Labels.Count==0,"Image-only APs resize to 32 pixels without a caption reservation");
        check(Math.Abs(imageLayout.Icon.X+imageLayout.Icon.Width/2-image.X-image.Width/2)<.001,"Image-only picture remains centered");
        var copy=ProjectStorage.Clone(project).Nodes[0];check(copy.ImageSize==48&&!copy.ShowDeviceInfo&&copy.UseDeviceImage,"Image size and label visibility survive save and reopen");
        check(DeviceIcons.ResolveKey(new(){UseDeviceImage=true,Model="NS-5AC"},new[]{"nanostation-ac"})=="nanostation-ac","NanoStation product code selects its actual model picture");
        check(DeviceIcons.ResolveKey(new(){UseDeviceImage=true,Kind=NodeKind.AccessPoint,Vendor="Ubiquiti"},new[]{"unifi-ap"})=="unifi-ap","Unknown UniFi AP can use the general AP picture");
        bool rejected=false;try{ProjectStorage.Deserialize("{\"FormatVersion\":1,\"Nodes\":[{\"Id\":\"bad\",\"ImageSize\":0}],\"Connections\":[]}");}catch(InvalidDataException){rejected=true;}
        check(rejected,"Invalid persisted image sizes are rejected");
        var old=ProjectStorage.Deserialize("{\"FormatVersion\":1,\"Nodes\":[{\"Id\":\"legacy\"}],\"Connections\":[]}");
        check(old.Nodes[0].ImageSize==48&&old.Nodes[0].ShowDeviceInfo,"Older maps receive readable image-layout defaults");
    }
}
