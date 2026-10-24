using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using RackTrail.Core;
using SkiaSharp;

namespace RackTrail.Desktop;

internal static class PresentationUiChecks
{
    public static void Run(string directory)
    {
        void Check(bool pass,string message){if(!pass)throw new InvalidOperationException(message);Console.WriteLine("PASS "+message);}
        var folder=Path.Combine(directory,"model-pictures");Directory.CreateDirectory(folder);
        using(var bitmap=new SKBitmap(48,128))
        {
            using var c=new SKCanvas(bitmap);c.Clear(SKColors.Transparent);using var paint=new SKPaint{Color=SKColors.White,IsAntialias=true};c.DrawRoundRect(new SKRect(5,2,43,126),14,14,paint);
            using var png=SKImage.FromBitmap(bitmap);using var encoded=png.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(Path.Combine(folder,"nanostation-ac.png"),encoded.ToArray());
        }
        var library=new IconLibrary(folder);var vm=new EditorViewModel();var radio=vm.Add(NodeKind.AccessPoint,60,240);radio.Name="Example radio";radio.Vendor="Ubiquiti";radio.Model="NS-5AC";radio.Addresses="192.0.2.20";vm.SetDeviceImage(radio,true);library.ApplyTo(vm.Project);
        Check(radio.EmbeddedIconKey=="nanostation-ac"&&DeviceIcons.EmbeddedPng(radio)!=null,"Model image library embeds a NanoStation picture");
        using(var actual=SKBitmap.Decode(DeviceIcons.EmbeddedPng(radio)!))Check(actual.Height>actual.Width*2,"Image library preserves a tall device's natural aspect ratio");
        double oldWidth=radio.Width;vm.ResizeDeviceImage(radio,24);Check(radio.ImageSize==24&&radio.Width<oldWidth&&radio.Height==48,"Image slider reduces the picture and its card footprint");
        vm.Undo();radio=vm.Project.Nodes.Single(n=>n.Id==radio.Id);Check(radio.ImageSize==48&&radio.Width==oldWidth,"Undo restores image size and geometry together");
        for(int i=0;i<3;i++){var n=vm.Add(NodeKind.AccessPoint,60+i*250,40);n.Name=new[]{"Rounded AP","Rectangle AP","Circle AP"}[i];n.Shape=(DeviceShape)i;n.Addresses="192.0.2."+(i+1);Containment.Resize(vm.Project,n,180,76);}
        var canvas=new MapCanvas(vm);var scope=new ThemeVariantScope{Child=canvas};scope.Measure(new Size(1000,500));scope.Arrange(new Rect(0,0,1000,500));canvas.Fit();
        foreach(var theme in new[]{ThemeVariant.Light,ThemeVariant.Dark}){scope.RequestedThemeVariant=theme;using var bitmap=new RenderTargetBitmap(new PixelSize(1000,500),new Vector(96,96));bitmap.Render(scope);bitmap.Save(Path.Combine(directory,theme==ThemeVariant.Dark?"device-layout-dark.png":"device-layout-light.png"));}
        PngExporter.Save(vm.Project,Path.Combine(directory,"device-layout-export.png"));var svg=VisualExporter.Svg(vm.Project);Check(svg.Contains("preserveAspectRatio=\"xMidYMid meet\"")&&svg.Contains("Example radio"),"Image export preserves proportions and side information");

        var window=new MainWindow{ShowInTaskbar=false};var model=(EditorViewModel)window.DataContext!;var ap=model.Add(NodeKind.AccessPoint);model.SetDeviceImage(ap,true);window.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();window.UpdateLayout();
        var slider=window.GetVisualDescendants().OfType<Slider>().Single(s=>s.Name=="DeviceImageSize");slider.Value=24;
        Check(ap.ImageSize==24&&ap.Height==48,"Image-size control changes the selected device live");
        var info=window.GetVisualDescendants().OfType<CheckBox>().Single(s=>s.Name=="DeviceImageInfo");info.IsChecked=false;
        Check(!ap.ShowDeviceInfo&&ap.Width==32&&ap.Height==32,"Image-only control makes a compact AP tile");
        model.Saved();window.Close();
    }
}
