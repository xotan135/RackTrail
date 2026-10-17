using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System.Globalization;
namespace RackTrail.Desktop;

public sealed class BrandLogo : Control
{
    public BrandLogo() { Width = 260; Height = 48; ActualThemeVariantChanged += (_, _) => InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); bool dark = ActualThemeVariant != ThemeVariant.Light;
        var foreground = new SolidColorBrush(Color.Parse(dark ? BrandArtwork.Ice : BrandArtwork.Navy));
        var blue = new SolidColorBrush(Color.Parse(BrandArtwork.Blue));
        using (context.PushTransform(Matrix.CreateScale(48d / 256, 48d / 256)))
        {
            context.DrawGeometry(foreground, null, Geometry.Parse(BrandArtwork.RPath));
            context.DrawGeometry(foreground, null, Geometry.Parse(BrandArtwork.TPath));
            context.DrawGeometry(blue, null, Geometry.Parse("F0 " + BrandArtwork.Rings));
        }
        var typeface = new Typeface("Inter", FontStyle.Normal, FontWeight.Bold);
        var rack = new FormattedText("Rack", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 30, foreground);
        var trail = new FormattedText("Trail", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 30, blue);
        context.DrawText(rack, new Point(57, 5)); context.DrawText(trail, new Point(57 + rack.Width, 5));
    }
}
