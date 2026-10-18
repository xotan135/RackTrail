using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RackTrail.Core;
using System.Net;
namespace RackTrail.Desktop;

public sealed class ScanWindow : Window
{
    private readonly TextBox targets=new(){Watermark="192.168.20.0/24 or 192.168.20.10-192.168.20.50",AcceptsReturn=true,MinHeight=65};
    private readonly TextBox exclusions=new(){Watermark="192.168.20.1, 192.168.20.100-192.168.20.120"};
    private readonly TextBlock preview=new(){TextWrapping=TextWrapping.Wrap},status=new(){Text="No scan has run.",TextWrapping=TextWrapping.Wrap};
    private readonly StackPanel results=new(){Spacing=6};
    private readonly Button scan=new(){Content="Start scan"},stop=new(){Content="Stop",IsEnabled=false},add=new(){Content="Add selected to map",IsEnabled=false};
    private readonly ComboBox container=new(){HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly List<(CheckBox Choice,DiscoveredHost Host)> rows=[];
    private readonly EditorViewModel vm;
    private CancellationTokenSource? cancellation;
    private List<string?> subnetIds=[];
    private bool running;
    public ScanWindow(EditorViewModel model)
    {
        vm=model;Title="Scan subnet / RackTrail";Icon=Branding.CreateIcon();Width=840;Height=760;MinWidth=650;MinHeight=550;
        var root=new Grid{RowDefinitions=new("Auto,*,Auto"),Margin=new Thickness(20)};var top=new StackPanel{Spacing=8};
        top.Children.Add(new TextBlock{Text="Discover devices",FontSize=23});
        top.Children.Add(new TextBlock{Text="Enter exactly what to scan. Use commas or new lines for multiple targets. Only those addresses are contacted, using ping and reverse DNS. Devices that block ping may be missed; type, vendor and network links are not inferred.",TextWrapping=TextWrapping.Wrap});
        top.Children.Add(new TextBlock{Text="Targets — IPv4 subnet, full IP range or individual addresses"});top.Children.Add(targets);
        top.Children.Add(new TextBlock{Text="Exclude — addresses, ranges or subnets (optional)"});top.Children.Add(exclusions);top.Children.Add(preview);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};actions.Children.Add(scan);actions.Children.Add(stop);top.Children.Add(actions);top.Children.Add(status);root.Children.Add(top);
        var scroll=new ScrollViewer{Content=results,Margin=new Thickness(0,12)};Grid.SetRow(scroll,1);root.Children.Add(scroll);
        var bottom=new StackPanel{Spacing=8};bottom.Children.Add(new TextBlock{Text="Add selected devices to"});bottom.Children.Add(container);
        var choices=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};choices.Children.Add(MainWindow.Button("Select all",()=>{foreach(var row in rows.Where(r=>r.Choice.IsEnabled))row.Choice.IsChecked=true;}));choices.Children.Add(MainWindow.Button("Select none",()=>{foreach(var row in rows)row.Choice.IsChecked=false;}));choices.Children.Add(add);choices.Children.Add(MainWindow.Button("Close",Close));bottom.Children.Add(choices);Grid.SetRow(bottom,2);root.Children.Add(bottom);Content=root;
        RefreshSubnets();var selected=vm.Selected;var subnet=selected?.Kind==NodeKind.Subnet?selected:selected==null?null:vm.Project.Ancestors(selected).FirstOrDefault(n=>n.Kind==NodeKind.Subnet);if(subnet!=null){targets.Text=subnet.Cidr;container.SelectedIndex=subnetIds.IndexOf(subnet.Id);}
        targets.TextChanged+=(_,_)=>UpdatePreview();exclusions.TextChanged+=(_,_)=>UpdatePreview();scan.Click+=async(_,_)=>await RunScan();stop.Click+=(_,_)=>{cancellation?.Cancel();stop.IsEnabled=false;status.Text="Stopping…";};add.Click+=(_,_)=>Import();Closed+=(_,_)=>cancellation?.Cancel();UpdatePreview();
    }
    private void RefreshSubnets(){var subnets=vm.Project.Nodes.Where(n=>n.Kind==NodeKind.Subnet).ToList();subnetIds=[null,..subnets.Select(n=>(string?)n.Id)];container.ItemsSource=new[]{"No subnet assignment"}.Concat(subnets.Select(n=>$"{n.Name} — {n.Cidr}")).ToList();container.SelectedIndex=0;}
    private IReadOnlyList<IPAddress> GetTargets()=>ScanTargets.Parse(targets.Text??"",exclusions.Text??"");
    private void UpdatePreview(){try{var count=GetTargets().Count;preview.Text=$"{count} target addresses after exclusions. Nothing runs until Start scan.";scan.IsEnabled=!running;}catch(ArgumentException ex){preview.Text=ex.Message;scan.IsEnabled=false;}}
    private void SetRunning(bool value){running=value;targets.IsEnabled=exclusions.IsEnabled=container.IsEnabled=!value;stop.IsEnabled=value;add.IsEnabled=!value&&rows.Any(r=>r.Choice.IsEnabled);UpdatePreview();}
    private async Task RunScan()
    {
        IReadOnlyList<IPAddress> addresses;try{addresses=GetTargets();}catch(ArgumentException ex){status.Text=ex.Message;return;}
        rows.Clear();results.Children.Clear();cancellation=new();SetRunning(true);
        var progress=new Progress<ScanProgress>(p=>{if(running)status.Text=$"Checked {p.Completed}/{p.Total} addresses · {p.Found} replies · {p.Errors} probe errors";});
        try{var report=await NetworkDiscovery.ScanAsync(addresses,progress,cancellation.Token);foreach(var host in report.Hosts){bool exists=DiscoveryImport.Exists(vm.Project,host.Address);var choice=new CheckBox{IsChecked=!exists,IsEnabled=!exists,Content=$"{host.Address}    {(host.Hostname.Length>0?host.Hostname:"No hostname")}    {host.ResponseTime} ms{(exists?"    Already on map":"")}"};rows.Add((choice,host));results.Children.Add(choice);}
            status.Text=$"{(report.Cancelled?"Stopped":"Finished")}: {report.Completed}/{addresses.Count} addresses checked; {report.Hosts.Count} replied; {report.Errors} probe errors. Review devices before adding them. No reply does not mean an address is unused.";
        }catch(Exception ex){status.Text="Scan failed: "+ex.Message;}finally{cancellation.Dispose();cancellation=null;SetRunning(false);}
    }
    private void Import()
    {
        var selected=rows.Where(r=>r.Choice.IsEnabled&&r.Choice.IsChecked==true).Select(r=>r.Host).ToList();if(selected.Count==0){status.Text="Select at least one device.";return;}
        string? subnetId=container.SelectedIndex>=0&&container.SelectedIndex<subnetIds.Count?subnetIds[container.SelectedIndex]:null;
        try{IReadOnlyList<Node> added=[];vm.Edit(()=>added=DiscoveryImport.Add(vm.Project,selected,subnetId));vm.Selection.Clear();foreach(var node in added)vm.Selection.Add(node.Id);vm.Status=$"Added {added.Count} discovered devices";vm.RefreshDetails();foreach(var row in rows){if(DiscoveryImport.Exists(vm.Project,row.Host.Address)){row.Choice.IsChecked=false;row.Choice.IsEnabled=false;}}add.IsEnabled=rows.Any(r=>r.Choice.IsEnabled);status.Text=$"Added {added.Count} devices. Skipped {selected.Count-added.Count} already mapped or outside the chosen subnet. Undo removes this import.";}catch(Exception ex) when(ex is ArgumentException or InvalidDataException){status.Text=ex.Message;}
    }
}
