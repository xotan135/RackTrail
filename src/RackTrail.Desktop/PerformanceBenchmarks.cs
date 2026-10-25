using Avalonia;
using Avalonia.Media.Imaging;
using RackTrail.Core;
using SkiaSharp;
using System.Diagnostics;
using System.Globalization;

namespace RackTrail.Desktop;

/// <summary>Opt-in measurements. This is not run by startup or by ordinary smoke tests.</summary>
internal static class PerformanceBenchmarks
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var rows = new List<string> { "nodes,connections,operation,iterations,total_ms,mean_ms,allocated_bytes,working_set_bytes,status" };
        foreach (int size in new[] { 100, 500, 1000, 2000 })
        {
            var project = Fixture(size);
            File.WriteAllText(Path.Combine(directory, $"map-{size}.networkmap.json"), ProjectStorage.Serialize(project));
            void Measure(string name, int iterations, Action action)
            {
                action(); long allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++) action();
                timer.Stop(); long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                using var process = Process.GetCurrentProcess();
                rows.Add(FormattableString.Invariant($"{size},{project.Connections.Count},{name},{iterations},{timer.Elapsed.TotalMilliseconds:0.###},{timer.Elapsed.TotalMilliseconds / iterations:0.###},{bytes},{process.WorkingSet64},ok"));
            }
            RoutingSnapshot routing;
            using (var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var timer = Stopwatch.StartNew(); long allocated = GC.GetAllocatedBytesForCurrentThread(); string status = "complete";
                try { routing = ConnectionRouter.Compute(ProjectSnapshot.Capture(project), limit.Token); }
                catch (OperationCanceledException) { routing = ConnectionRouter.Preview(project, new SceneIndex(project)); status = "timeout-preview"; }
                timer.Stop();
                using var process = Process.GetCurrentProcess();
                rows.Add(FormattableString.Invariant($"{size},{project.Connections.Count},cold-routing,1,{timer.Elapsed.TotalMilliseconds:0.###},{timer.Elapsed.TotalMilliseconds:0.###},{GC.GetAllocatedBytesForCurrentThread() - allocated},{process.WorkingSet64},{status}"));
            }
            Measure("json-history-snapshot", 3, () => _ = ProjectStorage.Serialize(project));
            Measure("detached-worker-snapshot", 10, () => _ = ProjectSnapshot.Capture(project));
            var compatibility = ProjectSnapshot.Capture(project);
            foreach (var link in compatibility.Connections) link.Route = routing.Route(link.Id).ToList();
            var compatSnapshot = ConnectionRouter.Snapshot(compatibility);
            var linkToQuery = compatibility.Connections[0];
            Measure("compatibility-lookup-with-map-hash", 1000, () => _ = MapLayout.Route(compatibility, linkToQuery));
            Measure("snapshot-lookup-without-map-hash", 1000, () => _ = compatSnapshot.Route(linkToQuery.Id));
            var index = new SceneIndex(project); var viewport = new VisualBox(0, 0, 1280, 800);
            Measure("indexed-viewport-query", 1000, () => _ = index.NodeBounds.Query(viewport));
            Measure("indexed-hover-query", 1000, () => _ = routing.Segments.Query(new(300, 300, 12, 12)));
            var vm = new EditorViewModel(); vm.Replace(project);
            using var canvas = new MapCanvas(vm) { Zoom = .75, Pan = new(15, 15), Performance = new() };
            canvas.Measure(new Size(1280, 800)); canvas.Arrange(new Rect(0, 0, 1280, 800)); canvas.SetDiagnosticRoutes(routing);
            using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 800), new Vector(96, 96));
            canvas.DiagnosticDrawAll = true; bitmap.Render(canvas); bitmap.Render(canvas);
            Measure("full-map-offscreen-draw", 10, () => bitmap.Render(canvas));
            canvas.DiagnosticDrawAll = false; bitmap.Render(canvas); bitmap.Render(canvas);
            Measure("culled-offscreen-draw", 10, () => bitmap.Render(canvas));
            Measure("pan-offscreen-draw", 10, () => { canvas.Pan += new Vector(10, 5); bitmap.Render(canvas); });
            Measure("zoom-offscreen-draw", 10, () => { canvas.Zoom = canvas.Zoom < .76 ? .8 : .75; bitmap.Render(canvas); });
            var target = project.Nodes.First(n => !n.CanContainNodes && n.Kind != NodeKind.VirtualMachine);
            var move = new MoveSession(project, new HashSet<string> { target.Id }, index);
            Measure("indexed-drag-roundtrip", 100, () => { move.Move(1, 0); move.Move(-1, 0); });
            Measure("indexed-resize-roundtrip", 100, () => {double width=target.Width,height=target.Height;Containment.Resize(project,target,width+1,height+1,index);Containment.Resize(project,target,width,height,index);});
            canvas.Performance!.Write(Path.Combine(directory, size.ToString(CultureInfo.InvariantCulture)));
            File.WriteAllLines(Path.Combine(directory, "benchmarks.csv"), rows);
        }
    }

    private static Project Fixture(int count)
    {
        var project = new Project { Name = $"Synthetic {count}-node performance map" };
        var pictures = new List<string>();
        for (int i = 0; i < 64; i++)
        {
            using var bitmap = new SKBitmap(24 + i % 8, 24 + i % 11); using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor((byte)(40 + i * 3), (byte)(70 + i * 2), (byte)(120 + i)));
            using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100); pictures.Add(Convert.ToBase64String(png.ToArray()));
        }
        for (int i = 0; i < count; i++)
        {
            int local = i % 100, block = i / 100; double x = block % 4 * 6800, y = block / 4 * 3500;
            var kind = local switch { 0 => NodeKind.Site, 1 => NodeKind.Subnet, 2 => NodeKind.Hypervisor, 3 or 4 => NodeKind.VirtualMachine, _ => i % 10 == 0 ? NodeKind.Switch : i % 2 == 0 ? NodeKind.AccessPoint : NodeKind.Server };
            var node = new Node { Id = $"node-{i:D4}", Name = $"Device {i:D4}", Kind = kind, X = x + 50 + local % 20 * 310, Y = y + 160 + local / 20 * 500, Addresses = $"10.{block}.{local / 250}.{local % 250 + 1}" };
            if (local == 0) { node.X = x; node.Y = y; node.Width = 6600; node.Height = 3200; }
            else if (local == 1) { node.ParentId = $"node-{i - 1:D4}"; node.X = x + 20; node.Y = y + 70; node.Width = 6560; node.Height = 3110; node.Cidr = $"10.{block}.0.0/16"; }
            else
            {
                node.ParentId = $"node-{i - local + 1:D4}";
                if (kind == NodeKind.Hypervisor) { node.Width = 900; node.Height = 350; node.HypervisorPlatform = "Hyper-V"; }
                if (kind == NodeKind.VirtualMachine) { var host = project.Nodes[i - local + 2]; node.HostId = node.ParentId = host.Id; node.X = host.X + 20; node.Y = host.Y + 70 + (local - 3) * 120; }
                if (kind == NodeKind.Switch) { node.Width = 240; node.ShowStackMembers = true; node.StackMembers = Enumerable.Range(1, 5).Select(n => new StackMember { Id = $"{i}-{n}", Number = n, Name = $"Member {n}" }).ToList(); }
                if (i % 3 == 0) { node.IconKey = node.EmbeddedIconKey = $"picture-{i % 64}"; node.IconPngBase64 = pictures[i % 64]; node.UseDeviceImage = true; }
            }
            project.Nodes.Add(node);
        }
        var devices = project.Nodes.Where(n => !n.IsContainer).ToList();
        for (int i = 0; i < devices.Count; i++)
            foreach (int offset in new[] { 1, 7 })
                project.Connections.Add(new() { Id = $"link-{i:D4}-{offset}", From = devices[i].Id, To = devices[(i + offset) % devices.Count].Id, Purpose = "Synthetic link" });
        foreach (var guest in project.Nodes.Where(n => n.HostId != null)) project.Connections.Add(new() { Id = "host-" + guest.Id, From = guest.HostId!, To = guest.Id, Kind = ConnectionKind.Hosting });
        Containment.Repair(project); return project;
    }
}
