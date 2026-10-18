using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
namespace RackTrail.Desktop;

public static class BrandUiChecks
{
    public static bool Completed { get; private set; }
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
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
        var scanWindow=new ScanWindow(new EditorViewModel()){RequestedThemeVariant=ThemeVariant.Dark,ShowInTaskbar=false};
        scanWindow.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();scanWindow.UpdateLayout();
        using(var scanBitmap=new RenderTargetBitmap(new PixelSize(840,760),new Vector(96,96))){scanBitmap.Render(scanWindow);scanBitmap.Save(Path.Combine(directory,"targeted-scan.png"));}
        scanWindow.Close();
        var textVm=new EditorViewModel();textVm.Replace(RackTrail.Core.SampleNetwork.Create());
        var textWindow=new TextExportWindow(textVm,new DesktopPlatformServices(new Window())){RequestedThemeVariant=ThemeVariant.Dark,ShowInTaskbar=false,Width=1500,Height=1300};
        textWindow.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();textWindow.UpdateLayout();
        using(var textBitmap=new RenderTargetBitmap(new PixelSize(1500,1300),new Vector(96,96))){textBitmap.Render(textWindow);textBitmap.Save(Path.Combine(directory,"text-export.png"));}
        textWindow.Close();
        Completed = true;
    }
}
