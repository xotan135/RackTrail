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
    private readonly IconLibrary iconLibrary=new(load:false);
    private bool checkingIcons;
    private Task? reloadIconsTask;
    private readonly DispatcherTimer iconRefresh=new(){Interval=TimeSpan.FromSeconds(3)};
    private string iconFingerprint="";
    private readonly StackPanel properties = new() { Spacing = 9, Margin = new Thickness(18) };
    private sealed record InventoryRow(Node Node,string Label,NodeKind Kind);
    private readonly ListBox inventory=new(){Height=320,HorizontalAlignment=HorizontalAlignment.Stretch};
    private long inventoryRevision=-1,iconRevision=-1;
    private Project? inventoryProject;
    private string inventoryQuery="";
    private NodeKind? inventoryType;
    private ClassificationConfidence? inventoryConfidence;
    private bool updatingInventory;
    private InventoryRow[] inventoryRows=[];
    private readonly CancellationTokenSource lifetime=new();
    private readonly TextBlock status = new() { Margin = new Thickness(18, 8) };
    private readonly TextBlock summary = new() { FontSize = 11, Classes={"muted"}, Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBox search = new() { Name="MapSearch", Watermark = "Search names, IPs, VLANs…" };
    private readonly ComboBox typeFilter=new(){Name="MapTypeFilter",ItemsSource=new[]{"All device types"}.Concat(DeviceClassifier.Types.Select(DeviceClassifier.Label)).ToList(),SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ComboBox confidenceFilter=new(){ItemsSource=new[]{"All confidence levels","Low","Medium","High"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromSeconds(30) };
    private string propertyKey = "";
    private readonly List<Action> propertyCommits = [];
    private bool closingApproved, savingRecovery, profiling, exporting;
    internal void ConfigureProfiling(string mapPath,string outputDirectory)
    {
        profiling=true;vm.Replace(ProjectStorage.Deserialize(File.ReadAllText(mapPath)));
        canvas.Performance=new PerformanceRecorder();Closed+=(_,_)=>{try{canvas.Performance!.Write(outputDirectory);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){System.Diagnostics.Debug.WriteLine("Profiling output failed: "+ex.Message);}};
    }
    public MainWindow()
    {
        CanResize=true; Title = "RackTrail"; Icon = Branding.CreateIcon();Width = 1500; Height = 940; MinWidth = 1080; MinHeight = 700; FontFamily = new FontFamily("Inter");
        inventory.ItemsPanel=new Avalonia.Controls.Templates.FuncTemplate<Panel?>(()=>new VirtualizingStackPanel());
        inventory.ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<InventoryRow>((row,_)=>{
            if(row==null)return new TextBlock();
            var grid=new Grid{ColumnDefinitions=new("24,*"),Margin=new Thickness(2,4)};
            grid.Children.Add(new DeviceLegendIcon(row.Node.Kind));var label=new TextBlock{Text=row.Label,TextWrapping=TextWrapping.Wrap,FontSize=11};Grid.SetColumn(label,1);grid.Children.Add(label);return grid;
        });
        inventory.SelectionChanged+=(_,_)=>{if(!updatingInventory&&inventory.SelectedItem is InventoryRow row)FocusItem(row.Node);};
        DataContext = vm; canvas = new(vm); platform = new DesktopPlatformServices(this);
        var root = new Grid { RowDefinitions = new("Auto,Auto,*,Auto") };
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(22, 17) };
        var brandLogo = new BrandLogo(); heading.Children.Add(brandLogo);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; actions.Children.Add(UserButton("New", async () => { if (await CanDiscard()) vm.Replace(new Project()); })); actions.Children.Add(UserButton("Open", Open)); actions.Children.Add(UserButton("Save", () => Save(false))); actions.Children.Add(UserButton("Save as", () => Save(true))); actions.Children.Add(UserButton("Theme", () => { Application.Current!.RequestedThemeVariant = Application.Current.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark; canvas.InvalidateVisual(); })); Grid.SetColumn(actions, 1); heading.Children.Add(actions); root.Children.Add(heading);
        actions.Children.Add(UserButton("About",()=>new AboutWindow().Show(this)));
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 18, 12) };
        toolbar.Children.Add(UserButton("Discover",OpenDiscovery)); toolbar.Children.Add(UserButton("↶ Undo", vm.Undo)); toolbar.Children.Add(UserButton("↷ Redo", vm.Redo)); toolbar.Children.Add(UserButton("Connect", () => { vm.ConnectMode = !vm.ConnectMode; vm.ConnectFrom = null; vm.Status = vm.ConnectMode ? "Connect: click two devices" : "Connection tool off"; vm.Notify(); })); toolbar.Children.Add(UserButton("Auto layout", () => vm.Edit(() => { MapLayout.Arrange(vm.Project); canvas.Fit(); }))); toolbar.Children.Add(ArrangeMenu()); toolbar.Children.Add(ViewMenu()); toolbar.Children.Add(UserButton("Fit", canvas.Fit));
        var snap = new CheckBox { Content = "Snap", IsChecked = true, Margin = new Thickness(8, 0) }; snap.IsCheckedChanged += (_, _) => canvas.Snap = snap.IsChecked == true; toolbar.Children.Add(snap);
        var guides=new CheckBox{Content="Guides",IsChecked=true,Margin=new Thickness(8,0)};guides.IsCheckedChanged+=(_,_)=>canvas.ShowGuides=guides.IsChecked==true;toolbar.Children.Add(guides);
        toolbar.Children.Add(UserButton("Text export", () => { new TextExportWindow(vm, platform).Show(this); })); toolbar.Children.Add(UserButton("SVG", () => ExportVisual("svg"))); toolbar.Children.Add(UserButton("PNG", () => ExportVisual("png"))); toolbar.Children.Add(UserButton("HTML", () => ExportVisual("html"))); toolbar.Children.Add(UserButton("Validate", ShowValidation)); foreach(var tool in toolbar.Children)tool.Margin=new Thickness(0,0,6,6); Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        var workspace = new Grid { ColumnDefinitions = new("265,5,*,5,320") };
        workspace.ColumnDefinitions[0].MinWidth=180;workspace.ColumnDefinitions[2].MinWidth=360;workspace.ColumnDefinitions[4].MinWidth=220;
        foreach(int col in new[]{1,3}){var splitter=new GridSplitter{ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext,HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brushes.SlateGray};Grid.SetColumn(splitter,col);workspace.Children.Add(splitter);}
        var left = new StackPanel { Margin = new Thickness(16), Spacing = 8 }; left.Children.Add(Label("DEVICE LIBRARY")); left.Children.Add(new TextBlock { Text = "Drag onto the canvas, or click to add", FontSize = 10, Classes={"muted"} });
        var palette = new Grid { ColumnDefinitions = new("*,*") }; int i = 0;

        foreach (var kind in Enum.GetValues<NodeKind>().Where(k => !new Node{Kind=k}.IsContainer)) { if(i%2==0)palette.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); var k = kind; var b = UserButton(DeviceClassifier.Label(kind), () => { AddItem(k); }); b.FontSize = 11; b.Margin = new Thickness(2); b.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumn(b, i % 2); Grid.SetRow(b, i / 2); palette.Children.Add(b); i++; EnablePaletteDrag(b, k); }
        left.Children.Add(palette);
        left.Children.Add(UserButton("+ Switch stack (5 members)",()=>AddItem(NodeKind.Switch,true)));
        left.Children.Add(UserButton("Open icons folder",()=>{try{platform.OpenFolder(iconLibrary.FolderPath);}catch(Exception ex){vm.Status=ex.Message;vm.Notify();}}));
        left.Children.Add(UserButton("Reload icons",async()=>{await ReloadIcons();if(lifetime.IsCancellationRequested)return;await Message("Device icons",iconLibrary.Warnings.Count==0?$"Loaded {iconLibrary.Images.Count} user icons. Built-in logos are also available.":string.Join("\n",iconLibrary.Warnings));}));
        left.Children.Add(UserButton("Discover network",OpenDiscovery));
        left.Children.Add(Label("CONTAINERS")); var kinds = new ComboBox { ItemsSource = Enum.GetValues<NodeKind>().Where(k => new Node{Kind=k}.IsContainer).ToList(), SelectedIndex = 3, HorizontalAlignment = HorizontalAlignment.Stretch }; left.Children.Add(kinds); left.Children.Add(UserButton("+ Add container", () => AddItem((NodeKind)kinds.SelectedItem!)));
        left.Children.Add(new Separator { Margin = new Thickness(0, 9) }); left.Children.Add(Label("NETWORK EXPLORER")); left.Children.Add(search);
        typeFilter.SelectionChanged+=(_,_)=>{vm.TypeFilter=typeFilter.SelectedIndex<=0?null:DeviceClassifier.Types[typeFilter.SelectedIndex-1];vm.Notify();};
        confidenceFilter.SelectionChanged+=(_,_)=>{vm.ConfidenceFilter=confidenceFilter.SelectedIndex<=0?null:(ClassificationConfidence)(confidenceFilter.SelectedIndex-1);vm.Notify();};
        left.Children.Add(typeFilter);left.Children.Add(confidenceFilter);
        var legend=new StackPanel{Spacing=4};
        foreach(var kind in DeviceClassifier.Types){var item=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7};item.Children.Add(new DeviceLegendIcon(kind));item.Children.Add(new TextBlock{Text=DeviceClassifier.Label(kind),FontSize=11});legend.Children.Add(item);}
        legend.Children.Add(new TextBlock{Text="High: corroborated strong evidence\nMedium: supported identification\nLow: insufficient or conflicting evidence\nManual: your correction is retained",TextWrapping=TextWrapping.Wrap,FontSize=10});
        left.Children.Add(new Expander{Header="Device and confidence legend",Content=legend,HorizontalAlignment=HorizontalAlignment.Stretch});
        left.Children.Add(summary); left.Children.Add(inventory); search.TextChanged += (_, _) => RefreshInventory();
        search.KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&SearchNodes().FirstOrDefault() is {} match){FocusItem(match);e.Handled=true;}};
        workspace.Children.Add(new Border { Classes={"panel"}, Child = new ScrollViewer { Content = left }, BorderThickness = new Thickness(0, 1, 1, 0) });
        var center = new Grid { RowDefinitions = new("Auto,*") }; var canvasHead = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(16, 12) }; var projectName = new TextBlock { FontWeight = FontWeight.SemiBold }; vm.Changed += () => projectName.Text = vm.Project.Name; projectName.Text = vm.Project.Name; canvasHead.Children.Add(projectName); var hint = new TextBlock { Text = "SCROLL  zoom    /    MIDDLE DRAG  pan", FontSize = 10, Classes={"muted"} }; Grid.SetColumn(hint, 1); canvasHead.Children.Add(hint); center.Children.Add(canvasHead); Grid.SetRow(canvas, 1); center.Children.Add(canvas); Grid.SetColumn(center, 2); workspace.Children.Add(center);
        var right = new Border { Classes={"panel"}, Child = new ScrollViewer { Content = properties }, BorderThickness = new Thickness(1, 1, 0, 0) }; Grid.SetColumn(right, 4); workspace.Children.Add(right); Grid.SetRow(workspace, 2); root.Children.Add(workspace);
        var footer = new Border { Classes={"panel"}, Child = status, BorderThickness = new Thickness(0, 1, 0, 0) }; Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        vm.Changed += ()=>{if(iconRevision!=vm.DocumentRevision){iconRevision=vm.DocumentRevision;iconLibrary.ApplyTo(vm.Project);}};vm.Changed += Refresh;iconLibrary.ApplyTo(vm.Project);Refresh(); KeyDown += Shortcut;
        iconRefresh.Tick+=async(_,_)=>{
            if(checkingIcons||lifetime.IsCancellationRequested)return;checkingIcons=true;
            try
            {
                var folder=iconLibrary.FolderPath;
                var fingerprint=await Task.Run(()=>Directory.Exists(folder)?string.Join(";",Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal).Select(p=>{lifetime.Token.ThrowIfCancellationRequested();return p+File.GetLastWriteTimeUtc(p).Ticks+new FileInfo(p).Length;})):"");
                if(fingerprint!=iconFingerprint){iconFingerprint=fingerprint;await ReloadIcons();}
            }
            catch(OperationCanceledException){}catch(IOException){}catch(UnauthorizedAccessException){}
            finally{checkingIcons=false;}
        };iconRefresh.Start();
        Closed+=(_,_)=>{iconRefresh.Stop();autosave.Stop();lifetime.Cancel();canvas.Dispose();DeviceIconRenderer.ClearCaches();};
        autosave.Tick += async (_, _) => {
            if(!vm.Dirty||savingRecovery||canvas.HasActiveGesture)return;
            savingRecovery=true;
            try{var copy=ProjectSnapshot.Capture(vm.Project);var path=platform.RecoveryPath;await Task.Run(()=>ProjectStorage.SaveAsync(copy,path,lifetime.Token));}
            catch(OperationCanceledException){}
            catch(Exception ex){vm.Status="Autosave failed: "+ex.Message;Refresh();}
            finally{savingRecovery=false;}
        };autosave.Start();
        Opened += async (_, _) => {
            await ReloadIcons();if(lifetime.IsCancellationRequested)return;canvas.Fit();
            if(Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)return;
            if(profiling)return;
            bool recovered=false;
            if(File.Exists(platform.RecoveryPath)&&await Confirm("Recovery available","Restore the last autosaved network?")){
                try{vm.Replace(await Task.Run(()=>ProjectStorage.LoadAsync(platform.RecoveryPath)));vm.Touch();canvas.Fit();recovered=true;}catch(Exception ex){await Message("Recovery failed",ex.Message);}
            }
            if(!recovered&&StartupPreferences.Load().AskToLoadSample)await AskForSample();
        };
        Closing += async (_, e) => { if (closingApproved) return; e.Cancel = true; if (await CanDiscard()) { closingApproved = true; autosave.Stop(); Close(); } };
    }
    public static TextBlock Label(string text) => new() { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.4, Classes={"section"}, Margin = new Thickness(0, 8, 0, 4) };
    public static Button Button(string text, Action action) { var b = new Button { Content = text, Padding = new Thickness(10, 7) }; b.Click += (_, _) => action(); return b; }
    public static Button Button(string text, Func<Task> action) { var b = new Button { Content = text, Padding = new Thickness(10, 7) }; b.Click += async (_, _) => { await action(); }; return b; }
    private Button UserButton(string text, Action action) { var button=Button(text,()=>vm.RunUserAction(action));return button; }
    private Button UserButton(string text, Func<Task> action) { var button=Button(text,async()=>{try{await action();}catch(InvalidDataException ex){vm.Status=ex.Message;vm.Notify();}});if(text is "Save" or "Discover" or "Discover network")button.Classes.Add("primary");return button; }
    private void EnablePaletteDrag(Button button, NodeKind kind)
    {
        Point origin = default; bool pressed = false, moved = false;
        button.AddHandler(PointerPressedEvent, (_, e) => { origin = e.GetPosition(this); pressed = true; moved = false; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.AddHandler(PointerMovedEvent, (_, e) => { var p = e.GetPosition(this); if (pressed && Math.Abs(p.X - origin.X) + Math.Abs(p.Y - origin.Y) > 8) moved = true; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.AddHandler(PointerReleasedEvent, (_, e) => { if (pressed && moved) { var pos = e.GetPosition(canvas); if (new Rect(canvas.Bounds.Size).Contains(pos)) { var world = canvas.World(pos); vm.RunUserAction(()=>{typeFilter.SelectedIndex=0;confidenceFilter.SelectedIndex=0;vm.Add(kind, world.X, world.Y);}); e.Handled = true; } } pressed = false; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    private void Refresh() { status.Text = vm.Status + "  •  " + vm.Project.Nodes.Count + " nodes / " + vm.Project.Connections.Count + " links"; Title = (vm.Dirty ? "● " : "") + vm.Project.Name + " / RackTrail"; RefreshInventory(); var key = (vm.Selected?.Id ?? vm.SelectedConnection?.Id ?? "none") + ":" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(vm.Project)+":"+string.Join(",",vm.Selection.Order())+":"+vm.Selected?.Kind+":"+vm.Selected?.StackMembers.Count+":"+vm.PropertiesRevision; if (key != propertyKey) { propertyKey = key; RefreshProperties(); } }
    private void RefreshInventory()
    {
        var query=search.Text??"";
        if(ReferenceEquals(inventoryProject,vm.Project)&&inventoryRevision==vm.DocumentRevision&&inventoryQuery==query&&inventoryType==vm.TypeFilter&&inventoryConfidence==vm.ConfidenceFilter)return;
        inventoryProject=vm.Project;inventoryRevision=vm.DocumentRevision;inventoryQuery=query;inventoryType=vm.TypeFilter;inventoryConfidence=vm.ConfidenceFilter;
        updatingInventory=true;
        try
        {
            var existing=inventoryRows.ToDictionary(r=>r.Node.Id);
            var rows=SearchNodes().Select(n=>{var label=n.Name+(n.IsStack?$" ({n.StackMembers.Count})":"")+(n.IsContainer?"":$" | {n.Classification?.Confidence??ClassificationConfidence.Low}");return existing.TryGetValue(n.Id,out var row)&&ReferenceEquals(row.Node,n)&&row.Label==label&&row.Kind==n.Kind?row:new InventoryRow(n,label,n.Kind);}).ToArray();
            if(!rows.SequenceEqual(inventoryRows)){inventoryRows=rows;inventory.ItemsSource=rows;}
        }
        finally{updatingInventory=false;}
        summary.Text=$"{vm.Project.Nodes.Count(n=>!n.IsContainer)} devices | {vm.Project.Nodes.Count(n=>n.Kind==NodeKind.Subnet)} subnets";
    }

    private long searchIndexRevision=-1;
    private Dictionary<string,string> searchTexts=[];
    private IEnumerable<Node> SearchNodes()
    {
        var query=(search.Text??"").Trim();
        if(query.Length==0)return vm.Project.Nodes.Where(vm.MatchesFilter);
        if(searchIndexRevision!=vm.DocumentRevision)
        {
            var depths=ProjectLimits.ValidateHierarchy(vm.Project);var paths=new Dictionary<string,string>();var texts=new Dictionary<string,string>();
            foreach(var n in vm.Project.Nodes.OrderBy(n=>depths[n.Id]))
            {
                string parent=n.ParentId is {} id?paths[id]:"";paths[n.Id]=parent+" "+n.Name;
                texts[n.Id]=n.Name+" "+n.Addresses+" "+n.Cidr+" "+n.Role+" VLAN "+n.VlanId+" "+parent+" "+string.Join(" ",n.StackMembers.Select(m=>$"{m.Name} {m.Model} {m.SerialNumber}"));
            }
            searchTexts=texts;searchIndexRevision=vm.DocumentRevision;
        }
        return vm.Project.Nodes.Where(n=>vm.MatchesFilter(n)&&searchTexts.GetValueOrDefault(n.Id,"").Contains(query,StringComparison.OrdinalIgnoreCase));
    }
    private void AddItem(NodeKind kind,bool stack=false){typeFilter.SelectedIndex=0;confidenceFilter.SelectedIndex=0;canvas.AddInView(kind,stack);}
    private void FocusItem(Node n){typeFilter.SelectedIndex=0;confidenceFilter.SelectedIndex=0;canvas.FocusNode(n);}
    private Button ActionMenu(string title, IEnumerable<(string Label,Action Action)> actions)
    {
        var panel=new StackPanel{Spacing=3,MinWidth=200};var flyout=new Flyout{Content=new ScrollViewer{Content=panel,MaxHeight=520}};
        foreach(var action in actions){var button=UserButton(action.Label,()=>{flyout.Hide();action.Action();});button.HorizontalAlignment=HorizontalAlignment.Stretch;panel.Children.Add(button);}
        return new Button{Content=title,Flyout=flyout};
    }
    private Button ArrangeMenu() => ActionMenu("Arrange",new (string,Action)[]{
        ("Reroute connections",vm.RerouteConnections),
        ("Bring to front",()=>vm.ReorderSelection(true)),("Send to back",()=>vm.ReorderSelection(false)),
        ("Fit container to contents",vm.FitSelectedContainers),("Lock selected",()=>vm.LockSelection(true)),("Unlock selected",()=>vm.LockSelection(false)),
        ("Align left",()=>vm.AlignSelection(Alignment.Left)),("Align horizontal centers",()=>vm.AlignSelection(Alignment.Center)),("Align right",()=>vm.AlignSelection(Alignment.Right)),
        ("Align top",()=>vm.AlignSelection(Alignment.Top)),("Align vertical centers",()=>vm.AlignSelection(Alignment.Middle)),("Align bottom",()=>vm.AlignSelection(Alignment.Bottom)),
        ("Space evenly horizontally",()=>vm.Distribute(true)),("Space evenly vertically",()=>vm.Distribute(false))});
    private Button ViewMenu() => ActionMenu("View",new (string,Action)[]{
        ("Show all connection labels",()=>{canvas.ShowAllConnectionLabels=true;canvas.InvalidateVisual();}),
        ("Automatic connection labels",()=>{canvas.ShowAllConnectionLabels=false;canvas.InvalidateVisual();}),
        ("Highlight selected device connections",()=>{canvas.FocusConnections=true;canvas.InvalidateVisual();}),
        ("Show connections equally",()=>{canvas.FocusConnections=false;canvas.InvalidateVisual();}),
        ("Compact map",()=>{vm.SetCompact(true,true);canvas.Fit();}),("Expanded map",()=>{vm.SetCompact(false,true);canvas.Fit();}),
        ("Compact selected containers",()=>vm.SetCompact(true)),("Expand selected containers",()=>vm.SetCompact(false)),
        ("Zoom to selected",()=>{if(vm.Selected is {} n)FocusItem(n);}),("Fit entire map",canvas.Fit)});
    private void MultiSelectionProperties()
    {
        properties.Children.Add(new TextBlock{Text=$"{vm.Selection.Count} items selected",FontSize=18});
        properties.Children.Add(ArrangeMenu());properties.Children.Add(ViewMenu());
        int subnets=vm.Project.Nodes.Count(n=>vm.Selection.Contains(n.Id)&&n.Kind==NodeKind.Subnet);
        if(subnets>0){properties.Children.Add(new TextBlock{Text=$"Apply changes to {subnets} selected subnet(s). Other item types are unaffected.",TextWrapping=TextWrapping.Wrap,FontSize=11});properties.Children.Add(BulkSubnetPanel());}
        properties.Children.Add(new TextBlock{Text="Ctrl + click adds or removes an item. Alignment and spacing use items in the same container; select at least three for even spacing.",TextWrapping=TextWrapping.Wrap,FontSize=11});
        properties.Children.Add(UserButton("Delete selected",vm.Delete));
    }
    private Expander BulkSubnetPanel()
    {
        var body=new StackPanel{Spacing=7};
        var name=new TextBox{Name="BatchSubnetName",Watermark="Leave blank to keep existing names"};
        var color=new ComboBox{Name="BatchSubnetColor",ItemsSource=new[]{"Keep existing","Automatic","Blue","Teal","Orange","Purple","Pink","Custom"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
        var custom=new TextBox{Name="BatchCustomColor",Watermark="#RRGGBB",IsVisible=false};color.SelectionChanged+=(_,_)=>custom.IsVisible=color.SelectedIndex==7;
        var width=new TextBox{Name="BatchSubnetWidth",Watermark="Unchanged (minimum 220)"};var height=new TextBox{Name="BatchSubnetHeight",Watermark="Unchanged (minimum 120)"};
        body.Children.Add(new TextBlock{Text="Subnet / VLAN name"});body.Children.Add(name);body.Children.Add(new TextBlock{Text="Outline color"});body.Children.Add(color);body.Children.Add(custom);
        body.Children.Add(new TextBlock{Text="Width"});body.Children.Add(width);body.Children.Add(new TextBlock{Text="Height"});body.Children.Add(height);
        double? Number(TextBox box){if(string.IsNullOrWhiteSpace(box.Text))return null;if(double.TryParse(box.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var result)&&double.IsFinite(result))return result;throw new InvalidDataException("Enter a valid numeric size, or leave it blank to keep the current size.");}
        var apply=UserButton("Apply to selected subnets",()=>{
            string? chosen=color.SelectedIndex switch{0=>null,1=>"",2=>"#7CAAF0",3=>"#69C7BF",4=>"#F29B72",5=>"#B9A0EF",6=>"#EF8CB8",_=>custom.Text?.Trim()??""};
            vm.EditSubnets(string.IsNullOrWhiteSpace(name.Text)?null:name.Text.Trim(),chosen,Number(width),Number(height));
        });apply.Name="ApplyBatchSubnets";body.Children.Add(apply);
        body.Children.Add(new TextBlock{Text="Leave fields blank to keep their values. Containers stay large enough for their contents.",FontSize=11,TextWrapping=TextWrapping.Wrap});
        return new Expander{Header="Edit subnet appearance",IsExpanded=vm.Selection.Count>1,Content=body,HorizontalAlignment=HorizontalAlignment.Stretch};
    }
    private void Field(string label, string value, Action<string> assign, bool multiline = false, StackPanel? panel = null)
    {
        var target = panel ?? properties;
        target.Children.Add(new TextBlock { Text = label, FontSize = 11, Classes={"muted"} }); var box = new TextBox { Text = value, AcceptsReturn = multiline, MinHeight = multiline ? 75 : 32 }; string previous = value;
        void Commit() { var text = box.Text ?? ""; if (text != previous) { previous = text; vm.Edit(() => assign(text)); } }
        propertyCommits.Add(Commit);box.LostFocus += (_, _) => Commit(); target.Children.Add(box);
    }
    private void RefreshProperties()
    {
        propertyCommits.Clear(); properties.Children.Clear(); properties.Children.Add(Label("PROPERTIES"));
        if(vm.Selection.Count>1){MultiSelectionProperties();return;}
        if (vm.SelectedConnection is { } c) { properties.Children.Add(new TextBlock { Text = "Network connection", FontSize = 17 }); ConnectionEndpoint(c,true);ConnectionEndpoint(c,false);Field("Additional port label", c.Ports, v => c.Ports = v); Field("Protocol", c.Protocol, v => c.Protocol = v); Field("Bandwidth", c.Bandwidth, v => c.Bandwidth = v); Field("Purpose", c.Purpose, v => c.Purpose = v); properties.Children.Add(UserButton("Reset automatic route", () => vm.Edit(() => c.Route = []))); properties.Children.Add(UserButton("Delete connection", vm.Delete)); return; }
        if (vm.Selected is not { } n) { properties.Children.Add(new TextBlock { Text = "A place for every connection.", FontSize = 19, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12) }); properties.Children.Add(new TextBlock { Text = "Select a device, container, or connection to edit its properties.\n\nCtrl + click for multiple selection. Drag empty canvas to select a region. Double-click a container to collapse it.", TextWrapping = TextWrapping.Wrap, FontSize = 12 }); Field("Project name", vm.Project.Name, v => vm.Project.Name = v); properties.Children.Add(UserButton("Load sample network", async () => { if (await CanDiscard()) { vm.Replace(SampleNetwork.Create()); canvas.Fit(); } })); return; }
        properties.Children.Add(UserButton("Bring to front",()=>vm.ReorderSelection(true)));
        properties.Children.Add(UserButton("Send to back",()=>vm.ReorderSelection(false)));
        properties.Children.Add(UserButton(n.Locked?"Unlock item":"Lock item",()=>vm.LockSelection(!n.Locked)));
        if(MapEditing.IsLocked(vm.Project,n))properties.Children.Add(new TextBlock{Text="Position and size are locked. A locked container also protects its contents.",TextWrapping=TextWrapping.Wrap,FontSize=11});
        if(n.CanContainNodes)properties.Children.Add(UserButton("Fit container to contents",vm.FitSelectedContainers));
        if(n.Kind==NodeKind.Subnet)properties.Children.Add(BulkSubnetPanel());
        properties.Children.Add(new TextBlock { Text = n.DisplayType, FontSize = 18, FontWeight = FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap }); Field("Name", n.Name, v => n.Name = v);
        if(n.Kind==NodeKind.Phones)properties.Children.Add(new TextBlock{Text="Represents the phones on a subnet. Assign container membership below; individual handset IP addresses are optional.",FontSize=11,TextWrapping=TextWrapping.Wrap,Classes={"muted"}});
        var type = new ComboBox { ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<NodeKind>((kind,_)=>new TextBlock{Text=DeviceClassifier.Label(kind)}), ItemsSource = Enum.GetValues<NodeKind>().Where(k => new Node{Kind=k}.IsContainer == n.IsContainer).ToList(), SelectedItem = n.Kind, IsEnabled=!n.IsStack&&n.HostId==null&&!vm.Project.Nodes.Any(x=>x.ParentId==n.Id||x.HostId==n.Id),HorizontalAlignment = HorizontalAlignment.Stretch }; type.SelectionChanged += (_, _) => { if (type.SelectedItem is NodeKind k && k != n.Kind) {vm.Edit(() => {if(!n.IsContainer)DeviceClassifier.SetManual(n,k);else n.Kind = k;if(n.CanContainNodes)Containment.Resize(vm.Project,n,Math.Max(460,n.Width),Math.Max(240,n.Height));});vm.RefreshDetails();} }; properties.Children.Add(type);
        if(!n.IsContainer){
            var classification=n.Classification;
            properties.Children.Add(new TextBlock{Text=classification?.Summary??$"{DeviceClassifier.Label(n.Kind)} · Low · Existing inventory",FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap});
            properties.Children.Add(new TextBlock{Text=classification?.Explanation??"No automatic assessment. Existing device type is retained until you choose otherwise.",TextWrapping=TextWrapping.Wrap,FontSize=11});
            if(classification!=null){
                properties.Children.Add(new TextBlock{Text="Discovered: "+string.Join(" · ",new[]{classification.Vendor,classification.Model,classification.OperatingSystem,classification.MacAddress}.Where(v=>v.Length>0)),TextWrapping=TextWrapping.Wrap,FontSize=11});
                properties.Children.Add(new TextBlock{Text="Roles: "+(classification.Roles.Count>0?string.Join(", ",classification.Roles):"Unknown"),TextWrapping=TextWrapping.Wrap,FontSize=11});
                if(!classification.ManualType.HasValue&&!n.IsStack&&n.HostId==null&&!vm.Project.Nodes.Any(x=>x.ParentId==n.Id||x.HostId==n.Id))
                    properties.Children.Add(UserButton("Keep current type (manual)",()=>{vm.Edit(()=>DeviceClassifier.SetManual(n,n.Kind));vm.RefreshDetails();}));
                var evidence=new StackPanel{Spacing=4};
                foreach(var item in classification.Evidence)evidence.Children.Add(new TextBlock{Text=$"{item.Source}: {item.Detail}",TextWrapping=TextWrapping.Wrap,FontSize=11});
                foreach(var limitation in classification.Limitations.Distinct())evidence.Children.Add(new TextBlock{Text="Limited: "+limitation,TextWrapping=TextWrapping.Wrap,FontSize=11});
                foreach(var neighbor in classification.Neighbors)evidence.Children.Add(new TextBlock{Text=$"{neighbor.Protocol} neighbor: {neighbor.Name} {neighbor.Address} {neighbor.Description}",TextWrapping=TextWrapping.Wrap,FontSize=11});
                properties.Children.Add(new Expander{Header=$"Discovery evidence ({classification.ObservedAt.LocalDateTime:g})",Content=evidence,HorizontalAlignment=HorizontalAlignment.Stretch});
                if(classification.ManualType.HasValue&&!n.IsStack&&n.HostId==null&&!vm.Project.Nodes.Any(x=>x.ParentId==n.Id||x.HostId==n.Id))
                    properties.Children.Add(UserButton("Restore automatic classification",()=>{vm.Edit(()=>DeviceClassifier.RestoreAutomatic(n));vm.RefreshDetails();}));
            }
            Field("Vendor",n.Vendor,v=>n.Vendor=v);Field("Model",n.Model,v=>{n.Model=v;var probe=new Node{Kind=n.Kind,Vendor=n.Vendor,Model=v,UseDeviceImage=true};var key=DeviceIcons.ResolveKey(probe,iconLibrary.Images.Keys);if(!n.CanContainNodes&&iconLibrary.Images.ContainsKey(key)&&!DeviceIcons.BuiltInKeys.Contains(key,StringComparer.OrdinalIgnoreCase)){n.UseDeviceImage=true;n.IconKey="auto";Containment.Resize(vm.Project,n,Math.Max(180,n.Width),Math.Max(48,n.ImageSize+8));}});IconProperties(n);
            if(!n.CanContainNodes){var shape=new ComboBox{ItemsSource=Enum.GetValues<DeviceShape>(),SelectedItem=n.Shape,HorizontalAlignment=HorizontalAlignment.Stretch};shape.SelectionChanged+=(_,_)=>{if(shape.SelectedItem is DeviceShape value&&value!=n.Shape){vm.Edit(()=>{n.Shape=value;n.UseDeviceImage=false;Containment.Resize(vm.Project,n,n.Width,n.Height);});vm.RefreshDetails();}};properties.Children.Add(shape);
                properties.Children.Add(UserButton(n.UseDeviceImage?"Use standard shape":"Use device image",()=>vm.SetDeviceImage(n,!n.UseDeviceImage)));
                if(n.UseDeviceImage)ImageProperties(n);
                properties.Children.Add(new TextBlock{Text="Model images replace the shape and keep device information beside the picture. Drag a corner or use Image size to scale down. Add PNG/JPG files to the icons folder; model names select matching images automatically.",FontSize=11,TextWrapping=TextWrapping.Wrap,Classes={"muted"}});}}
        if(n.Kind==NodeKind.Hypervisor){Field("Hypervisor platform",n.HypervisorPlatform,v=>n.HypervisorPlatform=v);properties.Children.Add(UserButton("+ Add virtual machine",()=>vm.AddVirtualMachine(n)));properties.Children.Add(UserButton(n.Collapsed?"Expand host":"Collapse host",()=>{vm.Edit(()=>n.Collapsed=!n.Collapsed);vm.RefreshDetails();}));}
        if(n.Kind==NodeKind.VirtualMachine)
        {
            var hostChoice=new ComboBox{Name="HostMembership",HorizontalAlignment=HorizontalAlignment.Stretch};
            ContainerPicker.Populate(hostChoice,vm.Project,vm.Project.Nodes.Where(x=>x.Kind==NodeKind.Hypervisor),"(No host)",n.HostId);
            hostChoice.SelectionChanged+=(_,_)=>{if(hostChoice.SelectedItem is ContainerChoice choice&&choice.Id!=n.HostId)vm.RunUserAction(()=>vm.AssignHost(n,choice.Id));};
            properties.Children.Add(Label("HYPERVISOR HOST"));properties.Children.Add(hostChoice);
        }
        var parents=vm.Project.Nodes.Where(x=>x.CanContainNodes&&x.Id!=n.Id&&!vm.Project.Ancestors(x).Any(a=>a.Id==n.Id));
        var combo=new ComboBox{Name="ContainerMembership",HorizontalAlignment=HorizontalAlignment.Stretch};
        ContainerPicker.Populate(combo,vm.Project,parents,"(No container)",n.ParentId);
        properties.Children.Add(new TextBlock{Text="Container membership",FontSize=11,Classes={"muted"}});
        combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is ContainerChoice choice&&choice.Id!=n.ParentId)vm.RunUserAction(()=>vm.AssignContainer(n,choice.Id));};
        properties.Children.Add(combo);

        if (n.IsContainer) { Field("CIDR", n.Cidr, v => n.Cidr = v); Field("Gateway", n.Gateway, v => n.Gateway = v); Field("VLAN ID", n.VlanId, v => n.VlanId = v); properties.Children.Add(UserButton(n.Collapsed ? "Expand container" : "Collapse container", () => { vm.Edit(() => n.Collapsed = !n.Collapsed); propertyKey = ""; Refresh(); })); }
        else { Field("Hostname", n.Hostname, v => n.Hostname = v); Field(n.IsStack?"Shared management IP addresses":"IP addresses (comma separated)", n.Addresses, v => n.Addresses = v); Field("Operating system", n.OperatingSystem, v => n.OperatingSystem = v); Field("Role", n.Role, v => n.Role = v); }
        if(n.Kind==NodeKind.Switch)
        {
            Field("Management interface (SSH / HTTPS address)",n.ManagementInterface,v=>n.ManagementInterface=v);
            if(n.IsStack)StackProperties(n);
            else properties.Children.Add(UserButton("Convert to 5-member stack",()=>vm.Edit(()=>n.StackMembers=Enumerable.Range(1,5).Select(number=>new StackMember{Number=number,Name=$"Switch {number}"}).ToList())));
        }
        Field(n.IsContainer ? "Description" : "Notes", n.Notes, v => n.Notes = v, true); var exceptions = new CheckBox { Content = "Allow validation exceptions", IsChecked = n.AllowValidationExceptions }; exceptions.IsCheckedChanged += (_, _) => { if (n.AllowValidationExceptions != (exceptions.IsChecked == true)) vm.Edit(() => n.AllowValidationExceptions = exceptions.IsChecked == true); }; properties.Children.Add(exceptions); properties.Children.Add(UserButton("Delete selected", vm.Delete));
        { Field("Width", n.Width.ToString(System.Globalization.CultureInfo.InvariantCulture), v => { if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) Containment.Resize(vm.Project,n,Math.Clamp(number,n.UseDeviceImage&&!n.ShowDeviceInfo?32:100,10000),n.Height); }); Field("Height", n.Height.ToString(System.Globalization.CultureInfo.InvariantCulture), v => { if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) Containment.Resize(vm.Project,n,n.Width,Math.Clamp(number,n.UseDeviceImage?32:60,10000)); }); }
    }
    private async Task AskForSample()
    {
        var dialog=new Window{Title="Start your map",Icon=Branding.CreateIcon(),Width=450,Height=240,MinWidth=360,MinHeight=220,CanResize=true,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(24),Spacing=16};panel.Children.Add(new TextBlock{Text="Would you like to load the sample network, or start with a blank map?",TextWrapping=TextWrapping.Wrap});
        var skip=new CheckBox{Content="Don't ask again"};panel.Children.Add(skip);var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};buttons.Children.Add(UserButton("Start blank",()=>dialog.Close(false)));buttons.Children.Add(UserButton("Load sample",()=>dialog.Close(true)));panel.Children.Add(buttons);dialog.Content=panel;
        bool load=await dialog.ShowDialog<bool>(this);
        if(skip.IsChecked==true){try{new StartupPreferences{AskToLoadSample=false}.Save();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){await Message("Startup preference", "Could not save your preference: "+ex.Message);}}
        if(load){vm.Replace(SampleNetwork.Create());canvas.Fit();}
    }
    private async Task ReloadIcons()
    {
        if(reloadIconsTask!=null){await reloadIconsTask;return;}
        reloadIconsTask=ReloadIconsCore();try{await reloadIconsTask;}finally{reloadIconsTask=null;}
    }
    private async Task ReloadIconsCore()
    {
        try
        {
            var folder=iconLibrary.FolderPath;var loaded=await Task.Run(()=>new IconLibrary(folder,cancellation:lifetime.Token));
            if(lifetime.IsCancellationRequested)return;
            iconLibrary.Adopt(loaded);if(iconLibrary.ApplyTo(vm.Project))vm.Touch(false);propertyKey="";vm.Notify();
        }
        catch(OperationCanceledException){}
    }
    private void ImageProperties(Node node)
    {
        var label=new TextBlock{Text=$"Image size: {node.ImageSize:0} px",FontSize=11};properties.Children.Add(label);
        var size=new Slider{Name="DeviceImageSize",Minimum=16,Maximum=256,Value=node.ImageSize,TickFrequency=4,IsSnapToTickEnabled=true,IsEnabled=!MapEditing.IsLocked(vm.Project,node)};
        bool dragging=false;
        size.AddHandler(PointerPressedEvent,(_,_)=>{if(!MapEditing.IsLocked(vm.Project,node)){vm.Checkpoint();dragging=true;}},Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
        size.ValueChanged+=(_,_)=>{if(Math.Abs(size.Value-node.ImageSize)>.01){vm.ResizeDeviceImage(node,Math.Round(size.Value),!dragging);label.Text=$"Image size: {node.ImageSize:0} px";}};
        size.AddHandler(PointerReleasedEvent,(_,_)=>{if(dragging){dragging=false;vm.RefreshDetails();}},Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
        size.PointerCaptureLost+=(_,_)=>{if(dragging){dragging=false;vm.RefreshDetails();}};
        properties.Children.Add(size);
        var info=new CheckBox{Name="DeviceImageInfo",Content="Show device information beside image",IsChecked=node.ShowDeviceInfo,IsEnabled=!MapEditing.IsLocked(vm.Project,node)};
        info.IsCheckedChanged+=(_,_)=>{if(node.ShowDeviceInfo!=(info.IsChecked==true)){vm.Edit(()=>{node.ShowDeviceInfo=info.IsChecked==true;Containment.Resize(vm.Project,node,node.ShowDeviceInfo?node.ImageSize+164:node.ImageSize+8,Math.Max(node.ShowDeviceInfo?48:32,node.ImageSize+8));});vm.RefreshDetails();}};
        properties.Children.Add(info);
    }
    private void IconProperties(Node node)
    {
        properties.Children.Add(new TextBlock{Text="Device icon",FontSize=11,Classes={"muted"}});
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
        properties.Children.Add(new TextBlock{Text="One shared management interface. Physical members have their own identity and connection ports.",FontSize=11,TextWrapping=TextWrapping.Wrap,Classes={"muted"}});
        Field("Stack technology",node.StackTechnology,v=>node.StackTechnology=v);
        var show=new CheckBox{Content="Show members on canvas",IsChecked=node.ShowStackMembers};show.IsCheckedChanged+=(_,_)=>{if(node.ShowStackMembers!=(show.IsChecked==true))vm.Edit(()=>node.ShowStackMembers=show.IsChecked==true);};properties.Children.Add(show);
        properties.Children.Add(UserButton("+ Add member",()=>vm.AddStackMember(node)));
        foreach(var member in node.StackMembers.OrderBy(m=>m.Number))
        {
            var body=new StackPanel{Spacing=7,Margin=new Thickness(4)};var expander=new Expander{Header=$"Member {member.Number} / {member.Name}",Content=body,HorizontalAlignment=HorizontalAlignment.Stretch};
            Field("Member number",member.Number.ToString(),v=>{if(int.TryParse(v,out var number))member.Number=number;expander.Header=$"Member {member.Number} / {member.Name}";},panel:body);
            Field("Name",member.Name,v=>{member.Name=v;expander.Header=$"Member {member.Number} / {member.Name}";},panel:body);
            Field("Model",member.Model,v=>member.Model=v,panel:body);Field("Serial number",member.SerialNumber,v=>member.SerialNumber=v,panel:body);
            Field("Role (active / standby / member)",member.Role,v=>member.Role=v,panel:body);Field("Status (documented)",member.Status,v=>member.Status=v,panel:body);Field("Notes",member.Notes,v=>member.Notes=v,multiline:true,panel:body);
            int links=vm.Project.Connections.Count(c=>c.From==node.Id&&c.FromStackMemberId==member.Id||c.To==node.Id&&c.ToStackMemberId==member.Id);
            var remove=UserButton("Remove member",()=>vm.RemoveStackMember(node,member));remove.IsEnabled=links==0;body.Children.Add(remove);
            if(links>0)body.Children.Add(new TextBlock{Text=$"{links} connection(s) use this member. Reassign them before removal.",TextWrapping=TextWrapping.Wrap,FontSize=10});
            properties.Children.Add(expander);
        }
    }
    private async Task OpenDiscovery(){foreach(var commit in propertyCommits.ToArray())commit();await new ScanWindow(vm).ShowDialog(this);canvas.Fit();}
    private async Task Open() { if (!await CanDiscard()) return; var path = await platform.OpenProject(); if (path == null) return; try { vm.Replace(await Task.Run(()=>ProjectStorage.LoadAsync(path)), path); canvas.Fit(); } catch (Exception ex) { await Message("Cannot open project", ex.Message); } }
    private async Task Save(bool saveAs)
    {
        foreach(var commit in propertyCommits.ToArray())commit();
        var path=saveAs||vm.FilePath==null?await platform.SavePath("Save network project","json"):vm.FilePath;if(path==null)return;
        var source=vm.Project;var revision=vm.DocumentRevision;var copy=ProjectSnapshot.Capture(source);
        try
        {
            await Task.Run(()=>ProjectStorage.SaveAsync(copy,path,lifetime.Token));
            if(!ReferenceEquals(source,vm.Project))return;
            vm.FilePath=path;if(vm.DocumentRevision==revision)vm.Saved();
            if(vm.DocumentRevision==revision&&File.Exists(platform.RecoveryPath))File.Delete(platform.RecoveryPath);
            vm.Status="Saved "+Path.GetFileName(path)+(vm.DocumentRevision==revision?"":" (newer edits remain unsaved)");vm.Notify();
        }
        catch(OperationCanceledException){}
        catch(Exception ex){await Message("Save failed",ex.Message);}
    }
    private async Task ExportVisual(string extension)
    {
        if(exporting){vm.Status="An export is already running.";vm.Notify();return;}
        var path=await platform.SavePath("Export "+extension.ToUpperInvariant(),extension);if(path==null)return;
        var copy=ProjectSnapshot.Capture(vm.Project);bool labels=canvas.ShowAllConnectionLabels;exporting=true;
        try
        {
            await Task.Run(async()=>{
                lifetime.Token.ThrowIfCancellationRequested();
                if(extension=="svg")await File.WriteAllTextAsync(path,VisualExporter.Svg(copy,labels,lifetime.Token),lifetime.Token);
                else if(extension=="html")await File.WriteAllTextAsync(path,HtmlExporter.Create(copy,labels,lifetime.Token),lifetime.Token);
                else PngExporter.Save(copy,path,labels,lifetime.Token);
            });
            if(!lifetime.IsCancellationRequested){vm.Status="Exported "+Path.GetFileName(path);vm.Notify();}
        }
        catch(OperationCanceledException){}
        catch(Exception ex){await Message("Export failed",ex.Message);}
        finally{exporting=false;}
    }

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
        if(e.KeyModifiers.HasFlag(KeyModifiers.Control)&&e.Key==Key.F){search.Focus();search.SelectAll();e.Handled=true;return;}
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { switch (e.Key) { case Key.S: Focus(); await Save(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); break; case Key.O: await Open(); break; case Key.Z: if(FocusManager?.GetFocusedElement() is TextBox)return;vm.Undo(); break; case Key.Y: if(FocusManager?.GetFocusedElement() is TextBox)return;vm.Redo(); break; case Key.C: if (FocusManager?.GetFocusedElement() is TextBox) return; vm.Copy(); break; case Key.V: if (FocusManager?.GetFocusedElement() is TextBox) return; vm.RunUserAction(vm.Paste); break; case Key.A: if (FocusManager?.GetFocusedElement() is TextBox) return; foreach (var n in vm.Project.Nodes.Where(vm.Visible)) vm.Selection.Add(n.Id); vm.Notify(); break; default: return; } e.Handled = true; }
        else if (e.Key == Key.Delete && FocusManager?.GetFocusedElement() is not TextBox) { vm.RunUserAction(vm.Delete); e.Handled = true; } else if (e.Key == Key.Escape) { vm.ConnectMode = false; vm.ConnectFrom = null; vm.Select(null); }
    }
}
