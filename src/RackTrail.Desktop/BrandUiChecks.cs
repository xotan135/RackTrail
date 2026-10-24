using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
namespace RackTrail.Desktop;

public static class BrandUiChecks
{
    public static bool EmbeddedImageGuardVerified { get; private set; }
    public static bool ScanReviewVerified { get; private set; }
    public static bool Completed { get; private set; }
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        EditingUiChecks.Run(directory);
        PresentationUiChecks.Run(directory);
        RoutingUiChecks.Run(directory);
        _ = Geometry.Parse("F0 " + BrandArtwork.Rings);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var logo = new BrandLogo();
            var background = new Border { Background = new SolidColorBrush(Color.Parse(theme == ThemeVariant.Dark ? "#151e2b" : "#f4f7fb")), Child = logo };
            var scope = new ThemeVariantScope { RequestedThemeVariant = theme, Child = background };
            scope.Measure(new Size(300, 72)); scope.Arrange(new Rect(0, 0, 300, 72));
            if (logo.ActualThemeVariant != theme) throw new InvalidOperationException("Brand logo did not inherit the requested theme.");
            using var bitmap = new RenderTargetBitmap(new PixelSize(300, 72), new Vector(96, 96));
            bitmap.Render(scope); bitmap.Save(Path.Combine(directory, theme == ThemeVariant.Dark ? "brand-dark.png" : "brand-light.png"));
        }
        foreach(var theme in new[]{ThemeVariant.Light,ThemeVariant.Dark}){
            var vm=new EditorViewModel();var host=vm.Add(RackTrail.Core.NodeKind.Hypervisor,-300,-180);vm.AddVirtualMachine(host);vm.AddVirtualMachine(host);var ap=vm.Add(RackTrail.Core.NodeKind.AccessPoint,220,-100);ap.UseDeviceImage=true;ap.Height=150;ap.Name="Ubiquiti AP model image";
            var phones=vm.Add(RackTrail.Core.NodeKind.Phones,220,140);phones.Name="Office phones";
            var map=new MapCanvas(vm);var scope=new ThemeVariantScope{RequestedThemeVariant=theme,Child=map};scope.Measure(new Size(900,620));scope.Arrange(new Rect(0,0,900,620));map.Fit();
            using var bitmap=new RenderTargetBitmap(new PixelSize(900,620),new Vector(96,96));bitmap.Render(scope);bitmap.Save(Path.Combine(directory,theme==ThemeVariant.Dark?"features-dark.png":"features-light.png"));
        }
        var scanModel=new EditorViewModel();
        scanModel.Replace(new(){Nodes=[new(){Id="preview-site",Name="Headquarters",Kind=RackTrail.Core.NodeKind.Site,Width=800,Height=500}]});
        var scanWindow=new ScanWindow(scanModel){RequestedThemeVariant=ThemeVariant.Dark,ShowInTaskbar=false};
        scanWindow.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();scanWindow.UpdateLayout();
        T Find<T>(string name) where T:Control=>scanWindow.GetVisualDescendants().OfType<T>().Single(c=>c.Name==name);
        Find<TextBox>("ScanTargets").Text="192.0.2.0/24";
        Find<ComboBox>("SnmpVersion").SelectedIndex=2;Find<TextBox>("SnmpCommunity").Text="preview";
        if(!Find<TextBox>("SnmpCommunity").IsEnabled)throw new InvalidOperationException("SNMP version did not enable community configuration.");
        void CaptureScan(string name,ThemeVariant theme){
            scanWindow.RequestedThemeVariant=theme;Avalonia.Threading.Dispatcher.UIThread.RunJobs();scanWindow.UpdateLayout();
            using var bitmap=new RenderTargetBitmap(new PixelSize(1120,880),new Vector(96,96));bitmap.Render(scanWindow);bitmap.Save(Path.Combine(directory,name));
        }
        CaptureScan("scan-setup-dark.png",ThemeVariant.Dark);CaptureScan("scan-setup-light.png",ThemeVariant.Light);
        var previewHost=new RackTrail.Core.DiscoveredHost("192.0.2.10","ap.example",2){
            Classification=RackTrail.Core.DeviceClassifier.Classify(new(){SystemDescription="wireless access point",Vendor="Example Networks",Model="AP-100",LldpCapabilities=0x10,Limitations=["Preview fixture: no packets sent."]})};
        scanWindow.PresentResults(new([previewHost],1,0,false),1);Avalonia.Threading.Dispatcher.UIThread.RunJobs();scanWindow.UpdateLayout();
        Find<ComboBox>("ReviewDeviceType").SelectedIndex=Array.IndexOf(RackTrail.Core.DiscoveryReview.EditableTypes,RackTrail.Core.NodeKind.Printer)+1;
        Find<ComboBox>("Placement").SelectedIndex=2;
        Find<TextBox>("NewSubnetName").Text="Office network";Find<TextBox>("NewSubnetCidr").Text="192.0.2.0/24";Find<ComboBox>("SubnetParent").SelectedIndex=1;
        CaptureScan("targeted-scan.png",ThemeVariant.Dark);CaptureScan("scan-review-light.png",ThemeVariant.Light);
        Find<Button>("ApplyDiscovery").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var imported=scanModel.Project.Nodes.Single(n=>n.Addresses=="192.0.2.10");
        var importedSubnet=scanModel.Project.Nodes.Single(n=>n.Kind==RackTrail.Core.NodeKind.Subnet);
        if(imported.Kind!=RackTrail.Core.NodeKind.Printer||imported.Classification?.ManualType!=RackTrail.Core.NodeKind.Printer||imported.ParentId!=importedSubnet.Id||importedSubnet.ParentId!="preview-site")throw new InvalidOperationException("Scan review failed to import correction, subnet and parent.");
        scanModel.Undo();if(scanModel.Project.Nodes.Count!=1)throw new InvalidOperationException("Scan import is not a single undo transaction.");
        ScanReviewVerified=true;scanWindow.Close();
        var textVm=new EditorViewModel();textVm.Replace(RackTrail.Core.SampleNetwork.Create());
        var textWindow=new TextExportWindow(textVm,new DesktopPlatformServices(new Window())){RequestedThemeVariant=ThemeVariant.Dark,ShowInTaskbar=false,Width=1500,Height=1300};
        textWindow.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();textWindow.UpdateLayout();
        using(var textBitmap=new RenderTargetBitmap(new PixelSize(1500,1300),new Vector(96,96))){textBitmap.Render(textWindow);textBitmap.Save(Path.Combine(directory,"text-export.png"));}
        textWindow.Close();
        var unsafePng=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZSkAAAAASUVORK5CYII=");
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(unsafePng.AsSpan(16,4),100000);
        var unsafeNode=new RackTrail.Core.Node{IconKey="oversized",EmbeddedIconKey="oversized",IconPngBase64=Convert.ToBase64String(unsafePng)};
        if(RackTrail.Core.DeviceIcons.EmbeddedPng(unsafeNode)!=null)throw new InvalidOperationException("Unsafe image reached renderer.");
        var safeDrawing=new DrawingGroup();using(var drawing=safeDrawing.Open())
            if(DeviceIconRenderer.Draw(drawing,unsafeNode,new Rect(0,0,16,16)))throw new InvalidOperationException("Avalonia accepted oversized embedded image.");
        EmbeddedImageGuardVerified=true;
        var classificationWindow=new MainWindow(){ShowInTaskbar=false};
        var classificationVm=(EditorViewModel)classificationWindow.DataContext!;
        var previewProject=new RackTrail.Core.Project{Name="Discovery classification preview"};
        foreach(var kind in new[]{RackTrail.Core.NodeKind.Server,RackTrail.Core.NodeKind.Switch,RackTrail.Core.NodeKind.Router,RackTrail.Core.NodeKind.Firewall,RackTrail.Core.NodeKind.AccessPoint,RackTrail.Core.NodeKind.Printer,RackTrail.Core.NodeKind.Workstation,RackTrail.Core.NodeKind.Phone,RackTrail.Core.NodeKind.IoT,RackTrail.Core.NodeKind.Device}){
            int index=previewProject.Nodes.Count;
            previewProject.Nodes.Add(new(){Kind=kind,Name=RackTrail.Core.DeviceClassifier.Label(kind),X=40+(index%3)*250,Y=40+(index/3)*130,Width=220,Addresses=$"192.0.2.{index+1}",Classification=new(){AutomaticType=kind,Confidence=kind==RackTrail.Core.NodeKind.Device?RackTrail.Core.ClassificationConfidence.Low:RackTrail.Core.ClassificationConfidence.Medium,Explanation="Preview management identity",Evidence=[new(RackTrail.Core.EvidenceSource.SnmpSystem,"Preview identity")]}});
        }
        classificationVm.Replace(previewProject);classificationVm.Select(previewProject.Nodes[4]);
        classificationWindow.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();classificationWindow.UpdateLayout();
        foreach(var theme in new[]{ThemeVariant.Dark,ThemeVariant.Light}){
            classificationWindow.RequestedThemeVariant=theme;classificationVm.Notify();Avalonia.Threading.Dispatcher.UIThread.RunJobs();classificationWindow.UpdateLayout();
            if(theme==ThemeVariant.Light&&classificationWindow.Background is SolidColorBrush lightBrush&&(lightBrush.Color.R>225||lightBrush.Color.G>225||lightBrush.Color.B>225))throw new InvalidOperationException("Light theme window is too bright.");
            using var bitmap=new RenderTargetBitmap(new PixelSize(1500,940),new Vector(96,96));bitmap.Render(classificationWindow);bitmap.Save(Path.Combine(directory,theme==ThemeVariant.Dark?"discovery-classification.png":"workspace-light.png"));
        }        classificationWindow.Close();
        Completed = true;
    }
}
