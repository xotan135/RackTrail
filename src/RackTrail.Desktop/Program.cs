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
        if (args.Length == 2 && args[0] == "--smoke-test") { BuildAvaloniaApp().SetupWithoutStarting(); BrandUiChecks.Run(args[1]); Task.Run(() => SmokeTests.Run(args[1])).GetAwaiter().GetResult(); return; }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
public class App : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted() { if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow(); base.OnFrameworkInitializationCompleted(); }
}
