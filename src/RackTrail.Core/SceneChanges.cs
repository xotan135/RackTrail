using System.Runtime.CompilerServices;

namespace RackTrail.Core;

/// <summary>Model notifications are not serialized. Detached worker copies must drop observers.</summary>
public abstract class SceneObservable
{
    internal event Action? SceneChanged;
    protected void SetSceneValue<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        SceneChanged?.Invoke();
    }
    protected void DetachSceneObservers() => SceneChanged = null;
}

/// <summary>O(1) reads between edits, including direct property edits outside view-model commands.</summary>
public static class SceneChanges
{
    private sealed class Tracker
    {
        private readonly HashSet<SceneObservable> observed = new(ReferenceEqualityComparer.Instance);
        private readonly Action changed;
        private List<Node>? nodes;
        private List<Connection>? links;
        private int nodeCount = -1, linkCount = -1;
        public long Version;
        public Tracker()
        {
            var weak = new WeakReference<Tracker>(this);
            changed = () => { if (weak.TryGetTarget(out var tracker)) Interlocked.Increment(ref tracker.Version); };
        }
        public void EnsureMembership(Project project)
        {
            if (!ReferenceEquals(nodes, project.Nodes) || !ReferenceEquals(links, project.Connections) || nodeCount != project.Nodes.Count || linkCount != project.Connections.Count)
                Refresh(project);
        }
        public void Refresh(Project project)
        {
            var current = new HashSet<SceneObservable>(ReferenceEqualityComparer.Instance);
            foreach (var node in project.Nodes) { current.Add(node); foreach (var member in node.StackMembers) current.Add(member); }
            foreach (var link in project.Connections) current.Add(link);
            bool membershipChanged = false;
            foreach (var item in observed.Where(item => !current.Contains(item)).ToArray())
            { item.SceneChanged -= changed; observed.Remove(item); membershipChanged = true; }
            foreach (var item in current)
                if (observed.Add(item)) { item.SceneChanged += changed; membershipChanged = true; }
            nodes = project.Nodes; links = project.Connections; nodeCount = nodes.Count; linkCount = links.Count;
            if (membershipChanged) Interlocked.Increment(ref Version);
        }
    }
    private static readonly ConditionalWeakTable<Project, Tracker> Trackers = new();
    public static long Revision(Project project)
    {
        var tracker = Trackers.GetValue(project, _ => new Tracker());
        tracker.EnsureMembership(project);
        return Volatile.Read(ref tracker.Version);
    }
    /// <summary>Call after collection replacement within commands, including same-count list edits.</summary>
    public static long Refresh(Project project)
    {
        var tracker = Trackers.GetValue(project, _ => new Tracker());
        tracker.Refresh(project);
        return Volatile.Read(ref tracker.Version);
    }
}
