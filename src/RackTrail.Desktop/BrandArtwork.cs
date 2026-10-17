using SkiaSharp;
namespace RackTrail.Desktop;

/// <summary>Scalable production adaptation of the selected RT route monogram.</summary>
public static class BrandArtwork
{
    public const string Navy = "#12214B";
    public const string Blue = "#1688FF";
    public const string Ice = "#E2EFFD";
    public const string Tile = "#0D1935";
    public const string RPath = "M24 74H51V87Q51 102 69 102H94Q131 102 131 136V144Q131 166 109 175L142 215H102L71 178H49V215H22V174Q22 149 48 149H94Q102 149 102 142V134Q102 126 94 126H51Q22 126 22 95V74Z";
    public const string TPath = "M81 44H209Q225 44 225 60V94Q225 110 209 110H180V170Q180 187 197 187V215H193Q149 215 149 173V82H81Z";
    public const string Rings = "M69 45A26 26 0 1 0 17 45A26 26 0 1 0 69 45Z M55 45A12 12 0 1 0 31 45A12 12 0 1 0 55 45Z M242 203A26 26 0 1 0 190 203A26 26 0 1 0 242 203Z M228 203A12 12 0 1 0 204 203A12 12 0 1 0 228 203Z";
    public static string MarkSvg(bool dark) => $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\"><path fill=\"{(dark ? Ice : Navy)}\" d=\"{RPath}\"/><path fill=\"{(dark ? Ice : Navy)}\" d=\"{TPath}\"/><path fill=\"{Blue}\" fill-rule=\"evenodd\" d=\"{Rings}\"/></svg>";
    public static string WordmarkSvg(bool dark) => $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"600\" height=\"140\" viewBox=\"0 0 600 140\"><g transform=\"translate(0 -2) scale(.56)\"><path fill=\"{(dark ? Ice : Navy)}\" d=\"{RPath}\"/><path fill=\"{(dark ? Ice : Navy)}\" d=\"{TPath}\"/><path fill=\"{Blue}\" fill-rule=\"evenodd\" d=\"{Rings}\"/></g><text x=\"152\" y=\"92\" font-family=\"Inter,Arial,sans-serif\" font-size=\"76\" font-weight=\"700\"><tspan fill=\"{(dark ? Ice : Navy)}\">Rack</tspan><tspan fill=\"{Blue}\">Trail</tspan></text></svg>";
    public static byte[] IconPng(int size)
    {
        using var bitmap = new SKBitmap(size, size); using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent); canvas.Scale(size / 256f);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(Tile) };
        canvas.DrawRoundRect(new SKRect(3, 3, 253, 253), 52, 52, paint);
        paint.Style = SKPaintStyle.Stroke; paint.Color = SKColor.Parse("#547DB8"); paint.StrokeWidth = size <= 24 ? 9 : 3;
        canvas.DrawRoundRect(new SKRect(4, 4, 252, 252), 52, 52, paint);
        canvas.Translate(25, 25); canvas.Scale(.8f);
        paint.Style = SKPaintStyle.Fill; paint.Color = SKColor.Parse(Ice);
        foreach (var shape in new[] { RPath, TPath }) { using var path = SKPath.ParseSvgPathData(shape); canvas.DrawPath(path, paint); }
        paint.Color = SKColor.Parse(Blue); using var rings = SKPath.ParseSvgPathData(Rings); rings.FillType = SKPathFillType.EvenOdd; canvas.DrawPath(rings, paint);
        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100); return png.ToArray();
    }
    public static void Export(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "RackTrail-mark-light.svg"), MarkSvg(false));
        File.WriteAllText(Path.Combine(directory, "RackTrail-mark-dark.svg"), MarkSvg(true));
        File.WriteAllText(Path.Combine(directory, "RackTrail-wordmark-light.svg"), WordmarkSvg(false));
        File.WriteAllText(Path.Combine(directory, "RackTrail-wordmark-dark.svg"), WordmarkSvg(true));
        int[] sizes = [16, 24, 32, 48, 64, 128, 256]; var pngs = sizes.Select(IconPng).ToArray();
        File.WriteAllBytes(Path.Combine(directory, "RackTrail.png"), pngs[^1]);
        using var stream = File.Create(Path.Combine(directory, "RackTrail.ico")); using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++) { writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write((uint)pngs[i].Length); writer.Write((uint)offset); offset += pngs[i].Length; }
        foreach (var png in pngs) writer.Write(png);
    }
}
