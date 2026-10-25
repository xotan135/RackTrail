using RackTrail.Core;
using SkiaSharp;
namespace RackTrail.Desktop;

public sealed class IconLibrary
{
    public string FolderPath { get; }
    public IReadOnlyDictionary<string,byte[]> Images => images;
    public IReadOnlyList<string> Warnings { get; private set; } = [];
    private Dictionary<string,byte[]> images = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string,string> encodedImages=new(StringComparer.OrdinalIgnoreCase);
    public IconLibrary(string? folderPath=null,bool load=true,CancellationToken cancellation=default) { FolderPath=folderPath??Path.Combine(AppContext.BaseDirectory,"icons");if(load)Reload(cancellation); }
    internal void Adopt(IconLibrary loaded){images=loaded.images;encodedImages=loaded.encodedImages;Warnings=loaded.Warnings;}
    public void Reload(CancellationToken cancellation=default)
    {
        var next=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);var warnings=new List<string>();
        try
        {
            if(Directory.Exists(FolderPath))foreach(var file in Directory.EnumerateFiles(FolderPath).Order(StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                if(!new[]{".png",".jpg",".jpeg"}.Contains(Path.GetExtension(file),StringComparer.OrdinalIgnoreCase))continue;
                try
                {
                    if(new FileInfo(file).Length>4*1024*1024)throw new InvalidDataException("Maximum icon file size is 4 MB.");
                    using var data=SKData.CreateCopy(File.ReadAllBytes(file));using var codec=SKCodec.Create(data);
                    if(codec==null||!ImageLimits.Allows(codec.Info.Width,codec.Info.Height,Math.Max(4,codec.Info.BytesPerPixel)))throw new InvalidDataException("Use a valid image no larger than 2048 × 2048.");
                    using var bitmap=SKBitmap.Decode(data);if(bitmap==null)throw new InvalidDataException("Image could not be decoded.");
                    float scale=Math.Min(1,Math.Min(512f/bitmap.Width,512f/bitmap.Height));int w=Math.Max(1,(int)Math.Round(bitmap.Width*scale)),h=Math.Max(1,(int)Math.Round(bitmap.Height*scale));
                    using var thumbnail=new SKBitmap(w,h);using var canvas=new SKCanvas(thumbnail);canvas.Clear(SKColors.Transparent);
                    using var paint=new SKPaint{IsAntialias=true,FilterQuality=SKFilterQuality.High};canvas.DrawBitmap(bitmap,new SKRect(0,0,w,h),paint);
                    using var image=SKImage.FromBitmap(thumbnail);using var png=image.Encode(SKEncodedImageFormat.Png,100);
                    var key=Path.GetFileNameWithoutExtension(file);if(next.ContainsKey(key)){warnings.Add($"{Path.GetFileName(file)}: duplicate icon name ignored");continue;}if(key is "auto" or "device"){warnings.Add($"{key}: reserved icon name ignored");continue;}next[key]=png.ToArray();
                }
                catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException){warnings.Add($"{Path.GetFileName(file)}: {ex.Message}");}
            }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){warnings.Add(ex.Message);}
        images=next;encodedImages=next.ToDictionary(pair=>pair.Key,pair=>Convert.ToBase64String(pair.Value),StringComparer.OrdinalIgnoreCase);Warnings=warnings;
    }
    public bool ApplyTo(Project project)
    {
        bool changed=false;
        foreach(var node in project.Nodes.Where(n=>!n.IsContainer))
        {
            var key=DeviceIcons.ResolveKey(node,DeviceIcons.BuiltInKeys.Concat(images.Keys));
            if(!encodedImages.TryGetValue(key,out var encoded))continue;
            if(node.EmbeddedIconKey==key&&node.IconPngBase64==encoded)continue;
            node.EmbeddedIconKey=key;node.IconPngBase64=encoded;changed=true;
        }
        return changed;
    }
    public static void WriteBuiltIns(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach(var key in DeviceIcons.BuiltInKeys)
        {
            var icon=DeviceIcons.Vector(key);if(icon==null)continue;
            using var bitmap=new SKBitmap(128,128);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.Transparent);canvas.Scale((float)(128/icon.Width),(float)(128/icon.Height));
            using var paint=new SKPaint{IsAntialias=true,Color=SKColor.Parse(icon.Color)};
            foreach(var svgPath in icon.Paths){using var path=SKPath.ParseSvgPathData(svgPath);canvas.DrawPath(path,paint);}
            using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(Path.Combine(folder,key+".png"));data.SaveTo(file);
        }
        File.WriteAllText(Path.Combine(folder,"README.txt"),"RACKTRAIL DEVICE ICONS\n\nAdd PNG or JPG files here. Files appear in the device Icon dropdown automatically.\nName a file for a model, vendor or OS (for example cisco.png or gentoo.png) and use that\nname in Model, Vendor or Operating system for automatic matching. Matching model pictures can switch the device to image mode when edited.\nUse device image also replaces the card shape with your selected image. Use standard shape to restore the card. Filenames\nare matched without regard to case or spaces. Choose an icon manually to override.\n\nUse transparent PNGs for best results; natural aspect ratios are retained. Maximum source size: 4 MB and\n2048 x 2048 pixels. RackTrail preserves pictures up to 512 pixels and embeds selected\nimages in saved projects. No rebuild or restart is needed. Invalid files are\nskipped; the Reload icons button shows diagnostics. SVG files are not loaded\nfrom this user folder. Built-in logos remain available if their PNGs are absent.\n");
    }
}
