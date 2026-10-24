using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
namespace RackTrail.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--write-brand") { BrandArtwork.Export(args[1]); return; }
        if(args.Length==2&&args[0]=="--write-icons"){IconLibrary.WriteBuiltIns(args[1]);return;}
        if(args.Length==3&&args[0]=="--check-map")
        {
            var project=RackTrail.Core.ProjectStorage.LoadAsync(args[1]).GetAwaiter().GetResult();RackTrail.Core.Containment.Repair(project);Directory.CreateDirectory(args[2]);
            var issues=RackTrail.Core.NetworkValidator.Validate(project);
            var routingTimer=System.Diagnostics.Stopwatch.StartNew();
            var routes=project.Connections.Select(c=>new { c.Id,c.From,c.To,Custom=c.Route.Count>0,Points=RackTrail.Core.MapLayout.Route(project,c) }).ToList();
            routingTimer.Stop();
            File.WriteAllText(Path.Combine(args[2],"routes.json"),System.Text.Json.JsonSerializer.Serialize(new{RoutingMilliseconds=routingTimer.Elapsed.TotalMilliseconds,Connections=routes},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(args[2],"validation.json"),System.Text.Json.JsonSerializer.Serialize(issues,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(args[2],"map.svg"),RackTrail.Core.VisualExporter.Svg(project));PngExporter.Save(project,Path.Combine(args[2],"map.png"));
            File.WriteAllText(Path.Combine(args[2],"map.html"),RackTrail.Core.HtmlExporter.Create(project));
            Console.WriteLine($"Map checked: {project.Nodes.Count} nodes, {project.Connections.Count} links, {issues.Count} validation notices. Routing: {routingTimer.Elapsed.TotalMilliseconds:0} ms.");return;
        }
        if (args.Length == 2 && args[0] == "--smoke-test") { BuildAvaloniaApp().SetupWithoutStarting(); BrandUiChecks.Run(args[1]); Task.Run(() => SmokeTests.Run(args[1])).GetAwaiter().GetResult(); return; }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
public class App : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://RackTrail/")){Source=new Uri("avares://RackTrail/Assets/Modern.axaml")}); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted() { if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow(); base.OnFrameworkInitializationCompleted(); }
}
