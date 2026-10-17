using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RackTrail.Core;

namespace RackTrail.Desktop;

public sealed class MainWindow : Window
{
    private readonly EditorViewModel vm = new();
    private readonly MapCanvas canvas;
    private readonly IPlatformServices platform;
    private readonly IconLibrary iconLibrary=new();
    private readonly DispatcherTimer iconRefresh=new(){Interval=TimeSpan.FromSeconds(3)};
    private string iconFingerprint="";
    private readonly StackPanel properties = new() { Spacing = 9, Margin = new Thickness(18) };
    private readonly StackPanel inventory = new() { Spacing = 3 };
    private readonly TextBlock status = new() { Margin = new Thickness(18, 8) };
    private readonly TextBlock summary = new() { FontSize = 11, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBox search = new() { Watermark = "Search names, IPs, subnets…" };
    private readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromSeconds(30) };
    private string propertyKey = "";
    private readonly List<Action> propertyCommits = [];
    private bool closingApproved, savingRecovery;
    public MainWindow()
    {
        CanResize=true; Title = "RackTrail"; Icon = Branding.CreateIcon();Width = 1500; Height = 940; MinWidth = 1080; MinHeight = 700; FontFamily = new FontFamily("Inter");
        DataContext = vm; canvas = new(vm); platform = new DesktopPlatformServices(this); Background = new SolidColorBrush(Color.Parse("#151e2b"));
        var root = new Grid { RowDefinitions = new("Auto,Auto,*,Auto") };
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(22, 17) };
        var brandLogo = new BrandLogo(); heading.Children.Add(brandLogo);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; actions.Children.Add(Button("New", async () => { if (await CanDiscard()) vm.Replace(new Project()); })); actions.Children.Add(Button("Open", Open)); actions.Children.Add(Button("Save", () => Save(false))); actions.Children.Add(Button("Save as", () => Save(true))); actions.Children.Add(Button("Theme", () => { Application.Current!.RequestedThemeVariant = Application.Current.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark; Background = new SolidColorBrush(Color.Parse(Application.Current.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? "#f4f7fb" : "#151e2b")); canvas.InvalidateVisual(); })); Grid.SetColumn(actions, 1); heading.Children.Add(actions); root.Children.Add(heading);
        actions.Children.Add(Button("About",()=>new AboutWindow().Show(this)));
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(18, 0, 18, 12) };
        toolbar.Children.Add(Button("↶ Undo", vm.Undo)); toolbar.Children.Add(Button("↷ Redo", vm.Redo)); toolbar.Children.Add(Button("Connect", () => { vm.ConnectMode = !vm.ConnectMode; vm.ConnectFrom = null; vm.Status = vm.ConnectMode ? "Connect: click two devices" : "Connection tool off"; vm.Notify(); })); toolbar.Children.Add(Button("Auto layout", () => vm.Edit(() => { MapLayout.Arrange(vm.Project); canvas.Fit(); }))); toolbar.Children.Add(Button("Align top", () => vm.Align(true))); toolbar.Children.Add(Button("Align left", () => vm.Align(false))); toolbar.Children.Add(Button("Fit", canvas.Fit));
        var snap = new CheckBox { Content = "Snap", IsChecked = true, Margin = new Thickness(8, 0) }; snap.IsCheckedChanged += (_, _) => canvas.Snap = snap.IsChecked == true; toolbar.Children.Add(snap);
        toolbar.Children.Add(Button("Text export", () => { new TextExportWindow(vm, platform).Show(this); })); toolbar.Children.Add(Button("SVG", () => ExportVisual("svg"))); toolbar.Children.Add(Button("PNG", () => ExportVisual("png"))); toolbar.Children.Add(Button("Validate", ShowValidation)); Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        var workspace = new Grid { ColumnDefinitions = new("235,5,*,5,285") };
        workspace.ColumnDefinitions[0].MinWidth=180;workspace.ColumnDefinitions[2].MinWidth=360;workspace.ColumnDefinitions[4].MinWidth=220;
        foreach(int col in new[]{1,3}){var splitter=new GridSplitter{ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext,HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brushes.SlateGray};Grid.SetColumn(splitter,col);workspace.Children.Add(splitter);}
        var left = new StackPanel { Margin = new Thickness(16), Spacing = 8 }; left.Children.Add(Label("DEVICE LIBRARY")); left.Children.Add(new TextBlock { Text = "Drag onto the canvas, or click to add", FontSize = 10, Foreground = Brushes.SlateGray });
        var palette = new Grid { ColumnDefinitions = new("*,*") }; int i = 0;

        foreach (var kind in Enum.GetValues<NodeKind>().Where(k => !new Node{Kind=k}.IsContainer)) { if(i%2==0)palette.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); var k = kind; var b = Button(kind.ToString(), () => { vm.Add(k, 100 + vm.Project.Nodes.Count % 5 * 30, 100 + vm.Project.Nodes.Count % 5 * 30); }); b.FontSize = 11; b.Margin = new Thickness(2); b.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumn(b, i % 2); Grid.SetRow(b, i / 2); palette.Children.Add(b); i++; EnablePaletteDrag(b, k); }
        left.Children.Add(palette);
        left.Children.Add(Button("+ Switch stack (5 members)",()=>vm.AddSwitchStack()));
        left.Children.Add(Button("Open icons folder",()=>{try{platform.OpenFolder(iconLibrary.FolderPath);}catch(Exception ex){vm.Status=ex.Message;vm.Notify();}}));
        left.Children.Add(Button("Reload icons",async()=>{ReloadIcons();await Message("Device icons",iconLibrary.Warnings.Count==0?$"Loaded {iconLibrary.Images.Count} user icons. Built-in logos are also available.":string.Join("\n",iconLibrary.Warnings));}));
        left.Children.Add(Button("Scan subnet",async()=>{foreach(var commit in propertyCommits.ToArray())commit();await new ScanWindow(vm).ShowDialog(this);canvas.Fit();}));
        left.Children.Add(Label("CONTAINERS")); var kinds = new ComboBox { ItemsSource = Enum.GetValues<NodeKind>().Where(k => new Node{Kind=k}.IsContainer).ToList(), SelectedIndex = 3, HorizontalAlignment = HorizontalAlignment.Stretch }; left.Children.Add(kinds); left.Children.Add(Button("+ Add container", () => vm.Add((NodeKind)kinds.SelectedItem!)));
        left.Children.Add(new Separator { Margin = new Thickness(0, 9) }); left.Children.Add(Label("NETWORK EXPLORER")); left.Children.Add(search); left.Children.Add(summary); left.Children.Add(inventory); search.TextChanged += (_, _) => RefreshInventory();
        workspace.Children.Add(new Border { Child = new ScrollViewer { Content = left }, BorderBrush = new SolidColorBrush(Color.Parse("#334155")), BorderThickness = new Thickness(0, 1, 1, 0) });
        var center = new Grid { RowDefinitions = new("Auto,*") }; var canvasHead = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(16, 12) }; var projectName = new TextBlock { FontWeight = FontWeight.SemiBold }; vm.Changed += () => projectName.Text = vm.Project.Name; projectName.Text = vm.Project.Name; canvasHead.Children.Add(projectName); var hint = new TextBlock { Text = "SCROLL  zoom    /    MIDDLE DRAG  pan", FontSize = 10, Foreground = Brushes.SlateGray }; Grid.SetColumn(hint, 1); canvasHead.Children.Add(hint); center.Children.Add(canvasHead); Grid.SetRow(canvas, 1); center.Children.Add(canvas); Grid.SetColumn(center, 2); workspace.Children.Add(center);
        var right = new Border { Child = new ScrollViewer { Content = properties }, BorderBrush = new SolidColorBrush(Color.Parse("#334155")), BorderThickness = new Thickness(1, 1, 0, 0) }; Grid.SetColumn(right, 4); workspace.Children.Add(right); Grid.SetRow(workspace, 2); root.Children.Add(workspace);
        var footer = new Border { Child = status, BorderBrush = new SolidColorBrush(Color.Parse("#334155")), BorderThickness = new Thickness(0, 1, 0, 0) }; Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        vm.Changed += ()=>iconLibrary.ApplyTo(vm.Project);vm.Changed += Refresh;iconLibrary.ApplyTo(vm.Project);Refresh(); KeyDown += Shortcut;
        iconRefresh.Tick+=(_,_)=>{try{var fingerprint=Directory.Exists(iconLibrary.FolderPath)?string.Join(";",Directory.EnumerateFiles(iconLibrary.FolderPath).Order(StringComparer.Ordinal).Select(p=>p+File.GetLastWriteTimeUtc(p).Ticks+new FileInfo(p).Length)):"";if(fingerprint!=iconFingerprint){iconFingerprint=fingerprint;ReloadIcons();}}catch(IOException){ }catch(UnauthorizedAccessException){ }};iconRefresh.Start();Closed+=(_,_)=>iconRefresh.Stop();
        autosave.Tick += async (_, _) => { if (!vm.Dirty || savingRecovery) return; savingRecovery = true; try { await ProjectStorage.SaveAsync(ProjectStorage.Clone(vm.Project), platform.RecoveryPath); } catch (Exception ex) { vm.Status = "Autosave failed: " + ex.Message; Refresh(); } finally { savingRecovery = false; } }; autosave.Start();
        Opened += async (_, _) => {
            canvas.Fit();bool recovered=false;
            if(File.Exists(platform.RecoveryPath)&&await Confirm("Recovery available","Restore the last autosaved network?")){
                try{vm.Replace(await ProjectStorage.LoadAsync(platform.RecoveryPath));vm.Touch();canvas.Fit();recovered=true;}catch(Exception ex){await Message("Recovery failed",ex.Message);}
            }
            if(!recovered&&StartupPreferences.Load().AskToLoadSample)await AskForSample();
        };
        Closing += async (_, e) => { if (closingApproved) return; e.Cancel = true; if (await CanDiscard()) { closingApproved = true; autosave.Stop(); Close(); } };
    }
    public static TextBlock Label(string text) => new() { Text = text, FontSize = 10, FontWeight = FontWeight.Bold, LetterSpacing = 1.5, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 8, 0, 4) };
    public static Button Button(string text, Action action) { var b = new Button { Content = text, Padding = new Thickness(10, 7) }; b.Click += (_, _) => action(); return b; }
    public static Button Button(string text, Func<Task> action) { var b = new Button { Content = text, Padding = new Thickness(10, 7) }; b.Click += async (_, _) => { await action(); }; return b; }
    private void EnablePaletteDrag(Button button, NodeKind kind)
    {
        Point origin = default; bool pressed = false, moved = false;
        button.AddHandler(PointerPressedEvent, (_, e) => { origin = e.GetPosition(this); pressed = true; moved = false; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.AddHandler(PointerMovedEvent, (_, e) => { var p = e.GetPosition(this); if (pressed && Math.Abs(p.X - origin.X) + Math.Abs(p.Y - origin.Y) > 8) moved = true; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.AddHandler(PointerReleasedEvent, (_, e) => { if (pressed && moved) { var pos = e.GetPosition(canvas); if (new Rect(canvas.Bounds.Size).Contains(pos)) { var world = canvas.World(pos); vm.Add(kind, world.X, world.Y); e.Handled = true; } } pressed = false; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    private void Refresh() { status.Text = vm.Status + "  •  " + vm.Project.Nodes.Count + " nodes / " + vm.Project.Connections.Count + " links"; Title = (vm.Dirty ? "● " : "") + vm.Project.Name + " / RackTrail"; RefreshInventory(); var key = (vm.Selected?.Id ?? vm.SelectedConnection?.Id ?? "none") + ":" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(vm.Project)+":"+vm.Selected?.Kind+":"+vm.Selected?.StackMembers.Count+":"+vm.PropertiesRevision; if (key != propertyKey) { propertyKey = key; RefreshProperties(); } }
    private void RefreshInventory()
    {
        inventory.Children.Clear(); string query = search.Text ?? ""; var nodes = vm.Project.Nodes.Where(n => (n.Name + " " + n.Addresses + " " + n.Cidr + " " + n.Role+" "+string.Join(" ",n.StackMembers.Select(m=>$"{m.Name} {m.Model} {m.SerialNumber}"))).Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (var n in nodes) { var b = Button((n.IsContainer ? "▣ " : "· ") + n.Name+(n.IsStack?$" ({n.StackMembers.Count})":""), () => { vm.Select(n); canvas.Pan = new(50 - n.X * canvas.Zoom, 50 - n.Y * canvas.Zoom); canvas.InvalidateVisual(); }); b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left; b.FontSize = 11; inventory.Children.Add(b); }
        summary.Text = $"{vm.Project.Nodes.Count(n => !n.IsContainer)} devices   ·   {vm.Project.Nodes.Count(n => n.Kind == NodeKind.Subnet)} subnets";
    }
    private void Field(string label, string value, Action<string> assign, bool multiline = false, StackPanel? panel = null)
    {
        var target = panel ?? properties;
        target.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Brushes.SlateGray }); var box = new TextBox { Text = value, AcceptsReturn = multiline, MinHeight = multiline ? 75 : 32 }; string previous = value;
        void Commit() { var text = box.Text ?? ""; if (text != previous) { previous = text; vm.Edit(() => assign(text)); } }
        propertyCommits.Add(Commit);box.LostFocus += (_, _) => Commit(); target.Children.Add(box);
    }
    private void RefreshProperties()
    {
        propertyCommits.Clear(); properties.Children.Clear(); properties.Children.Add(Label("PROPERTIES"));
        if (vm.SelectedConnection is { } c) { properties.Children.Add(new TextBlock { Text = "Network connection", FontSize = 17 }); ConnectionEndpoint(c,true);ConnectionEndpoint(c,false);Field("Additional port label", c.Ports, v => c.Ports = v); Field("Protocol", c.Protocol, v => c.Protocol = v); Field("Bandwidth", c.Bandwidth, v => c.Bandwidth = v); Field("Purpose", c.Purpose, v => c.Purpose = v); properties.Children.Add(Button("Reset automatic route", () => vm.Edit(() => c.Route = []))); properties.Children.Add(Button("Delete connection", vm.Delete)); return; }
        if (vm.Selected is not { } n) { properties.Children.Add(new TextBlock { Text = "A place for every connection.", FontSize = 19, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12) }); properties.Children.Add(new TextBlock { Text = "Select a device, container, or connection to edit its properties.\n\nCtrl + click for multiple selection. Drag empty canvas to select a region. Double-click a container to collapse it.", TextWrapping = TextWrapping.Wrap, FontSize = 12 }); Field("Project name", vm.Project.Name, v => vm.Project.Name = v); properties.Children.Add(Button("Load sample network", async () => { if (await CanDiscard()) { vm.Replace(SampleNetwork.Create()); canvas.Fit(); } })); return; }
        properties.Children.Add(new TextBlock { Text = n.DisplayType, FontSize = 18, FontWeight = FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap }); Field("Name", n.Name, v => n.Name = v);
        if(n.Kind==NodeKind.Phones)properties.Children.Add(new TextBlock{Text="Represents the phones on a subnet. Assign container membership below; individual handset IP addresses are optional.",FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray});
        var type = new ComboBox { ItemsSource = Enum.GetValues<NodeKind>().Where(k => new Node{Kind=k}.IsContainer == n.IsContainer).ToList(), SelectedItem = n.Kind, IsEnabled=!n.IsStack&&n.HostId==null&&!vm.Project.Nodes.Any(x=>x.ParentId==n.Id||x.HostId==n.Id),HorizontalAlignment = HorizontalAlignment.Stretch }; type.SelectionChanged += (_, _) => { if (type.SelectedItem is NodeKind k && k != n.Kind) {vm.Edit(() => {n.Kind = k;if(n.CanContainNodes)Containment.Resize(vm.Project,n,Math.Max(460,n.Width),Math.Max(240,n.Height));});vm.RefreshDetails();} }; properties.Children.Add(type);
        if(!n.IsContainer){Field("Vendor",n.Vendor,v=>n.Vendor=v);Field("Model",n.Model,v=>n.Model=v);IconProperties(n);
            if(!n.CanContainNodes){var shape=new ComboBox{ItemsSource=Enum.GetValues<DeviceShape>(),SelectedItem=n.Shape,HorizontalAlignment=HorizontalAlignment.Stretch};shape.SelectionChanged+=(_,_)=>{if(shape.SelectedItem is DeviceShape value&&value!=n.Shape){vm.Edit(()=>{n.Shape=value;Containment.Resize(vm.Project,n,n.Width,n.Height);});vm.RefreshDetails();}};properties.Children.Add(shape);
                properties.Children.Add(Button(n.UseDeviceImage?"Use standard shape":"Use device image",()=>{vm.Edit(()=>{n.UseDeviceImage=!n.UseDeviceImage;if(n.UseDeviceImage)Containment.Resize(vm.Project,n,n.Width,Math.Max(132,n.Height));});vm.RefreshDetails();}));
                properties.Children.Add(new TextBlock{Text="Choose a model image above, then use the button to replace the shape. Add PNG/JPG files to the icons folder; a filename matching the model is selected automatically in image mode.",FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray});}}
        if(n.Kind==NodeKind.Hypervisor){Field("Hypervisor platform",n.HypervisorPlatform,v=>n.HypervisorPlatform=v);properties.Children.Add(Button("+ Add virtual machine",()=>vm.AddVirtualMachine(n)));properties.Children.Add(Button(n.Collapsed?"Expand host":"Collapse host",()=>{vm.Edit(()=>n.Collapsed=!n.Collapsed);vm.RefreshDetails();}));}
        if(n.Kind==NodeKind.VirtualMachine){var hosts=vm.Project.Nodes.Where(x=>x.Kind==NodeKind.Hypervisor).ToList();var hostChoice=new ComboBox{ItemsSource=new[]{"(No host)"}.Concat(hosts.Select(x=>x.Name)).ToList(),SelectedIndex=n.HostId==null?0:hosts.FindIndex(x=>x.Id==n.HostId)+1,HorizontalAlignment=HorizontalAlignment.Stretch};hostChoice.SelectionChanged+=(_,_)=>{var id=hostChoice.SelectedIndex<=0?null:hosts[hostChoice.SelectedIndex-1].Id;if(id!=n.HostId)vm.AssignHost(n,id);};properties.Children.Add(Label("HYPERVISOR HOST"));properties.Children.Add(hostChoice);}
        var parents = vm.Project.Nodes.Where(x => x.CanContainNodes && x.Id != n.Id && !vm.Project.Ancestors(x).Any(a => a.Id == n.Id)).ToList(); var combo = new ComboBox { ItemsSource = new[] { "(No container)" }.Concat(parents.Select(x => x.Name)).ToList(), SelectedIndex = n.ParentId == null ? 0 : parents.FindIndex(x => x.Id == n.ParentId) + 1, HorizontalAlignment = HorizontalAlignment.Stretch }; properties.Children.Add(new TextBlock { Text = "Container membership", FontSize = 11, Foreground = Brushes.SlateGray }); combo.SelectionChanged += (_, _) => { var parent = combo.SelectedIndex <= 0 ? null : parents[combo.SelectedIndex - 1].Id; if (parent != n.ParentId) vm.AssignContainer(n,parent); }; properties.Children.Add(combo);
        if (n.IsContainer) { Field("CIDR", n.Cidr, v => n.Cidr = v); Field("Gateway", n.Gateway, v => n.Gateway = v); Field("VLAN ID", n.VlanId, v => n.VlanId = v); properties.Children.Add(Button(n.Collapsed ? "Expand container" : "Collapse container", () => { vm.Edit(() => n.Collapsed = !n.Collapsed); propertyKey = ""; Refresh(); })); }
        else { Field("Hostname", n.Hostname, v => n.Hostname = v); Field(n.IsStack?"Shared management IP addresses":"IP addresses (comma separated)", n.Addresses, v => n.Addresses = v); Field("Operating system", n.OperatingSystem, v => n.OperatingSystem = v); Field("Role", n.Role, v => n.Role = v); }
        if(n.Kind==NodeKind.Switch)
        {
            Field("Management interface (SSH / HTTPS address)",n.ManagementInterface,v=>n.ManagementInterface=v);
            if(n.IsStack)StackProperties(n);
            else properties.Children.Add(Button("Convert to 5-member stack",()=>vm.Edit(()=>n.StackMembers=Enumerable.Range(1,5).Select(number=>new StackMember{Number=number,Name=$"Switch {number}"}).ToList())));
        }
        Field(n.IsContainer ? "Description" : "Notes", n.Notes, v => n.Notes = v, true); var exceptions = new CheckBox { Content = "Allow validation exceptions", IsChecked = n.AllowValidationExceptions }; exceptions.IsCheckedChanged += (_, _) => { if (n.AllowValidationExceptions != (exceptions.IsChecked == true)) vm.Edit(() => n.AllowValidationExceptions = exceptions.IsChecked == true); }; properties.Children.Add(exceptions); properties.Children.Add(Button("Delete selected", vm.Delete));
        { Field("Width", n.Width.ToString(System.Globalization.CultureInfo.InvariantCulture), v => { if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) Containment.Resize(vm.Project,n,Math.Clamp(number,100,10000),n.Height); }); Field("Height", n.Height.ToString(System.Globalization.CultureInfo.InvariantCulture), v => { if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) Containment.Resize(vm.Project,n,n.Width,Math.Clamp(number,60,10000)); }); }
    }
    private async Task AskForSample()
    {
        var dialog=new Window{Title="Start your map",Icon=Branding.CreateIcon(),Width=450,Height=240,MinWidth=360,MinHeight=220,CanResize=true,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(24),Spacing=16};panel.Children.Add(new TextBlock{Text="Would you like to load the sample network, or start with a blank map?",TextWrapping=TextWrapping.Wrap});
        var skip=new CheckBox{Content="Don't ask again"};panel.Children.Add(skip);var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};buttons.Children.Add(Button("Start blank",()=>dialog.Close(false)));buttons.Children.Add(Button("Load sample",()=>dialog.Close(true)));panel.Children.Add(buttons);dialog.Content=panel;
        bool load=await dialog.ShowDialog<bool>(this);
        if(skip.IsChecked==true){try{new StartupPreferences{AskToLoadSample=false}.Save();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){await Message("Startup preference", "Could not save your preference: "+ex.Message);}}
        if(load){vm.Replace(SampleNetwork.Create());canvas.Fit();}
    }
    private void ReloadIcons(){iconLibrary.Reload();if(iconLibrary.ApplyTo(vm.Project))vm.Touch();propertyKey="";vm.Notify();}
    private void IconProperties(Node node)
    {
        properties.Children.Add(new TextBlock{Text="Device icon",FontSize=11,Foreground=Brushes.SlateGray});
        var keys=new[]{"auto","device"}.Concat(DeviceIcons.BuiltInKeys).Concat(iconLibrary.Images.Keys).Concat(node.IconKey is "auto" or "device"?Array.Empty<string>():new[]{node.IconKey}).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var combo=new ComboBox{ItemsSource=keys.Select(k=>k=="auto"?"Automatic (vendor / OS)":k=="device"?"Standard device icon":k).ToList(),SelectedIndex=keys.FindIndex(k=>k.Equals(node.IconKey,StringComparison.OrdinalIgnoreCase)),HorizontalAlignment=HorizontalAlignment.Stretch};
        combo.SelectionChanged+=(_,_)=>{if(combo.SelectedIndex>=0&&node.IconKey!=keys[combo.SelectedIndex])vm.Edit(()=>node.IconKey=keys[combo.SelectedIndex]);};properties.Children.Add(combo);
    }
    private void ConnectionEndpoint(Connection c,bool from)
    {
        var node=vm.Project.Nodes.First(n=>n.Id==(from?c.From:c.To));var memberId=from?c.FromStackMemberId:c.ToStackMemberId;
        properties.Children.Add(Label((from?"FROM / ":"TO / ")+node.Name));
        if(node.IsStack)
        {
            var members=node.StackMembers.OrderBy(m=>m.Number).ToList();var combo=new ComboBox{ItemsSource=new[]{"Entire stack / unspecified"}.Concat(members.Select(m=>$"Member {m.Number} / {m.Name}")).ToList(),SelectedIndex=memberId==null?0:members.FindIndex(m=>m.Id==memberId)+1,HorizontalAlignment=HorizontalAlignment.Stretch};
            combo.SelectionChanged+=(_,_)=>{var id=combo.SelectedIndex<=0?null:members[combo.SelectedIndex-1].Id;if(id!=(from?c.FromStackMemberId:c.ToStackMemberId))vm.Edit(()=>{if(from)c.FromStackMemberId=id;else c.ToStackMemberId=id;});};properties.Children.Add(combo);
        }
        Field(from?"From port (e.g. Gi3/0/24)":"To port",from?c.FromPort:c.ToPort,v=>{if(from)c.FromPort=v;else c.ToPort=v;});
    }
    private void StackProperties(Node node)
    {
        properties.Children.Add(Label("SWITCH STACK"));
        properties.Children.Add(new TextBlock{Text="One shared management interface. Physical members have their own identity and connection ports.",FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray});
        Field("Stack technology",node.StackTechnology,v=>node.StackTechnology=v);
        var show=new CheckBox{Content="Show members on canvas",IsChecked=node.ShowStackMembers};show.IsCheckedChanged+=(_,_)=>{if(node.ShowStackMembers!=(show.IsChecked==true))vm.Edit(()=>node.ShowStackMembers=show.IsChecked==true);};properties.Children.Add(show);
        properties.Children.Add(Button("+ Add member",()=>vm.AddStackMember(node)));
        foreach(var member in node.StackMembers.OrderBy(m=>m.Number))
        {
            var body=new StackPanel{Spacing=7,Margin=new Thickness(4)};var expander=new Expander{Header=$"Member {member.Number} / {member.Name}",Content=body,HorizontalAlignment=HorizontalAlignment.Stretch};
            Field("Member number",member.Number.ToString(),v=>{if(int.TryParse(v,out var number))member.Number=number;expander.Header=$"Member {member.Number} / {member.Name}";},panel:body);
            Field("Name",member.Name,v=>{member.Name=v;expander.Header=$"Member {member.Number} / {member.Name}";},panel:body);
            Field("Model",member.Model,v=>member.Model=v,panel:body);Field("Serial number",member.SerialNumber,v=>member.SerialNumber=v,panel:body);
            Field("Role (active / standby / member)",member.Role,v=>member.Role=v,panel:body);Field("Status (documented)",member.Status,v=>member.Status=v,panel:body);Field("Notes",member.Notes,v=>member.Notes=v,multiline:true,panel:body);
            int links=vm.Project.Connections.Count(c=>c.From==node.Id&&c.FromStackMemberId==member.Id||c.To==node.Id&&c.ToStackMemberId==member.Id);
            var remove=Button("Remove member",()=>vm.RemoveStackMember(node,member));remove.IsEnabled=links==0;body.Children.Add(remove);
            if(links>0)body.Children.Add(new TextBlock{Text=$"{links} connection(s) use this member. Reassign them before removal.",TextWrapping=TextWrapping.Wrap,FontSize=10});
            properties.Children.Add(expander);
        }
    }
    private async Task Open() { if (!await CanDiscard()) return; var path = await platform.OpenProject(); if (path == null) return; try { vm.Replace(await ProjectStorage.LoadAsync(path), path); canvas.Fit(); } catch (Exception ex) { await Message("Cannot open project", ex.Message); } }
    private async Task Save(bool saveAs) { foreach(var commit in propertyCommits.ToArray())commit();var path = saveAs || vm.FilePath == null ? await platform.SavePath("Save network project", "json") : vm.FilePath; if (path == null) return; try { await ProjectStorage.SaveAsync(vm.Project, path); vm.FilePath = path; vm.Saved(); if (File.Exists(platform.RecoveryPath)) File.Delete(platform.RecoveryPath); vm.Status = "Saved " + Path.GetFileName(path); vm.Notify(); } catch (Exception ex) { await Message("Save failed", ex.Message); } }
    private async Task ExportVisual(string extension) { var path = await platform.SavePath("Export " + extension.ToUpperInvariant(), extension); if (path == null) return; try { if (extension == "svg") await File.WriteAllTextAsync(path, VisualExporter.Svg(vm.Project)); else PngExporter.Save(vm.Project, path); vm.Status = "Exported " + Path.GetFileName(path); vm.Notify(); } catch (Exception ex) { await Message("Export failed", ex.Message); } }
    private async Task ShowValidation() { var issues = NetworkValidator.Validate(vm.Project); await Message("Network validation", issues.Count == 0 ? "All addresses and subnets are valid." : string.Join("\n\n", issues.Select(i => (i.Excepted ? "[Allowed exception] " : "") + i.Message))); }
    private async Task<bool> CanDiscard() { foreach(var commit in propertyCommits.ToArray())commit();if (!vm.Dirty) return true; var result = await Choice("Unsaved changes", "Save your changes before continuing?", ["Save", "Discard", "Cancel"]); if (result == "Save") { await Save(false); return !vm.Dirty; } return result == "Discard"; }
    private async Task<bool> Confirm(string title, string text) => await Choice(title, text, ["Restore", "Skip"]) == "Restore";
    private async Task Message(string title, string text) => await Choice(title, text, ["OK"]);
    private async Task<string?> Choice(string title, string text, string[] choices)
    {
        var w = new Window { Title = title, Icon=Branding.CreateIcon(),Width = 520, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = true }; var panel = new DockPanel { Margin = new Thickness(24) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right }; foreach (var choice in choices) buttons.Children.Add(Button(choice, () => w.Close(choice))); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } }); w.Content = panel; return await w.ShowDialog<string?>(this);
    }
    private async void Shortcut(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { switch (e.Key) { case Key.S: Focus(); await Save(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); break; case Key.O: await Open(); break; case Key.Z: vm.Undo(); break; case Key.Y: vm.Redo(); break; case Key.C: if (FocusManager?.GetFocusedElement() is TextBox) return; vm.Copy(); break; case Key.V: if (FocusManager?.GetFocusedElement() is TextBox) return; vm.Paste(); break; case Key.A: if (FocusManager?.GetFocusedElement() is TextBox) return; foreach (var n in vm.Project.Nodes.Where(vm.Project.Visible)) vm.Selection.Add(n.Id); vm.Notify(); break; default: return; } e.Handled = true; }
        else if (e.Key == Key.Delete && FocusManager?.GetFocusedElement() is not TextBox) { vm.Delete(); e.Handled = true; } else if (e.Key == Key.Escape) { vm.ConnectMode = false; vm.ConnectFrom = null; vm.Select(null); }
    }
}
