using System.Collections.ObjectModel;

namespace RackTrail.Core;

/// <summary>Published only after construction; consumers cannot modify route points.</summary>
public sealed class RoutingSnapshot
{
    public IReadOnlyDictionary<string, IReadOnlyList<MapPoint>> Routes { get; }
    public SpatialIndex<string> Segments { get; } = new();
    public RoutingSnapshot(IReadOnlyDictionary<string, List<MapPoint>> routes)
    {
        var copy = new Dictionary<string, IReadOnlyList<MapPoint>>();
        foreach (var (id, points) in routes)
        {
            copy[id] = Array.AsReadOnly(points.ToArray());
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1]; var b = points[i];
                Segments.Add(id, new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)));
            }
        }
        Routes = new ReadOnlyDictionary<string, IReadOnlyList<MapPoint>>(copy);
    }
    public IReadOnlyList<MapPoint> Route(string id) => Routes.TryGetValue(id, out var points) ? points : Array.Empty<MapPoint>();
}
