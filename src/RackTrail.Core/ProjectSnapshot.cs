namespace RackTrail.Core;

/// <summary>Copy on the owning thread; only the copy may be handed to a worker. Strings are immutable and shared.</summary>
public static class ProjectSnapshot
{
    public static Project Capture(Project source) => new()
    {
        FormatVersion = source.FormatVersion, Name = source.Name,
        Nodes = source.Nodes.Select(n => n.SnapshotCopy()).ToList(),
        Connections = source.Connections.Select(c => c.SnapshotCopy()).ToList()
    };
}
