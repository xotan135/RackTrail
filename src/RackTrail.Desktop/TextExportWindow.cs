using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RackTrail.Core;
namespace RackTrail.Desktop;

public sealed class TextExportWindow : Window
{
    public TextExportWindow(EditorViewModel vm, IPlatformServices platform)
    {
        Title = "Text export / RackTrail"; Icon=Branding.CreateIcon();Width = 1140; Height = 820; MinWidth = 820; MinHeight = 550;
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(20) }; var top = new StackPanel { Spacing = 9 }; top.Children.Add(new TextBlock { Text = "Your map, in plain text.", FontSize = 24, FontWeight = FontWeight.SemiBold });
        var options = new WrapPanel { Orientation = Orientation.Horizontal }; options.Children.Add(new TextBlock { Text = "Layout", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,0) }); var layout = new ComboBox { ItemsSource = Enum.GetValues<TextExportLayout>(), SelectedItem = TextExportLayout.Map, Margin = new Thickness(0,0,12,0) }; options.Children.Add(layout); options.Children.Add(new TextBlock { Text = "Target columns", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,0) }); var width = new ComboBox { ItemsSource = new[] { 80, 120, 160 }, SelectedIndex = 2, Margin = new Thickness(0, 0, 12, 0) }; options.Children.Add(width);
        CheckBox Check(string label, bool value = true) { var b = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 14, 0) }; options.Children.Add(b); return b; }
        var unicode = Check("Unicode", false); var detail = Check("Detailed"); var ips = Check("IPs"); var cidr = Check("CIDR"); var vlan = Check("VLANs"); var roles = Check("Roles"); var labels = Check("Link labels"); var crlf = Check("Windows CRLF"); top.Children.Add(options); top.Children.Add(new TextBlock { Text = "Map keeps relative positions and nested containers; dense maps may expand beyond the target width. Outline uses a fixed column limit.", TextWrapping = TextWrapping.Wrap, FontSize = 11 }); root.Children.Add(top);
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12, Margin = new Thickness(0, 15), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch }; ScrollViewer.SetHorizontalScrollBarVisibility(preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto); Grid.SetRow(preview, 1); root.Children.Add(preview);
        TextExportOptions Get() => new((int)(width.SelectedItem ?? 120), unicode.IsChecked == true, detail.IsChecked == true, ips.IsChecked == true, cidr.IsChecked == true, vlan.IsChecked == true, roles.IsChecked == true, labels.IsChecked == true, crlf.IsChecked == true, (TextExportLayout)(layout.SelectedItem ?? TextExportLayout.Map));
        void Update() => preview.Text = TextExporter.Export(vm.Project, Get()); foreach (var c in new[] { unicode, detail, ips, cidr, vlan, roles, labels, crlf }) c.IsCheckedChanged += (_, _) => Update(); width.SelectionChanged += (_, _) => Update(); layout.SelectionChanged += (_, _) => Update(); vm.Changed += Update; Closed += (_, _) => vm.Changed -= Update;
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; var info = new TextBlock { FontSize = 11, Margin = new Thickness(10) };
        async Task Write(string ext) { try { var path = await platform.SavePath("Export network text", ext); if (path != null) { await File.WriteAllTextAsync(path, ext == "md" ? TextExporter.Markdown(vm.Project, Get()) : preview.Text); info.Text = "Export complete"; } } catch (Exception ex) { info.Text = ex.Message; } }
        bottom.Children.Add(MainWindow.Button("Export .txt", () => Write("txt"))); bottom.Children.Add(MainWindow.Button("Export Markdown", () => Write("md"))); bottom.Children.Add(MainWindow.Button("Copy to clipboard", async () => { try { await platform.CopyText(preview.Text ?? ""); info.Text = "Copied"; } catch (Exception ex) { info.Text = ex.Message; } })); bottom.Children.Add(info); Grid.SetRow(bottom, 2); root.Children.Add(bottom); Content = root; Update();
    }
}
