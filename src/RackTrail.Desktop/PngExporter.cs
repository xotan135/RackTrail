using RackTrail.Core;
using SkiaSharp;
namespace RackTrail.Desktop;

public static class PngExporter
{
    public static void Save(Project p, string path)
    {
        var size = VisualExporter.Bounds(p); double scale = Math.Min(1, Math.Min(10000 / size.Width, 10000 / size.Height));
        using var bitmap = new SKBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColor.Parse("#101722")); canvas.Scale((float)scale); canvas.Translate((float)-size.X,(float)-size.Y);
        using var paint = new SKPaint { IsAntialias = true };
        void Text(string value, double x, double y, int font, string color) { paint.Style = SKPaintStyle.Fill; paint.Color = SKColor.Parse(color); paint.TextSize = font; using var face = SKTypeface.FromFamilyName("sans-serif"); paint.Typeface = face; canvas.DrawText(value, (float)x, (float)y, paint); }
        void DrawNode(Node n)
        {
            paint.Style = SKPaintStyle.Fill; paint.Color = SKColor.Parse(n.IsContainer ? "#172130" : "#202d40"); var rect = new SKRect((float)n.X, (float)n.Y, (float)(n.X + n.Width), (float)(n.Y + n.DisplayHeight));
            bool image=n.UseDeviceImage&&!n.CanContainNodes;
            void Frame(){if(!n.CanContainNodes&&n.Shape==DeviceShape.Circle)canvas.DrawOval(rect,paint);else canvas.DrawRoundRect(rect,n.Shape==DeviceShape.Rectangle?0:10,n.Shape==DeviceShape.Rectangle?0:10,paint);}
            if(!image){Frame();paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=1;paint.Color=SKColor.Parse(VisualExporter.Color(n));Frame();}
            canvas.Save(); canvas.ClipRect(rect);
            float iconSize=image?(float)Math.Max(20,Math.Min(n.Width-24,n.DisplayHeight-58)):20,iconX=image?(float)(n.X+(n.Width-iconSize)/2):(float)n.X+12;
            bool hasIcon=DeviceIconRenderer.Draw(canvas,n,new SKRect(iconX,(float)n.Y+14,iconX+iconSize,(float)n.Y+14+iconSize));
            if(!hasIcon&&n.Kind==NodeKind.Printer){hasIcon=true;paint.Style=SKPaintStyle.Stroke;paint.Color=SKColor.Parse(VisualExporter.Color(n));canvas.DrawRect((float)n.X+15,(float)n.Y+16,10,6,paint);canvas.DrawRoundRect(new SKRect((float)n.X+12,(float)n.Y+21,(float)n.X+28,(float)n.Y+29),2,2,paint);canvas.DrawRect((float)n.X+15,(float)n.Y+26,10,7,paint);}
            double labelX=image?n.X+12:n.X+(hasIcon?38:14);double labelY=image?n.Y+iconSize+34:n.Y+27;Text(n.Name, labelX, labelY, 13, "#edf3fa"); Text(n.IsContainer ? n.Cidr + "  " + n.VlanId : n.VisualDetail, labelX, labelY+21, 11, "#a6b7cf");
            if(n.IsStack&&n.ShowStackMembers){int row=0;foreach(var member in n.StackMembers.OrderBy(m=>m.Number))Text($"{member.Number:00}  {member.Name} / {member.Role}",n.X+14,n.Y+83+24*row++,11,"#a6b7cf");}
            canvas.Restore();
        }
        foreach (var n in p.Nodes.Where(n => n.CanContainNodes && p.Visible(n)).OrderBy(n => p.Ancestors(n).Count())) DrawNode(n);
        foreach (var c in p.Connections) { var r = MapLayout.Route(p, c); paint.Color = SKColor.Parse("#7397c1"); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 2; for (int i = 1; i < r.Count; i++) canvas.DrawLine((float)r[i - 1].X, (float)r[i - 1].Y, (float)r[i].X, (float)r[i].Y, paint); if (r.Count > 1 && c.Kind!=ConnectionKind.Hosting) { var m = r[r.Count / 2]; Text(c.LabelFor(p), m.X + 5, m.Y - 6, 10, "#b7c9e0"); } }
        foreach (var n in p.Nodes.Where(n => !n.CanContainNodes && p.Visible(n))) DrawNode(n);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(path); data.SaveTo(file);
    }
}
