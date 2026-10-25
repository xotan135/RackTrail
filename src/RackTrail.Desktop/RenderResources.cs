using Avalonia;
using Avalonia.Media;
using RackTrail.Core;
using System.Globalization;

namespace RackTrail.Desktop;

/// <summary>UI-thread-only resources; position and viewport transforms are deliberately not cache keys.</summary>
internal sealed class RenderResources
{
    private readonly Dictionary<string, SolidColorBrush> brushes = [];
    private readonly Dictionary<(string, double), Pen> pens = [];
    private readonly Dictionary<(string Text, double Width, double Size, bool Center, string Color), FormattedText> texts = [];
    public SolidColorBrush Brush(string color)
    {
        if (!brushes.TryGetValue(color, out var brush)) { if (brushes.Count >= 128) brushes.Clear(); brushes[color] = brush = new(Color.Parse(color)); }
        return brush;
    }
    public Pen Pen(string color, double width)
    {
        if (!pens.TryGetValue((color, width), out var pen)) { if (pens.Count >= 256) pens.Clear(); pens[(color, width)] = pen = new(Brush(color), width); }
        return pen;
    }
    public void Label(DrawingContext dc, VisualLabel label, string color)
    {
        if (label.Width < 1) return;
        var key = (label.Text, label.Width, label.Size, label.Center, color);
        if (!texts.TryGetValue(key, out var text))
        {
            if (texts.Count >= 8192) texts.Clear();
            texts[key] = text = new(label.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), label.Size, Brush(color))
            { MaxTextWidth = label.Width, MaxTextHeight = label.Size * 1.4, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = label.Center ? TextAlignment.Center : TextAlignment.Left };
        }
        using (dc.PushClip(new Rect(label.X, label.Y, label.Width, label.Size * 1.4))) dc.DrawText(text, new(label.X, label.Y));
    }
}
