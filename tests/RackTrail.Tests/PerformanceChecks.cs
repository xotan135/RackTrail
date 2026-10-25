using RackTrail.Core;

internal static class PerformanceChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var cachedProject = new Project { Nodes = [new() { Id = "cache-a" }, new() { Id = "cache-b", X = 400 }],
            Connections = [new() { Id = "cached-link", From = "cache-a", To = "cache-b", Route = [new(180, 38), new(400, 38)] }] };
        var cachedLink = cachedProject.Connections[0];
        var legacyRoute = MapLayout.Route(cachedProject, cachedLink);
        var immutableRoutes = ConnectionRouter.Snapshot(cachedProject);
        check(ReferenceEquals(legacyRoute, MapLayout.Route(cachedProject, cachedLink)), "Legacy route cache retains list identity for unchanged maps");
        legacyRoute.Clear();
        check(immutableRoutes.Route(cachedLink.Id).Count == 2 && cachedLink.Route.Count == 2,
            "Mutating a legacy route list cannot corrupt immutable snapshots or saved custom routes");
        cachedProject.Nodes[0].X += 10;
        var refreshedRoute = MapLayout.Route(cachedProject, cachedLink);
        check(!ReferenceEquals(legacyRoute, refreshedRoute) && refreshedRoute.Count == 2,
            "Geometry changes replace legacy cached lists and restore their computed points");
        ConnectionRouter.Invalidate(cachedProject);
        check(!ReferenceEquals(refreshedRoute, MapLayout.Route(cachedProject, cachedLink)), "Explicit invalidation replaces legacy route references");
        var project = new Project();
        var container = new Node { Id = "container", Kind = NodeKind.Site, Width = 800, Height = 500 };
        var node = new Node { Id = "child", Kind = NodeKind.AccessPoint, ParentId = container.Id, X = 40, Y = 90 };
        var locked = new Node { Id = "locked", Kind = NodeKind.Server, X = -900, Locked = true };
        project.Nodes.AddRange([container, node, locked]);
        node.StackMembers.Add(new() { Name = "Original" });
        node.Classification = new() { Roles = ["Original"], Evidence = [new(EvidenceSource.Inventory, "Original")] };
        project.Connections.Add(new() { Id = "crossing", From = node.Id, To = locked.Id, Route = [new(-1000, 200), new(1000, 200)] });
        string observedJson=ProjectStorage.Serialize(project);
        long revision=SceneChanges.Revision(project);
        check(SceneChanges.Revision(project)==revision&&ProjectStorage.Serialize(project)==observedJson,"Scene observation changes neither revision on reads nor saved project format");
        container.Collapsed=true;
        check(SceneChanges.Revision(project)>revision,"Direct container collapse advances scene revision outside view-model commands");
        revision=SceneChanges.Revision(project);container.Collapsed=true;
        check(SceneChanges.Revision(project)==revision,"Assigning unchanged scene properties does not invalidate cached geometry");
        container.Collapsed=false;
        var copy = ProjectSnapshot.Capture(project);
        revision=SceneChanges.Revision(project);copy.Nodes[0].Collapsed=true;
        check(SceneChanges.Revision(project)==revision,"Mutating a detached worker snapshot cannot notify observers of the live model");
        copy.Nodes[0].Collapsed=false;
        node.X = 90; node.StackMembers[0].Name = "Changed"; node.Classification.Roles[0] = "Changed"; project.Connections[0].Route.Clear();
        check(copy.Nodes[1].X == 40 && copy.Nodes[1].StackMembers[0].Name == "Original" && copy.Nodes[1].Classification!.Roles[0] == "Original" && copy.Connections[0].Route.Count == 2,
            "Worker snapshot detaches mutable geometry, inventory, classification and custom routes");
        var routes = ConnectionRouter.Compute(copy);
        check(routes.Segments.Query(new(-20, 190, 40, 20)).Contains("crossing"), "Spatial query includes a crossing connection with offscreen endpoints");
        copy.Connections[0].Route.Clear();
        check(routes.Route("crossing").Count == 2, "Published routes are detached from source custom-route lists");
        var index = new SceneIndex(project); var session = new MoveSession(project, new HashSet<string> { node.Id }, index);
        session.Move(-9999, -9999); check(Containment.IsInside(node, container), "Indexed gesture clamps nested device at parent content boundary");
        session.Move(9999, 9999); check(Containment.IsInside(node, container), "Indexed gesture retains right and bottom containment");
        var lockedSession = new MoveSession(project, new HashSet<string> { locked.Id }, index); double x = locked.X;
        lockedSession.Move(20, 20); check(locked.X == x, "Indexed gesture preserves geometry locks");
        var group = new MoveSession(project, new HashSet<string> { container.Id, node.Id }, index); double relative = node.X - container.X;
        group.Move(-2000, -1000); check(container.X < 0 && node.X - container.X == relative, "Selected ancestors translate descendants once across origin");
        var fresh = new SceneIndex(project); check(fresh.NodeBounds.Query(new(node.X, node.Y, 1, 1)).Contains(node.Id), "Geometry index rebuild locates moved nodes");
        var giant = new SpatialIndex<string>(); giant.Add("continent", new(-1e12, -1e12, 2e12, 2e12));
        check(giant.Query(new(0, 0, 1, 1)).Contains("continent"), "Oversized spatial entries remain queryable without unbounded cell allocation");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool rejected = false;
        try { ConnectionRouter.Compute(copy, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
        check(rejected, "Obsolete routing can be cancelled before work begins");
        var path = Path.Combine(Path.GetTempPath(), "racktrail-save-check-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await Task.WhenAll(ProjectStorage.SaveAsync(copy, path), ProjectStorage.SaveAsync(project, path));
            check((await ProjectStorage.LoadAsync(path)).Nodes.Count == project.Nodes.Count && !File.Exists(path + ".tmp"), "Overlapping saves leave a complete atomic document and no temporary file");
        }
        finally { File.Delete(path); }
    }
}
