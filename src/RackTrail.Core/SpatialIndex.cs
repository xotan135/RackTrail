namespace RackTrail.Core;

/// <summary>A bounded uniform grid. Oversized objects use one overflow slot, not thousands of cells.</summary>
public sealed class SpatialIndex<T> where T : notnull
{
    private const double Cell = 256;
    private readonly Dictionary<(int X, int Y), List<(T Item, VisualBox Box)>> cells = [];
    private readonly List<(T Item, VisualBox Box)> large = [];
    public void Add(T item, VisualBox box)
    {
        var (left, top, right, bottom) = Cells(box);
        if ((long)(right - left + 1) * (bottom - top + 1) > 256) { large.Add((item, box)); return; }
        for (int x = left; x <= right; x++) for (int y = top; y <= bottom; y++)
        {
            if (!cells.TryGetValue((x, y), out var bucket)) cells[(x, y)] = bucket = [];
            bucket.Add((item, box));
        }
    }
    public HashSet<T> Query(VisualBox area)
    {
        var result = new HashSet<T>();
        void Check(IEnumerable<(T Item, VisualBox Box)> bucket)
        {
            foreach (var entry in bucket) if (Intersects(area, entry.Box)) result.Add(entry.Item);
        }
        Check(large);
        var (left, top, right, bottom) = Cells(area);
        if ((long)(right - left + 1) * (bottom - top + 1) > 4096)
            foreach (var bucket in cells.Values) Check(bucket);
        else
            for (int x = left; x <= right; x++) for (int y = top; y <= bottom; y++)
                if (cells.TryGetValue((x, y), out var bucket)) Check(bucket);
        return result;
    }
    public static bool Intersects(VisualBox a, VisualBox b) => a.X <= b.X + b.Width && a.X + a.Width >= b.X && a.Y <= b.Y + b.Height && a.Y + a.Height >= b.Y;
    private static (int, int, int, int) Cells(VisualBox b)
    {
        // Limit indexing coordinates without limiting actual map coordinates.
        static int Coordinate(double value) => (int)Math.Clamp(Math.Floor(value / Cell), -1_000_000, 1_000_000);
        return (Coordinate(b.X), Coordinate(b.Y), Coordinate(b.X + b.Width), Coordinate(b.Y + b.Height));
    }
}
