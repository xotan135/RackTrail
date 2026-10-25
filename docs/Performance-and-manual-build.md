# Large-map performance changes and manual build practice

## Verification status

These source changes have **not been compiled, executed, packaged, or pushed**. The user requested manual build practice. No before/after timings, passing-test counts, or FPS claims are available yet. Static review and whitespace checks do not establish runtime correctness.

The project version is 1.12.0; the saved-project format is unchanged. Existing generated binaries are older builds: use the commands below to produce binaries containing these changes.

## What changed

- The canvas owns a geometry revision, immutable route snapshots and a reusable scene index. Panning, zooming, selection and hover reuse routing results. A general status update no longer repaints the map.
- One cancellable background worker computes routes from a detached model copy. It keeps at most one replacement request. Generation tickets reject queued stale results after replacement, cancellation or shutdown. Caption-only requests reuse existing routes instead of rerouting geometry.
- Exports reuse a single route snapshot for bounds, captions and drawing. Standalone callers that directly mutate the public model retain the compatibility fingerprint check in `ConnectionRouter.Route`/`Snapshot`; the interactive canvas does not use that lookup path.
- A bounded spatial grid culls nodes and connection segments, retaining connections that cross the viewport with both endpoints offscreen. Hit testing and marquee selection use the same index. Large container bounds use overflow entries instead of allocating millions of grid cells.
- Pointer events coalesce at approximately 16 ms. Release flushes the final pointer position before snapping and committing. Drag sessions retain roots, descendants, affected links and alignment-guide bins. Drag previews update only affected routes. Full routing resumes after the gesture.
- Image caches check for existing decoded resources before Base64 decoding. Each renderer has a 64 MiB / 256-entry LRU limit that includes encoded keys. Leases defer disposal until active drawing calls finish. Invalid images retain dimension/decoded-byte guards and safe fallbacks. Vector geometry, text layouts, brushes and pens are reused.
- The inventory uses a finite-height virtualized list. Selection/status changes do not rebuild its data; unchanged rows are retained after document edits. Icon PNGs are encoded once per library reload.
- Each undo/redo buffer retains at most 100 snapshots within an estimated 32 MiB string budget, except that one oversized newest snapshot is retained to preserve rollback. Project history still uses JSON, so the initial checkpoint of an image-heavy map can pause the UI; measure this rather than assuming it is resolved.
- Autosave and exports use detached copies with shared immutable strings. Serialization and file work run off the UI thread. Atomic saves are serialized to avoid shared temporary-file races. Exports are limited to one at a time and routing receives shutdown cancellation.

## Build manually

Open PowerShell in the project directory. Install the .NET **10 SDK** if `dotnet --list-sdks` does not show a 10.x SDK.

Run these commands individually and inspect the output after each one. Stop at the first error.

```powershell
Set-Location 'D:\projects\Network Map'
dotnet --list-sdks
dotnet restore .\RackTrail.slnx
dotnet build .\RackTrail.slnx -c Release --no-restore
```

`restore` downloads the referenced dependencies. `build` compiles the application and test project. A successful build ends with `Build succeeded`; it does not mean the tests have run.

Run the existing and new regression checks:

```powershell
dotnet run --project .\tests\RackTrail.Tests -c Release --no-build
dotnet run --project .\src\RackTrail.Desktop -c Release --no-build -- --smoke-test artifacts/manual-checks
```

The added checks cover detached snapshots, crossing edges, bounded spatial indexing, containment/locks, history eviction, image-resource lifetime, queued stale routing results, shutdown suppression and overlapping atomic saves. Native checks also exercise existing UI and export behavior.

Launch your newly compiled application:

```powershell
dotnet run --project .\src\RackTrail.Desktop -c Release --no-build
```

## Measure instead of guessing

Generate synthetic maps and benchmark CSV files:

```powershell
dotnet run --project .\src\RackTrail.Desktop -c Release --no-build -- --performance-check artifacts/performance
```

This creates maps with 100, 500, 1,000 and 2,000 total nodes, nested site/subnet containers, stacks, Hyper-V hosts/VMs, about two network links per device, hosting links and 64 distinct synthetic pictures. It does not scan a network or open a real user map.

Read `artifacts/performance/benchmarks.csv`. It compares warmed full-map drawing with viewport-culling drawing, and the compatibility hash lookup with direct snapshot lookup, **within the same modified executable**. These are isolated algorithm comparisons, not complete before/after application measurements. It also records pan/zoom drawing, hover queries, paired drag/resize round trips, JSON history cost, detached-copy cost, allocations, working set and cold routing. Routing has a ten-second budget per fixture; `timeout-preview` means full routing did not finish and the later drawing measurements used preview routes. Never report that row as completed routing.

For real interaction measurements:

```powershell
dotnet run --project .\src\RackTrail.Desktop -c Release --no-build -- --profile-map artifacts/performance/map-2000.networkmap.json artifacts/performance/live-2000
```

Pan for 15 seconds, zoom in/out for 15 seconds, hover over nodes/links, drag one node, drag a container, resize each, then close the app normally (discard changes to the synthetic map if prompted). Repeat at other map sizes by changing the input filename and output directory.

The application writes `interaction.csv` and `machine.txt` on close. Compare p50/p95/max UI drawing time, coalesced input-queue delay, pointer processing, background routing/caption duration, allocation counts and memory. UI drawing timings measure submission to Avalonia, not GPU presentation or guaranteed FPS. Input-queue delay is not hardware-to-screen latency. For a true before/after app comparison, repeat the same actions on the unmodified version using an external profiler; this version's new CSV instrumentation cannot retroactively measure the original version.

Keep the same machine, map, viewport size, zoom range, power plan and Release configuration for comparisons. Record CPU/GPU/RAM details along with `machine.txt`. Warm up first. A 16.7 ms routine drawing budget is a target, not a verified result. Fully zoomed-out maps still draw many objects; cold obstacle routing, very large labels, PNG export buffers and full JSON history snapshots can remain expensive.

## Package only after checks pass

Close running copies of the application before packaging.

```powershell
.\scripts\Check-PublishContents.ps1
.\scripts\Publish-Windows.ps1 -OutputDirectory artifacts/manual-release
```

The portable executable will be under `artifacts/manual-release/RackTrail-win-x64/`, with the ZIP alongside it. The publish script restores the self-contained `win-x64` runtime before cleaning and publishing, avoiding the earlier `NETSDK1047` failure.

For the installer, install **Inno Setup 6** first, then run:

```powershell
.\scripts\Build-Installer.ps1 -OutputDirectory artifacts/manual-installer
```

The installer is written under `artifacts/manual-installer/1.12.0/`. Launch it manually to practice installing the app. If the application version changes later, use that version's folder. The automated `Test-Installer.ps1` integration check deliberately runs only on disposable GitHub Actions runners; it is not a local manual-build command.

None of these commands commits, pushes, creates a tag or publishes a GitHub release.
