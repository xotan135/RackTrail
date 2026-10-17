using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Reflection;
namespace RackTrail.Desktop;

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        var assembly=typeof(App).Assembly;
        string name=assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product??"RackTrail";
        string version=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]??assembly.GetName().Version?.ToString()??"";
        Title="About "+name;Icon=Branding.CreateIcon();Width=650;Height=650;MinWidth=500;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Spacing=12,Margin=new Thickness(24)};panel.Children.Add(new BrandLogo());panel.Children.Add(new TextBlock{Text="Version "+version,FontSize=16});
        panel.Children.Add(new TextBlock{Text="Local network map studio for Windows.\nProject format: 1",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(MainWindow.Label("VERSION HISTORY"));using var stream=assembly.GetManifestResourceStream("RackTrail.Desktop.CHANGELOG.md");using var reader=stream==null?null:new StreamReader(stream);string history=reader?.ReadToEnd()??"Version history unavailable.";
        panel.Children.Add(new TextBlock{Text=history,TextWrapping=TextWrapping.Wrap,FontSize=12});panel.Children.Add(MainWindow.Button("Close",()=>Close()));Content=new ScrollViewer{Content=panel};
    }
}
