using Avalonia;
using Avalonia.Media.Imaging;
using RackTrail.Core;

namespace RackTrail.Desktop;

internal static class RoutingUiChecks
{
    public static void Run(string directory)
    {
        void Check(bool pass,string label){if(!pass)throw new InvalidOperationException(label);Console.WriteLine("PASS "+label);}
        var p=new Project{Name="Connection routing preview"};
        for(int i=0;i<9;i++)p.Nodes.Add(new(){Id="routing-ui-"+i,Name=i==0?"Core switch":"Device "+i,Kind=i==0?NodeKind.Switch:NodeKind.Server,X=40+i%3*270,Y=40+i/3*180,Width=180,Height=76});
        for(int i=1;i<9;i++)p.Connections.Add(new(){Id="routing-ui-link-"+i,From=p.Nodes[0].Id,To=p.Nodes[i].Id,Purpose="Test connection"});
        var vm=new EditorViewModel();vm.Replace(p);var canvas=new MapCanvas(vm){Zoom=.65};
        Check(!canvas.ShowConnectionLabel(p.Connections[0]),"Overview zoom hides automatic connection labels");
        canvas.Zoom=1;Check(canvas.ShowConnectionLabel(p.Connections[0]),"Detailed zoom shows labels on a small map");
        vm.Select(p.Nodes[1]);Check(canvas.RelatedConnection(p.Connections[0])&&!canvas.RelatedConnection(p.Connections[1]),"Selected device highlights only related connections");
        Check(canvas.ShowConnectionLabel(p.Connections[0])&&!canvas.ShowConnectionLabel(p.Connections[1]),"Focusing a device removes unrelated connection labels");
        var before=ProjectStorage.Serialize(vm.Project);bool dirty=vm.Dirty;vm.RerouteConnections();
        Check(ProjectStorage.Serialize(vm.Project)==before&&vm.Dirty==dirty,"Reroute control preserves positions, custom routes, and save state");
        vm.Select(null);vm.SelectedConnection=p.Connections[1];canvas.Zoom=.2;
        Check(canvas.ShowConnectionLabel(p.Connections[1])&&!canvas.ShowConnectionLabel(p.Connections[0]),"Selected connection exposes its label at overview zoom");
        canvas.Exporting=true;Check(canvas.ShowConnectionLabel(p.Connections[1])==canvas.ShowConnectionLabel(p.Connections[0]),"Exports do not inherit transient selected labels");canvas.Exporting=false;
        vm.SelectedConnection=null;canvas.ShowAllConnectionLabels=true;Check(canvas.ShowConnectionLabel(p.Connections[0]),"Show all connection labels overrides overview hiding");
        canvas.ShowAllConnectionLabels=false;canvas.Zoom=1;canvas.Pan=new(10,10);
        canvas.Measure(new Size(860,620));canvas.Arrange(new Rect(0,0,860,620));
        using(var bitmap=new RenderTargetBitmap(new PixelSize(860,620),new Vector(96,96))){bitmap.Render(canvas);bitmap.Save(Path.Combine(directory,"routing-overview.png"));}
        vm.Select(p.Nodes[1]);
        using(var bitmap=new RenderTargetBitmap(new PixelSize(860,620),new Vector(96,96))){bitmap.Render(canvas);bitmap.Save(Path.Combine(directory,"routing-focused.png"));}
    }
}
