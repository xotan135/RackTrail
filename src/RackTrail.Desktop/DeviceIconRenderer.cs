using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RackTrail.Core;
using SkiaSharp;
namespace RackTrail.Desktop;

public static class DeviceIconRenderer
{
    private static readonly object rasterLock=new();
    private static readonly Dictionary<string,Bitmap> nativeImages=new();
    private static readonly Dictionary<string,SKBitmap> exportImages=new();
    private static void TrimNative()
    {
        while(nativeImages.Count>32||nativeImages.Values.Sum(b=>(long)b.PixelSize.Width*b.PixelSize.Height*4)>32L*1024*1024){var first=nativeImages.First();nativeImages.Remove(first.Key);first.Value.Dispose();}
    }
    private static void TrimExport()
    {
        while(exportImages.Count>32||exportImages.Values.Sum(b=>(long)b.ByteCount)>32L*1024*1024){var first=exportImages.First();exportImages.Remove(first.Key);first.Value.Dispose();}
    }
    public static string TypeColor(NodeKind kind)=>kind switch{
        NodeKind.Firewall=>"#A65D40",NodeKind.Server=>"#377C77",NodeKind.IoT=>"#71548B",NodeKind.Device=>"#596C80",_=>"#426B98"};
    private static bool IsTypeIcon(string key)=>key.StartsWith("type-",StringComparison.Ordinal)||key is "accesspoint" or "phone" or "phones" or "iot" or "unknown" or "hardware";
    public static bool Draw(DrawingContext context,Node node,Rect rect,bool light=false)
    {
        if(node.IsContainer)return false;
        if(DeviceIcons.EmbeddedPng(node) is {} bytes)
        {
            try{lock(rasterLock){if(!nativeImages.TryGetValue(node.IconPngBase64,out var bitmap)){using var stream=new MemoryStream(bytes);bitmap=new Bitmap(stream);nativeImages[node.IconPngBase64]=bitmap;TrimNative();}
                double scale=Math.Min(rect.Width/bitmap.PixelSize.Width,rect.Height/bitmap.PixelSize.Height),w=bitmap.PixelSize.Width*scale,h=bitmap.PixelSize.Height*scale;
                context.DrawImage(bitmap,new Rect(rect.X+(rect.Width-w)/2,rect.Y+(rect.Height-h)/2,w,h));return true;}}catch(Exception ex)when(ex is ArgumentException or InvalidDataException){ }
        }
        var icon=DeviceIcons.Vector(DeviceIcons.ResolveKey(node));if(icon==null)return false;
        using(context.PushTransform(Matrix.CreateScale(rect.Width/icon.Width,rect.Height/icon.Height)*Matrix.CreateTranslation(rect.X,rect.Y)))
            foreach(var path in icon.Paths)context.DrawGeometry(new SolidColorBrush(Color.Parse(light&&IsTypeIcon(icon.Key)?TypeColor(node.Kind):icon.Color)),null,Geometry.Parse(path));
        return true;
    }
    public static bool Draw(SKCanvas canvas,Node node,SKRect rect)
    {
        if(node.IsContainer)return false;
        if(DeviceIcons.EmbeddedPng(node) is {} bytes)
        {
            lock(rasterLock){if(!exportImages.TryGetValue(node.IconPngBase64,out var bitmap)){bitmap=SKBitmap.Decode(bytes);if(bitmap!=null){exportImages[node.IconPngBase64]=bitmap;TrimExport();}}
                if(bitmap!=null){float scale=Math.Min(rect.Width/bitmap.Width,rect.Height/bitmap.Height),w=bitmap.Width*scale,h=bitmap.Height*scale;using var paint=new SKPaint{IsAntialias=true,FilterQuality=SKFilterQuality.High};canvas.DrawBitmap(bitmap,new SKRect(rect.Left+(rect.Width-w)/2,rect.Top+(rect.Height-h)/2,rect.Left+(rect.Width+w)/2,rect.Top+(rect.Height+h)/2),paint);return true;}}
        }
        var icon=DeviceIcons.Vector(DeviceIcons.ResolveKey(node));if(icon==null)return false;
        canvas.Save();canvas.Translate(rect.Left,rect.Top);canvas.Scale((float)(rect.Width/icon.Width),(float)(rect.Height/icon.Height));using var brush=new SKPaint{IsAntialias=true,Color=SKColor.Parse(icon.Color)};
        foreach(var text in icon.Paths){using var path=SKPath.ParseSvgPathData(text);canvas.DrawPath(path,brush);}canvas.Restore();return true;
    }
}
