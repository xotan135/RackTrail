using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RackTrail.Core;
namespace RackTrail.Desktop;
public sealed class DeviceLegendIcon(NodeKind kind) : Control
{
    static DeviceLegendIcon(){AffectsRender<DeviceLegendIcon>(ThemeVariantScope.ActualThemeVariantProperty);}
    protected override Size MeasureOverride(Size availableSize) => new(24,24);
    public override void Render(DrawingContext context) {
        var node=new Node{Kind=kind,X=-10,Y=-12,IconKey=DeviceIcons.TypeKey(kind)};
        bool light=ActualThemeVariant==Avalonia.Styling.ThemeVariant.Light;
        MapCanvas.DrawIcon(context,node,new Pen(new SolidColorBrush(Color.Parse(light?DeviceIconRenderer.TypeColor(kind):VisualExporter.Color(node))),1.5),light);
    }
}
