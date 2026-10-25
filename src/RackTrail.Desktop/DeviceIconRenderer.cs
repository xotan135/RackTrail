using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RackTrail.Core;
using SkiaSharp;
namespace RackTrail.Desktop;

public static class DeviceIconRenderer
{
    private static readonly object rasterLock=new();
    private static readonly ImageResourceCache<Bitmap> nativeImages=new();
    private static readonly ImageResourceCache<SKBitmap> exportImages=new();
    private static readonly Dictionary<string,Geometry> nativeVectors=[];
    private static readonly Dictionary<string,SKPath> exportVectors=[];
    private sealed class Resolution
    {
        public string Icon="",Embedded="",Vendor="",Model="",OS="",Technology="",Data="",Key="";
        public NodeKind Kind;public bool Image;public bool Stack;public bool BadNative,BadExport;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node,Resolution> resolutions=new();
    private static string Key(Node n)
    {
        var r=resolutions.GetValue(n,_=>new Resolution());
        if(r.Key.Length==0||r.Icon!=n.IconKey||r.Embedded!=n.EmbeddedIconKey||r.Vendor!=n.Vendor||r.Model!=n.Model||r.OS!=n.OperatingSystem||r.Technology!=n.StackTechnology||!ReferenceEquals(r.Data,n.IconPngBase64)||r.Kind!=n.Kind||r.Image!=n.UseDeviceImage||r.Stack!=n.IsStack)
        {
            r.BadNative=r.BadExport=false;
            r.Icon=n.IconKey;r.Embedded=n.EmbeddedIconKey;r.Vendor=n.Vendor;r.Model=n.Model;r.OS=n.OperatingSystem;r.Technology=n.StackTechnology;r.Data=n.IconPngBase64;r.Kind=n.Kind;r.Image=n.UseDeviceImage;r.Stack=n.IsStack;r.Key=DeviceIcons.ResolveKey(n);
        }
        return r.Key;
    }
    internal static void ClearCaches(){lock(rasterLock){nativeImages.Clear();exportImages.Clear();nativeVectors.Clear();foreach(var path in exportVectors.Values)path.Dispose();exportVectors.Clear();}}
    public static string TypeColor(NodeKind kind)=>kind switch{
        NodeKind.Firewall=>"#A65D40",NodeKind.Server=>"#377C77",NodeKind.IoT=>"#71548B",NodeKind.Device=>"#596C80",_=>"#426B98"};
    private static bool IsTypeIcon(string key)=>key.StartsWith("type-",StringComparison.Ordinal)||key is "accesspoint" or "phone" or "phones" or "iot" or "unknown" or "hardware";
    public static bool Draw(DrawingContext context,Node node,Rect rect,bool light=false)
    {
        if(node.IsContainer)return false;
        var resolved=Key(node);var state=resolutions.GetValue(node,_=>new Resolution());
        if(!state.BadNative&&node.IconPngBase64.Length>0&&resolved.Equals(node.EmbeddedIconKey,StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                lock(rasterLock)
                {
                    var lease=nativeImages.Acquire(node.IconPngBase64);
                    if(lease==null&&DeviceIcons.EmbeddedPng(node) is {} bytes){using var stream=new MemoryStream(bytes);var bitmap=new Bitmap(stream);lease=nativeImages.Add(node.IconPngBase64,bitmap,(long)bitmap.PixelSize.Width*bitmap.PixelSize.Height*4);}
                    if(lease==null)state.BadNative=true;
                    if(lease!=null){using(lease){var bitmap=lease.Value;double scale=Math.Min(rect.Width/bitmap.PixelSize.Width,rect.Height/bitmap.PixelSize.Height),w=bitmap.PixelSize.Width*scale,h=bitmap.PixelSize.Height*scale;context.DrawImage(bitmap,new Rect(rect.X+(rect.Width-w)/2,rect.Y+(rect.Height-h)/2,w,h));}return true;}
                }
            }catch(Exception ex)when(ex is ArgumentException or InvalidDataException){state.BadNative=true;}
        }
        var icon=DeviceIcons.Vector(Key(node));if(icon==null)return false;
        using(context.PushTransform(Matrix.CreateScale(rect.Width/icon.Width,rect.Height/icon.Height)*Matrix.CreateTranslation(rect.X,rect.Y)))
            foreach(var path in icon.Paths){if(!nativeVectors.TryGetValue(path,out var geometry)){geometry=Geometry.Parse(path);nativeVectors[path]=geometry;}context.DrawGeometry(new SolidColorBrush(Color.Parse(light&&IsTypeIcon(icon.Key)?TypeColor(node.Kind):icon.Color)),null,geometry);}
        return true;
    }
    public static bool Draw(SKCanvas canvas,Node node,SKRect rect)
    {
        if(node.IsContainer)return false;
        var resolved=Key(node);var state=resolutions.GetValue(node,_=>new Resolution());
        if(!state.BadExport&&node.IconPngBase64.Length>0&&resolved.Equals(node.EmbeddedIconKey,StringComparison.OrdinalIgnoreCase))
        {
            lock(rasterLock)
            {
                var lease=exportImages.Acquire(node.IconPngBase64);
                if(lease==null&&DeviceIcons.EmbeddedPng(node) is {} bytes){var decoded=SKBitmap.Decode(bytes);if(decoded!=null)lease=exportImages.Add(node.IconPngBase64,decoded,decoded.ByteCount);}
                if(lease==null)state.BadExport=true;
                if(lease!=null){using(lease){var bitmap=lease.Value;float scale=Math.Min(rect.Width/bitmap.Width,rect.Height/bitmap.Height),w=bitmap.Width*scale,h=bitmap.Height*scale;using var paint=new SKPaint{IsAntialias=true,FilterQuality=SKFilterQuality.High};canvas.DrawBitmap(bitmap,new SKRect(rect.Left+(rect.Width-w)/2,rect.Top+(rect.Height-h)/2,rect.Left+(rect.Width+w)/2,rect.Top+(rect.Height+h)/2),paint);}return true;}
            }
        }
        var icon=DeviceIcons.Vector(Key(node));if(icon==null)return false;
        canvas.Save();canvas.Translate(rect.Left,rect.Top);canvas.Scale((float)(rect.Width/icon.Width),(float)(rect.Height/icon.Height));using var brush=new SKPaint{IsAntialias=true,Color=SKColor.Parse(icon.Color)};
        lock(rasterLock)foreach(var text in icon.Paths){if(!exportVectors.TryGetValue(text,out var path)){path=SKPath.ParseSvgPathData(text);exportVectors[text]=path;}canvas.DrawPath(path,brush);}canvas.Restore();return true;
    }
}
