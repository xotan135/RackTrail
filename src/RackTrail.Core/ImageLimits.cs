using System.Buffers.Binary;
namespace RackTrail.Core;

public static class ImageLimits
{
    public const int MaximumDimension = 2048;
    public const long MaximumDecodedBytes = 16L*1024*1024;
    public static bool Allows(long width,long height,int bytesPerPixel=4)=>width>0&&height>0&&width<=MaximumDimension&&height<=MaximumDimension&&bytesPerPixel is >=4 and <=8&&width*height*bytesPerPixel<=MaximumDecodedBytes;

    // Project icons are PNGs; imported JPEGs are normalized to PNG by IconLibrary.
    // Inspect fixed-size header bytes only, before either native renderer allocates pixels.
    public static bool AllowsPng(ReadOnlySpan<byte> data)
    {
        if(data.Length<33||!data[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8,4))!=13||!data.Slice(12,4).SequenceEqual("IHDR"u8))return false;
        int bitDepth=data[24],colorType=data[25];
        bool validDepth=colorType switch{0=>bitDepth is 1 or 2 or 4 or 8 or 16,2 or 4 or 6=>bitDepth is 8 or 16,3=>bitDepth is 1 or 2 or 4 or 8,_=>false};
        if(!validDepth||data[26]!=0||data[27]!=0||data[28]>1)return false;
        return Allows(BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16,4)),BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20,4)),bitDepth==16?8:4);
    }
}
