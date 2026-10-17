using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RackTrail.Core;
using SkiaSharp;
namespace RackTrail.Desktop;

public static class DeviceIconRenderer
{
    public static bool Draw(DrawingContext context,Node node,Rect rect)
    {
        if(node.IsContainer)return false;
        if(DeviceIcons.EmbeddedPng(node) is {} bytes)
        {
            try{using var stream=new MemoryStream(bytes);using var bitmap=new Bitmap(stream);context.DrawImage(bitmap,rect);return true;}catch(Exception ex)when(ex is ArgumentException or InvalidDataException){ }
        }
        var icon=DeviceIcons.Vector(DeviceIcons.ResolveKey(node));if(icon==null)return false;
        using(context.PushTransform(Matrix.CreateScale(rect.Width/icon.Width,rect.Height/icon.Height)*Matrix.CreateTranslation(rect.X,rect.Y)))
            foreach(var path in icon.Paths)context.DrawGeometry(new SolidColorBrush(Color.Parse(icon.Color)),null,Geometry.Parse(path));
        return true;
    }
    public static bool Draw(SKCanvas canvas,Node node,SKRect rect)
    {
        if(node.IsContainer)return false;
        if(DeviceIcons.EmbeddedPng(node) is {} bytes)
        {
            using var bitmap=SKBitmap.Decode(bytes);if(bitmap!=null){using var paint=new SKPaint{IsAntialias=true,FilterQuality=SKFilterQuality.High};canvas.DrawBitmap(bitmap,rect,paint);return true;}
        }
        var icon=DeviceIcons.Vector(DeviceIcons.ResolveKey(node));if(icon==null)return false;
        canvas.Save();canvas.Translate(rect.Left,rect.Top);canvas.Scale((float)(rect.Width/icon.Width),(float)(rect.Height/icon.Height));using var brush=new SKPaint{IsAntialias=true,Color=SKColor.Parse(icon.Color)};
        foreach(var text in icon.Paths){using var path=SKPath.ParseSvgPathData(text);canvas.DrawPath(path,brush);}canvas.Restore();return true;
    }
}
