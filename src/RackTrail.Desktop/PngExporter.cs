using RackTrail.Core;
using SkiaSharp;
namespace RackTrail.Desktop;

public static class PngExporter
{
    public static void Save(Project p, string path,bool showConnectionLabels=false,CancellationToken cancellation=default)
    {
        var routing=ConnectionRouter.Snapshot(p,cancellation);
        var size = VisualExporter.Bounds(p,routing); double scale = Math.Min(1, Math.Min(10000 / size.Width, 10000 / size.Height));
        using var bitmap = new SKBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColor.Parse("#101722")); canvas.Scale((float)scale); canvas.Translate((float)-size.X,(float)-size.Y);
        using var paint = new SKPaint { IsAntialias = true };
        void Text(string value, double x, double y, int font, string color) { paint.Style = SKPaintStyle.Fill; paint.Color = SKColor.Parse(color); paint.TextSize = font; using var face = SKTypeface.FromFamilyName("sans-serif"); paint.Typeface = face; canvas.DrawText(value, (float)x, (float)y, paint); }
        void DrawNode(Node n)
        {
            cancellation.ThrowIfCancellationRequested();
            paint.Style = SKPaintStyle.Fill; paint.Color = SKColor.Parse(n.IsContainer ? "#172130" : "#202d40"); var rect = new SKRect((float)n.X, (float)n.Y, (float)(n.X + n.Width), (float)(n.Y + n.DisplayHeight));
            bool image=n.UseDeviceImage&&!n.CanContainNodes;
            void Frame(){if(!n.CanContainNodes&&n.Shape==DeviceShape.Circle)canvas.DrawOval(rect,paint);else canvas.DrawRoundRect(rect,n.Shape==DeviceShape.Rectangle?0:10,n.Shape==DeviceShape.Rectangle?0:10,paint);}
            if(!image){Frame();paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=1;paint.Color=SKColor.Parse(VisualExporter.Color(n));Frame();}
            canvas.Save(); canvas.ClipRect(rect);
            var presentation=NodePresentation.Create(n);var icon=presentation.Icon;
            if(!DeviceIconRenderer.Draw(canvas,n,new SKRect((float)icon.X,(float)icon.Y,(float)(icon.X+icon.Width),(float)(icon.Y+icon.Height))))
            {
                var vector=DeviceIcons.Vector(DeviceIcons.TypeKey(n.Kind));
                if(vector!=null){canvas.Save();canvas.Translate((float)icon.X,(float)icon.Y);canvas.Scale((float)(icon.Width/vector.Width),(float)(icon.Height/vector.Height));paint.Style=SKPaintStyle.Fill;paint.Color=SKColor.Parse(vector.Color);foreach(var pathText in vector.Paths){using var path=SKPath.ParseSvgPathData(pathText);canvas.DrawPath(path,paint);}canvas.Restore();}
            }
            foreach(var line in presentation.Labels)
            {
                paint.Style=SKPaintStyle.Fill;paint.Color=SKColor.Parse("#edf3fa");paint.TextSize=(float)line.Size;paint.TextAlign=line.Center?SKTextAlign.Center:SKTextAlign.Left;
                using var face=SKTypeface.FromFamilyName("sans-serif");paint.Typeface=face;
                var text=line.Text;if(paint.MeasureText(text)>line.Width){while(text.Length>0&&paint.MeasureText(text+"…")>line.Width)text=text[..^1];text+="…";}
                canvas.DrawText(text,(float)(line.X+(line.Center?line.Width/2:0)),(float)(line.Y+line.Size),paint);
            }
            paint.TextAlign=SKTextAlign.Left;
            canvas.Restore();
        }
        var captions=ConnectionCaptions.Create(p,p.Connections.Where(c=>showConnectionLabels||ConnectionRouter.ShowLabel(p)),routing,cancellation);
        var connections=p.Connections.ToLookup(c=>MapEditing.ConnectionLayer(p,c));
        foreach(var layer in MapEditing.PaintOrder(p).GroupBy(n=>MapEditing.Layer(p,n)))
        {
            foreach(var n in layer.Where(n=>n.CanContainNodes))DrawNode(n);
            foreach(var c in connections[layer.Key])
            {
                var r=routing.Route(c.Id);paint.Style=SKPaintStyle.Stroke;
                foreach(var stroke in new[]{("#101722",5.6f),("#7397c1",1.6f)})
                {
                    paint.Color=SKColor.Parse(stroke.Item1);paint.StrokeWidth=stroke.Item2;
                    for(int i=1;i<r.Count;i++)canvas.DrawLine((float)r[i-1].X,(float)r[i-1].Y,(float)r[i].X,(float)r[i].Y,paint);
                }
                if(captions.TryGetValue(c.Id,out var caption))
                {
                    var box=caption.Box;paint.Style=SKPaintStyle.Fill;paint.Color=SKColor.Parse("#101722");
                    canvas.DrawRoundRect(new SKRect((float)box.X,(float)box.Y,(float)(box.X+box.Width),(float)(box.Y+box.Height)),3,3,paint);
                    Text(caption.Text,box.X+6,box.Y+14,10,"#b7c9e0");
                }
            }
            foreach(var n in layer.Where(n=>!n.CanContainNodes))DrawNode(n);
        }
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(path); data.SaveTo(file);
    }
}
