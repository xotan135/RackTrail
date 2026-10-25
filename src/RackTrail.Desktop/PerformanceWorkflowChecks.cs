using RackTrail.Core;

namespace RackTrail.Desktop;

internal static class PerformanceWorkflowChecks
{
    private sealed class Resource : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }
    public static void Run(Action<bool, string> check)
    {
        var cache = new ImageResourceCache<Resource>(1024, 1); var resource = new Resource();
        using var lease = cache.Add("a", resource, 8);
        using var second = cache.Add("b", new Resource(), 8);
        check(!resource.Disposed, "Image cache eviction retains a leased drawing resource");
        lease.Dispose(); check(resource.Disposed, "Retired image is released after its final drawing lease");
        cache.Clear(); check(!second.Value.Disposed, "Clearing image cache preserves active leases");
        second.Dispose(); check(second.Value.Disposed, "Cleared image resources dispose when drawing finishes");
        var history = new HistoryBuffer(24); history.Push("first"); history.Push("second"); history.Push("third");
        check(history.Count == 2 && history.Pop() == "third" && history.Pop() == "second", "History budget evicts oldest snapshots and preserves newest undo order");
        var vm = new EditorViewModel(); var n = vm.Add(NodeKind.Server); long revision = vm.GeometryRevision;
        vm.Select(n); vm.Notify(); vm.RefreshDetails();
        check(vm.GeometryRevision == revision, "Selection, status and properties refresh do not invalidate routing geometry");
        vm.Touch(false);check(vm.GeometryRevision==revision,"Icon appearance updates preserve routing geometry revision");
        vm.Edit(() => n.X += 100); check(vm.GeometryRevision > revision, "Committed geometry edit advances routing revision");
        vm.Undo(); check(vm.Project.Nodes[0].X == 100, "Performance changes preserve undo after committed movement");
        vm.Redo(); check(vm.Project.Nodes[0].X == 200, "Performance changes preserve redo after committed movement");
    }
}
