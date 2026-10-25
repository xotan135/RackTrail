using Avalonia.Threading;
using RackTrail.Core;

namespace RackTrail.Desktop;

internal static class PerformanceUiChecks
{
    public static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
        var delivered = new List<long>();
        using var worker = new RouteWorker(result => delivered.Add(result.Revision), message => throw new InvalidOperationException(message));
        // Hold UI dispatch until both results are queued. A queued result must still be rejected after replacement.
        worker.Request(10, new Project(), false);
        Check(worker.Completion.Wait(TimeSpan.FromSeconds(5)), "Routing worker finishes without needing UI dispatch");
        worker.Request(11, new Project(), false);
        Check(worker.Completion.Wait(TimeSpan.FromSeconds(5)), "Replacement routing request finishes");
        Dispatcher.UIThread.RunJobs();
        Check(delivered.SequenceEqual(new long[] { 11 }), "Already-queued stale routing result is discarded after a newer revision");
        worker.Request(12, new Project(), false);
        Check(worker.Completion.Wait(TimeSpan.FromSeconds(5)), "Routing result can be queued before shutdown");
        worker.Dispose(); Dispatcher.UIThread.RunJobs();
        Check(delivered.SequenceEqual(new long[] { 11 }), "Shutdown suppresses pending routing callbacks");
        var vm = new EditorViewModel(); vm.Replace(new() { Nodes = [new() { Id = "far-left", X = -1000 }, new() { Id = "far-right", X = 1000 }],
            Connections = [new() { Id = "across", From = "far-left", To = "far-right", Route = [new(-900, 100), new(1000, 100)] }] });
        using var canvas = new MapCanvas(vm);
        canvas.SetDiagnosticRoutes(ConnectionRouter.Compute(ProjectSnapshot.Capture(vm.Project)));
        var routes = canvas.CurrentRoutes; canvas.Pan = new(40, 40); canvas.Zoom = .8; vm.Select(vm.Project.Nodes[0]);
        Check(ReferenceEquals(routes, canvas.CurrentRoutes), "Pan, zoom and selection reuse the existing routing snapshot");
        Check(canvas.CurrentRoutes.Segments.Query(new(0, 80, 100, 40)).Contains("across"), "Canvas retains a crossing edge when both endpoint cards are outside viewport");
        vm.Edit(()=>vm.Project.Nodes.Add(new(){Id="unconnected-obstacle",X=0,Y=0}));
        Check(!ReferenceEquals(routes,canvas.CurrentRoutes),"An obstacle edit invalidates the whole routing snapshot even without incident edges");
    }
}
