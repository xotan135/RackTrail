using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RackTrail.Core;
using System.Net;
namespace RackTrail.Desktop;

public sealed class ScanWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly TabControl tabs=new();
    private readonly TextBox targets=new(){Name="ScanTargets",Watermark="192.168.20.0/24 or 192.168.20.10-192.168.20.50",AcceptsReturn=true,MinHeight=82};
    private readonly TextBox exclusions=new(){Watermark="192.168.20.1, 192.168.20.100-192.168.20.120"};
    private readonly ComboBox scanNetwork=new(){Name="ScanNetwork",HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ComboBox snmpVersion=new(){Name="SnmpVersion",ItemsSource=new[]{"Off","SNMPv1","SNMPv2c"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly TextBox community=new(){Name="SnmpCommunity",Watermark="Read-only community · session only",PasswordChar='●',IsEnabled=false};
    private readonly CheckBox services=new(){Content="Check common TCP service ports"};
    private readonly CheckBox authorized=new(){Content="I am authorized to scan these targets"};
    private readonly TextBlock preview=Muted("Choose a network to begin."),status=Muted("No scan has run.");
    private readonly TextBlock reviewSummary=Muted("Run a scan, then review the device types and choose where to put them.");
    private readonly TextBlock ouiStatus=Muted("Optional local MAC vendor CSV. No database is downloaded.");
    private readonly StackPanel results=new(){Spacing=12};
    private readonly Button scan=new(){Name="StartScan",Content="Start discovery"},stop=new(){Content="Stop scan",IsEnabled=false},add=new(){Name="ApplyDiscovery",Content="Apply selected devices",IsEnabled=false};
    private readonly ComboBox placement=new(){Name="Placement",ItemsSource=new[]{"Keep existing / no container for new","Assign to an existing container","Create a new subnet"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ComboBox container=new(){Name="DestinationContainer",HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ComboBox subnetParent=new(){Name="SubnetParent",HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly TextBox subnetName=new(){Name="NewSubnetName",Watermark="Office network"};
    private readonly TextBox subnetCidr=new(){Name="NewSubnetCidr",Watermark="192.168.20.0/24"};
    private readonly StackPanel existingDestination=new(){Spacing=8},newDestination=new(){Spacing=8};
    private readonly List<(CheckBox Choice,DiscoveredHost Host,ComboBox Type,NodeKind[] Kinds)> rows=[];
    private List<string?> scanSubnetIds=[],containerIds=[],parentIds=[];
    private IReadOnlyDictionary<string,string> ouiVendors=new Dictionary<string,string>();
    private DiscoverySession? session;
    private CancellationTokenSource? cancellation;
    private bool running;
    public ScanWindow(EditorViewModel model)
    {
        vm=model;Title="Network discovery / RackTrail";Icon=Branding.CreateIcon();
        Width=1120;Height=880;MinWidth=900;MinHeight=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        scan.Classes.Add("primary");add.Classes.Add("primary");
        var root=new Grid{RowDefinitions=new("Auto,*,Auto"),Margin=new Thickness(26)};
        var heading=new StackPanel{Spacing=5,Margin=new Thickness(0,0,0,18)};
        heading.Children.Add(Section("NETWORK TOOLS"));
        heading.Children.Add(new TextBlock{Text="Discover your network",FontSize=28,FontWeight=FontWeight.SemiBold});
        heading.Children.Add(Muted("Choose the scope. Review the evidence. Put each device in the right place."));
        root.Children.Add(heading);
        tabs.ItemsSource=new[]{new TabItem{Header="1  Scan setup",Content=BuildSetup()},new TabItem{Header="2  Review and add",Content=BuildReview()}};
        Grid.SetRow(tabs,1);root.Children.Add(tabs);
        var footer=new Grid{ColumnDefinitions=new("*,Auto"),Margin=new Thickness(0,16,0,0)};
        footer.Children.Add(status);var close=MainWindow.Button("Close",Close);Grid.SetColumn(close,1);footer.Children.Add(close);
        Grid.SetRow(footer,2);root.Children.Add(footer);Content=root;
        RefreshContainers();
        var selected=vm.Selected;var subnet=selected?.Kind==NodeKind.Subnet?selected:selected==null?null:vm.Project.Ancestors(selected).FirstOrDefault(n=>n.Kind==NodeKind.Subnet);
        if(subnet!=null){scanNetwork.SelectedIndex=scanSubnetIds.IndexOf(subnet.Id);targets.Text=subnet.Cidr;placement.SelectedIndex=1;container.SelectedIndex=containerIds.IndexOf(subnet.Id);}
        targets.TextChanged+=(_,_)=>{UpdatePreview();PrefillSubnet();};exclusions.TextChanged+=(_,_)=>UpdatePreview();
        scanNetwork.SelectionChanged+=(_,_)=>{if(scanNetwork.SelectedIndex>0&&vm.Project.Nodes.Find(n=>n.Id==scanSubnetIds[scanNetwork.SelectedIndex]) is {} chosen)targets.Text=chosen.Cidr;};
        snmpVersion.SelectionChanged+=(_,_)=>{community.IsEnabled=!running&&snmpVersion.SelectedIndex>0;session=null;UpdatePreview();};
        community.TextChanged+=(_,_)=>{session=null;UpdatePreview();};services.IsCheckedChanged+=(_,_)=>{session=null;UpdatePreview();};
        authorized.IsCheckedChanged+=(_,_)=>UpdatePreview();placement.SelectionChanged+=(_,_)=>UpdateDestination();
        scan.Click+=async(_,_)=>await RunScan();stop.Click+=(_,_)=>{cancellation?.Cancel();stop.IsEnabled=false;status.Text="Stopping remaining work…";};
        add.Click+=(_,_)=>Import();Closed+=(_,_)=>{cancellation?.Cancel();community.Text="";session=null;};
        UpdateDestination();UpdatePreview();PrefillSubnet();
    }
    private Control BuildSetup()
    {
        var body=new StackPanel{Spacing=16,Margin=new Thickness(0,16,0,0)};
        var scope=new StackPanel{Spacing=9};scope.Children.Add(Section("NETWORK TO SCAN"));
        scope.Children.Add(Muted("Choose an existing subnet or enter an explicit IPv4 network, range, or address."));
        scope.Children.Add(scanNetwork);scope.Children.Add(targets);
        scope.Children.Add(FieldLabel("Exclude addresses or networks (optional)"));scope.Children.Add(exclusions);scope.Children.Add(preview);
        var methods=new StackPanel{Spacing=10};methods.Children.Add(Section("DISCOVERY METHODS"));
        var credentials=new Grid{ColumnDefinitions=new("150,12,*")};
        var versionField=new StackPanel{Spacing=6};versionField.Children.Add(FieldLabel("SNMP version"));versionField.Children.Add(snmpVersion);
        var communityField=new StackPanel{Spacing=6};communityField.Children.Add(FieldLabel("SNMP community"));communityField.Children.Add(community);
        credentials.Children.Add(versionField);Grid.SetColumn(communityField,2);credentials.Children.Add(communityField);methods.Children.Add(credentials);
        methods.Children.Add(Muted("SNMPv1 and v2c use an unencrypted read-only community. No defaults or password guessing. SNMPv3 is not yet supported."));
        methods.Children.Add(services);methods.Children.Add(Muted("Service checks only connect to a small fixed port list. Neighbors are never scanned automatically."));
        var extra=new StackPanel{Spacing=8};extra.Children.Add(MainWindow.Button("Load MAC vendor CSV",LoadVendors));extra.Children.Add(ouiStatus);
        methods.Children.Add(new Expander{Header="MAC vendor lookup (optional)",Content=extra,HorizontalAlignment=HorizontalAlignment.Stretch});
        var columns=new Grid{ColumnDefinitions=new("*,18,*")};columns.Children.Add(Card(scope));var methodsCard=Card(methods);Grid.SetColumn(methodsCard,2);columns.Children.Add(methodsCard);body.Children.Add(columns);
        var actions=new StackPanel{Spacing=12};actions.Children.Add(authorized);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};buttons.Children.Add(scan);buttons.Children.Add(stop);actions.Children.Add(buttons);
        body.Children.Add(Card(actions));
        body.Children.Add(Muted("A missing response does not mean an address is unused. Classification stays Unknown when evidence is insufficient."));
        return new ScrollViewer{Content=body};
    }
    private Control BuildReview()
    {
        var review=new Grid{ColumnDefinitions=new("*,18,330"),Margin=new Thickness(0,16,0,0)};
        var devices=new Grid{RowDefinitions=new("Auto,Auto,*")};
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        tools.Children.Add(MainWindow.Button("Select all",()=>{foreach(var row in rows.Where(r=>r.Choice.IsEnabled))row.Choice.IsChecked=true;}));
        tools.Children.Add(MainWindow.Button("Select none",()=>{foreach(var row in rows)row.Choice.IsChecked=false;}));devices.Children.Add(tools);
        reviewSummary.Margin=new Thickness(0,12);Grid.SetRow(reviewSummary,1);devices.Children.Add(reviewSummary);
        var scroll=new ScrollViewer{Content=results};Grid.SetRow(scroll,2);devices.Children.Add(scroll);review.Children.Add(devices);
        var destination=new StackPanel{Spacing=12};destination.Children.Add(Section("MAP DESTINATION"));
        destination.Children.Add(Muted("The chosen destination applies to all selected devices, including existing cards."));
        destination.Children.Add(placement);
        existingDestination.Children.Add(FieldLabel("Container / subnet"));existingDestination.Children.Add(container);destination.Children.Add(existingDestination);
        newDestination.Children.Add(FieldLabel("Subnet name"));newDestination.Children.Add(subnetName);
        newDestination.Children.Add(FieldLabel("IPv4 subnet (CIDR)"));newDestination.Children.Add(subnetCidr);
        newDestination.Children.Add(FieldLabel("Parent container for this subnet"));newDestination.Children.Add(subnetParent);
        destination.Children.Add(newDestination);
        destination.Children.Add(Muted("Device types can be corrected on each result. Automatic uses the latest evidence; choosing a type saves a manual correction."));
        destination.Children.Add(add);destination.Children.Add(Muted("One Undo restores the entire import, including a newly created subnet."));
        var destinationCard=Card(destination);destinationCard.VerticalAlignment=VerticalAlignment.Top;
        var destinationScroll=new ScrollViewer{Content=destinationCard};Grid.SetColumn(destinationScroll,2);review.Children.Add(destinationScroll);
        return review;
    }
    private static Border Card(Control content){var border=new Border{Child=content};border.Classes.Add("card");return border;}
    private static TextBlock Section(string text){var block=new TextBlock{Text=text};block.Classes.Add("section");return block;}
    private static TextBlock FieldLabel(string text)=>new(){Text=text,FontSize=12,FontWeight=FontWeight.SemiBold};
    private static TextBlock Muted(string text){var block=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=12};block.Classes.Add("muted");return block;}
    private async Task LoadVendors()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="MAC vendor CSV (OUI,vendor)",AllowMultiple=false});if(files.Count==0)return;
        try{await using var stream=await files[0].OpenReadAsync();using var reader=new StreamReader(stream);var buffer=new char[1_000_001];int count=await reader.ReadBlockAsync(buffer,0,buffer.Length);
            if(count>1_000_000)throw new InvalidDataException("Use a vendor CSV under 1 MB.");
            ouiVendors=OuiRegistry.Parse(new string(buffer,0,count));session=null;ouiStatus.Text=$"Loaded {ouiVendors.Count} vendor prefixes.";
        }catch(Exception ex)when(ex is IOException or ArgumentException){ouiStatus.Text=ex.Message;}
    }
    private void RefreshContainers()
    {
        var subnets=vm.Project.Nodes.Where(n=>n.Kind==NodeKind.Subnet).ToList();
        var scanChoices=ContainerPicker.Populate(scanNetwork,vm.Project,subnets,"Enter a network or IP range below");
        scanSubnetIds=scanChoices.Select(c=>c.Id).ToList();
        var parents=vm.Project.Nodes.Where(n=>n.IsContainer).ToList();
        var existingChoices=ContainerPicker.Populate(container,vm.Project,parents);
        containerIds=existingChoices.Select(c=>c.Id).ToList();
        var subnetParents=parents.Where(n=>n.Kind!=NodeKind.Subnet&&!vm.Project.Ancestors(n).Any(a=>a.Kind==NodeKind.Subnet));
        var parentChoices=ContainerPicker.Populate(subnetParent,vm.Project,subnetParents,"No parent — top level");
        parentIds=parentChoices.Select(c=>c.Id).ToList();

    }
    private void PrefillSubnet()
    {
        var text=(targets.Text??"").Trim();
        if(text.Contains('/')&&!text.Contains(',')&&!text.Contains('\n')&&IpNetwork.TryParse(text,out var network)&&network!.Bits==32&&string.IsNullOrWhiteSpace(subnetCidr.Text))subnetCidr.Text=text;
    }
    private void UpdateDestination(){existingDestination.IsVisible=placement.SelectedIndex==1;newDestination.IsVisible=placement.SelectedIndex==2;}
    private IReadOnlyList<IPAddress> GetTargets()=>ScanTargets.Parse(targets.Text??"",exclusions.Text??"");
    private void UpdatePreview()
    {
        try{
            var count=GetTargets().Count;preview.Text=$"{count} target addresses after exclusions.";
            if(snmpVersion.SelectedIndex>0&&string.IsNullOrWhiteSpace(community.Text)){preview.Text+=" Enter the SNMP community or choose Off.";scan.IsEnabled=false;return;}
            scan.IsEnabled=!running&&authorized.IsChecked==true;
        }catch(ArgumentException ex){preview.Text=ex.Message;scan.IsEnabled=false;}
    }
    private void SetRunning(bool value)
    {
        running=value;stop.IsEnabled=value;
        // Keep setup interactive only for the Stop button while a scan is active.
        tabs.IsEnabled=true;targets.IsEnabled=exclusions.IsEnabled=scanNetwork.IsEnabled=snmpVersion.IsEnabled=services.IsEnabled=authorized.IsEnabled=!value;
        community.IsEnabled=!value&&snmpVersion.SelectedIndex>0;placement.IsEnabled=container.IsEnabled=subnetParent.IsEnabled=subnetName.IsEnabled=subnetCidr.IsEnabled=!value;
        add.IsEnabled=!value&&rows.Any(r=>r.Choice.IsEnabled);UpdatePreview();
    }
    private async Task RunScan()
    {
        if(authorized.IsChecked!=true)return;
        IReadOnlyList<IPAddress> addresses;try{addresses=GetTargets();if(snmpVersion.SelectedIndex>0&&string.IsNullOrWhiteSpace(community.Text))throw new ArgumentException("Enter a read-only SNMP community.");}
        catch(ArgumentException ex){status.Text=ex.Message;return;}
        rows.Clear();results.Children.Clear();cancellation=new();SetRunning(true);
        var progress=new Progress<ScanProgress>(p=>{if(running)status.Text=$"Checking {p.Completed}/{p.Total} · {p.Found} devices · {p.Errors} probe errors";});
        try{
            session??=new DiscoverySession(new(){CheckServices=services.IsChecked==true,SnmpCommunity=snmpVersion.SelectedIndex==0?"":community.Text??"",SnmpVersion=snmpVersion.SelectedIndex==1?SnmpVersion.V1:SnmpVersion.V2c,OuiVendors=ouiVendors,FindMac=WindowsNeighborCache.Find});
            var report=await NetworkDiscovery.ScanAsync(addresses,progress,cancellation.Token,discovery:session,inventory:ProjectStorage.Clone(vm.Project));
            PresentResults(report,addresses.Count);
        }catch(Exception ex){status.Text="Scan failed: "+ex.Message;}
        finally{cancellation.Dispose();cancellation=null;SetRunning(false);}
    }
    internal void ShowSetup()=>tabs.SelectedIndex=0;
    internal void PresentResults(ScanReport report,int total)
    {
        rows.Clear();results.Children.Clear();
        foreach(var host in report.Hosts){
            var existing=DiscoveryReview.Existing(vm.Project,host.Address);var classification=host.Classification??DeviceClassifier.Classify(host.Facts??new(){Hostname=host.Hostname},existing);
            var content=new StackPanel{Spacing=8};
            var heading=new Grid{ColumnDefinitions=new("32,*,Auto")};var icon=new ContentControl{Content=new DeviceLegendIcon(classification.EffectiveType),VerticalAlignment=VerticalAlignment.Center};heading.Children.Add(icon);
            var label=new TextBlock{Text=$"{host.Address}  {host.Hostname}",FontWeight=FontWeight.SemiBold,FontSize=14,TextWrapping=TextWrapping.Wrap};Grid.SetColumn(label,1);heading.Children.Add(label);
            var choice=new CheckBox{IsChecked=existing==null,VerticalAlignment=VerticalAlignment.Top};Grid.SetColumn(choice,2);heading.Children.Add(choice);content.Children.Add(heading);
            var assessmentLabel=Muted(classification.Summary);content.Children.Add(assessmentLabel);content.Children.Add(Muted(classification.Explanation));
            var kinds=DiscoveryReview.EditableTypes.Concat(existing==null?[]:new[]{existing.Kind}).Distinct().ToArray();
            var type=new ComboBox{Name="ReviewDeviceType",ItemsSource=new[]{$"Automatic · {DeviceClassifier.Label(classification.AutomaticType)}"}.Concat(kinds.Select(k=>$"Manual · {DeviceClassifier.Label(k)}")).ToList(),SelectedIndex=classification.ManualType is {} manual?Array.IndexOf(kinds,manual)+1:0,HorizontalAlignment=HorizontalAlignment.Stretch};
            bool locked=existing!=null&&DiscoveryReview.TypeLocked(vm.Project,existing);
            if(locked){type.SelectedIndex=Array.IndexOf(kinds,existing!.Kind)+1;type.IsEnabled=false;}
            type.SelectionChanged+=(_,_)=>{if(!locked&&choice.IsEnabled){choice.IsChecked=true;var kind=type.SelectedIndex>0?kinds[type.SelectedIndex-1]:classification.AutomaticType;icon.Content=new DeviceLegendIcon(kind);assessmentLabel.Text=type.SelectedIndex>0?$"Manual correction · {DeviceClassifier.Label(kind)} (automatic: {DeviceClassifier.Label(classification.AutomaticType)})":$"Automatic · {DeviceClassifier.Label(kind)} · {classification.Confidence} confidence";}};
            content.Children.Add(FieldLabel("Device type"));content.Children.Add(type);
            content.Children.Add(Muted(locked?"Type is protected by existing host, stack or child relationships.":existing==null?"New device · select to add to the map.":"Existing device · select to update or move it."));
            var evidence=new StackPanel{Spacing=6};evidence.Children.Add(Muted(string.Join(" · ",new[]{classification.Vendor,classification.Model,classification.OperatingSystem,string.Join(", ",classification.Roles)}.Where(t=>t.Length>0))));
            foreach(var item in classification.Evidence)evidence.Children.Add(Muted($"{item.Source}: {item.Detail}"));
            foreach(var limit in classification.Limitations.Distinct())evidence.Children.Add(Muted("Limited: "+limit));
            content.Children.Add(new Expander{Header="Evidence and discovery limits",Content=evidence,HorizontalAlignment=HorizontalAlignment.Stretch});
            rows.Add((choice,host,type,kinds));results.Children.Add(Card(content));
        }
        reviewSummary.Text=$"{report.Hosts.Count} devices · review types, select cards, then choose a destination. {report.Completed}/{total} targets checked.";
        status.Text=$"{(report.Cancelled?"Stopped":"Scan complete")} · {report.Hosts.Count} devices · {report.Errors} probe errors";
        add.IsEnabled=!running&&rows.Count>0;tabs.SelectedIndex=1;
    }
    private void Import()
    {
        var selected=rows.Where(r=>r.Choice.IsEnabled&&r.Choice.IsChecked==true).Select(r=>new DiscoverySelection(r.Host,r.Type.SelectedIndex>0?r.Kinds[r.Type.SelectedIndex-1]:null,r.Type.IsEnabled&&r.Type.SelectedIndex==0)).ToList();
        var destination=new DiscoveryDestination((DiscoveryPlacement)placement.SelectedIndex,
            container.SelectedIndex>=0?containerIds[container.SelectedIndex]:null,subnetName.Text??"",subnetCidr.Text??"",subnetParent.SelectedIndex>=0?parentIds[subnetParent.SelectedIndex]:null);
        try{
            DiscoveryApplied? applied=null;vm.Edit(()=>applied=DiscoveryReview.Apply(vm.Project,selected,destination));
            vm.Selection.Clear();foreach(var node in applied!.Devices)vm.Selection.Add(node.Id);vm.Status=$"Applied {applied.Devices.Count} devices"+(applied.CreatedSubnet!=null?$" to new subnet {applied.CreatedSubnet.Name}":"");vm.RefreshDetails();
            foreach(var row in rows.Where(r=>selected.Any(s=>s.Host.Address==r.Host.Address))){row.Choice.IsChecked=false;row.Choice.IsEnabled=false;row.Type.IsEnabled=false;}
            add.IsEnabled=rows.Any(r=>r.Choice.IsEnabled);status.Text=vm.Status+". One Undo restores this change.";
            var selectedContainer=applied.CreatedSubnet?.Id??destination.ContainerId;
            RefreshContainers();
            if(selectedContainer!=null&&containerIds.Contains(selectedContainer)){placement.SelectedIndex=1;container.SelectedIndex=containerIds.IndexOf(selectedContainer);}
            else placement.SelectedIndex=0;
        }catch(Exception ex)when(ex is ArgumentException or InvalidDataException or InvalidOperationException){status.Text=ex.Message;}
    }
}
