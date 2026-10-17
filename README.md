# RackTrail

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/brand-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="docs/images/brand-light.png">
  <img src="docs/images/brand-light.png" alt="RackTrail RT monogram and wordmark" width="300">
</picture>

RackTrail is a Windows desktop app for building and maintaining network maps. Document sites, subnets, devices and connections, discover devices on selected IPv4 targets, and export maps as text, SVG or PNG. Everything stays local, with no account or cloud service required.

[Download RackTrail for Windows x64](https://github.com/xotan135/RackTrail/releases/latest) · [Version history](CHANGELOG.md)

## Features

- Map sites, buildings, VLANs, subnets and security zones.
- Add devices, phone groups, switch stacks, hypervisor hosts and virtual machines.
- Scan chosen IPv4 subnets, IP ranges or individual addresses, with exclusions and review before importing.
- Use vendor icons or custom hardware images, embedded in saved projects.
- Validate addresses and subnet membership, then export text, Markdown, SVG or PNG.
- Work offline with portable JSON projects, undo/redo and local recovery.

Built with C#, .NET 10 and Avalonia. The supported release targets Windows x64.

![RackTrail sample network](samples/sample.png)

## Launch the Windows release

Download the ZIP from [GitHub Releases](https://github.com/xotan135/RackTrail/releases/latest), or run `scripts/Publish-Windows.ps1` to build it from source.

1. Download and extract `RackTrail-win-x64.zip` into a folder.
2. Run `RackTrail.exe` from the extracted folder. Keep all accompanying DLLs and runtime files together.
3. Start with an empty map, choose the sample when prompted, or use **Open** for a saved JSON project.

The release is self-contained: no .NET installation is required. This is an unsigned portable folder release, not an installer. The unpacked executable is also available at `artifacts/releases/RackTrail-win-x64/RackTrail.exe`.

## Editing workflow

### Targeted device discovery

Click **Scan subnet** in the device library. Enter an IPv4 subnet such as `192.168.20.0/24`, a full range such as `192.168.20.10-192.168.20.50`, or individual addresses. Separate multiple targets with commas or new lines. Optional exclusions accept the same formats. The preview shows the number of addresses after exclusions; scanning starts only when you click **Start scan**. Each scan is limited to 1,024 addresses. CIDR targets omit network and broadcast addresses except for /31 and /32.

Discovery uses ICMP ping and reverse DNS hostname lookup. Devices that block ping may be missed, and no reply does not mean an address is unused. It does not identify device types, vendors, wireless assignments or physical connections. Stop cancels remaining work and keeps completed results for review.

Select results and choose an existing subnet or **No subnet assignment**, then click **Add selected to map**. Existing device IP addresses are skipped; when assigning a subnet, addresses outside it are skipped too. Imported cards use the generic **Device** type; change the type and name in Properties as needed. One Undo removes the entire import. Discovery does not run automatically.

### Phones

Choose **Phones** in the device library to add one card representing the phones on a subnet. Use **Container membership** to assign it to the voice subnet or VLAN, and connect it to a switch if needed. Name it for the group (for example **Office phones**) and use Notes for any additional details. Individual handset cards and IP addresses are optional. The phone symbol and card are included in saved projects and text, SVG and PNG exports.

### Printers and device icons

Printers are connectable devices in the palette and support the same properties,
subnet membership, validation and exports as other devices.

The `icons` folder beside the executable is editable by end users. Copy PNG or JPG
files into it and they appear automatically in the device's **Device icon** dropdown
within a few seconds. Use **Open icons folder** to find it and **Reload icons** to
refresh immediately or see skipped-file diagnostics. Use transparent square PNGs
for best results. Files must be no larger than 4 MB or 2048 × 2048 pixels. SVG is
used for bundled vectors but is not imported from the end-user folder.

Set **Vendor** (for example Cisco or HP) or **Operating system** (Windows Server,
Gentoo Linux, Ubuntu or Debian). With **Automatic** selected, the app matches those
fields to filenames, ignoring case, spaces and punctuation. Vendor matches take
precedence over OS matches. `acme.png` matches Vendor `Acme Corp`. Choose an icon
manually to override automatic matching, or choose **Standard device icon** to use
the device symbol. Unrecognized names use the standard symbol.

Cisco, Windows, Gentoo, Linux, Ubuntu, Debian, Apple, HP, Dell and Ubiquiti logos
are bundled and work offline. End users can replace their folder PNGs or add any
other vendor. Selected images are embedded in saved JSON projects, so moved
projects keep their icons without machine-specific paths. SVG and PNG exports
include those icons. The **About** button displays the actual application version
and bundled [version history](CHANGELOG.md). Version 1.2.0 adds these features.

If the extracted installation is in a protected folder, the user must have write
access to add icons. The application itself works with its bundled icons there.

- Click a device in the left palette to add it, or drag it onto the canvas. Select the container kind and choose **Add container** for sites, buildings, VLANs, subnets and security zones.
- Click a node and edit its name, hostname, IPs, operating system, role and notes in the properties panel. Separate multiple addresses with commas. Property text commits when focus leaves the field; clicking Save commits the focused field.
- Use **Container membership** to assign devices or subnets to a container. This relationship is independent of network connections. Moving a container also moves its descendants. Container width and height are editable near the bottom of its properties.
- Choose **Connect**, click the first device, then the second. Click a connection segment to edit ports, protocol, bandwidth and purpose. Press Escape to cancel connection mode.
- Drag nodes to reposition them. Ctrl-click toggles selection; drag empty canvas for region selection. Use **Align top** or **Align left** with multiple selected nodes. The grid snaps to 10 model units when enabled.
- Mouse wheel zooms around the pointer. Middle or right drag pans. **Fit** centers the map; **Auto layout** arranges the device backbone and nested containers and recalculates routes.
- Double-click a container to collapse or expand it, or use its properties button. Connections to hidden descendants terminate at the collapsed container.
- Search the explorer by name, address, role or CIDR; click a result to select and locate it.
- **Validate** reports invalid IP addresses, duplicate addresses, overlapping subnets, outside-subnet devices, invalid gateways and VLAN IDs. IPv4 and IPv6 are supported. **Allow validation exceptions** marks warnings as intentional. Warnings never block saving.
- **Theme** toggles light and dark modes.

| Shortcut | Action |
| --- | --- |
| Ctrl+S / Ctrl+Shift+S | Save / Save as |
| Ctrl+O | Open |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+C / Ctrl+V | Copy / Paste selected nodes, descendants and internal links |
| Ctrl+A | Select all visible nodes |
| Delete | Delete selected nodes or connection |
| Escape | Cancel connection mode and clear selection |

Text-field copy/paste and selection keep their normal behavior. Copy/paste of map objects uses an internal clipboard; text exports use the system clipboard.

## Stacked switches

Choose **Switch stack (5 members)** in the palette, or select an existing switch and **Convert to 5-member stack**. The stack is a single logical switch with one shared management hostname, IP address and SSH/HTTPS interface address. It does not store login passwords or connect to the hardware.

Expand each member in the properties panel to record its member number, name, model, serial number, active/standby/member role, documented status and notes. Add or remove members as needed. Counts are configurable; the editor does not enforce a particular Cisco model's hardware limits or infer live status.

Select a connection to assign its **From** and **To** endpoints to physical stack members and enter their ports, for example member 3 and `Gi3/0/24`. Selecting **Entire stack / unspecified** retains a logical stack-level link. Member identities are stable, so renumbering a member preserves its connections; free-text port names are not automatically rewritten. A member with assigned connections cannot be removed until its connections are reassigned. Duplicate member numbers and missing member references are reported by validation.

**Show members on canvas** expands the stack card and draws assigned connections at their member rows. ASCII/Unicode detailed exports include member inventory and assigned ports, while overview exports summarize the member count. SVG/PNG follow the canvas member visibility. Existing projects remain readable; use this RackTrail version to save stack-aware projects, since older application versions do not preserve the new metadata.

The solution is `RackTrail.slnx`, the desktop project is `RackTrail.csproj`, and the executable is `RackTrail.exe`. Its multi-resolution icon is embedded in the executable and loaded by the main window, export window and dialogs for title-bar/taskbar display. `scripts/New-AppIcon.ps1` regenerates the checked-in icon assets on Windows.

The RT route monogram appears in the header and About window. Light mode uses
navy and blue; dark mode uses ice blue and blue. The navy app-icon tile works on
either desktop theme. Scalable light/dark marks and wordmarks are in
`src/RackTrail.Desktop/Assets`; the same production artwork generates the PNG
and seven-size ICO. Version 1.3.0 applies this identity throughout the app.

## Save and recovery

Projects are versioned, readable JSON, with portable identifiers and no machine paths. Geometry, properties, container membership, collapse state, validation exceptions, connection metadata and route points are serialized. Saving writes a temporary file beside the destination and atomically replaces the destination. Unsupported versions, missing endpoints and container cycles are rejected on load.

Unsaved changes are autosaved every 30 seconds to the platform's local application-data directory under `RackTrail/recovery.racktrail.json`. On Windows this is normally `%LOCALAPPDATA%/RackTrail`. The next launch offers to restore this snapshot. Normal saves clear the snapshot. Recovery is a single latest snapshot; run one editor instance at a time. New/open/close prompt to save unsaved work.

## Text and visual exports

**Text export** opens a live monospaced preview with 80, 120 and 160 column limits. Choose strict ASCII or Unicode borders; overview or detailed output; IPs, CIDRs, VLANs, roles and connection labels; and Windows CRLF or Unix LF line endings.

Output is generated directly from the model and has three parts:

1. A compact topology spanning forest built from actual connections.
2. Numbered hierarchy sections containing nested boxes for sites, subnets and devices.
3. An exact connection ledger containing both endpoint references and optional link metadata. This preserves cyclic, redundant and cross-section links without text crossings or overlapping labels. A repeated endpoint in the topology is shown as a reference.

Deep hierarchies receive continuation sections instead of exceeding the width limit. Strict ASCII replaces unsupported characters predictably with `?` after compatibility normalization. Unchanged models produce identical output. Export `.txt`, Markdown fences or clipboard text. Text layout targets ordinary single-cell monospaced labels; wide Unicode glyphs may require ASCII mode for consistent terminal alignment.

**SVG** produces a scalable model-based vector drawing; **PNG** uses SkiaSharp. Both preserve positions and visible routes and honor collapsed containers. PNG dimensions are capped at 10,000 pixels per axis to bound memory use. The UI bundles Inter. PNG and SVG use platform sans-serif font fallback, so font metrics can differ slightly across systems.

## Build and verify

Install the .NET 10 SDK. Internet access is needed for the first NuGet restore.

```powershell
dotnet restore RackTrail.slnx
dotnet build RackTrail.slnx -c Release
dotnet run --project tests/RackTrail.Tests -c Release
dotnet run --project src/RackTrail.Desktop -c Release -- --smoke-test artifacts/verification
dotnet run --project src/RackTrail.Desktop -c Release
```

The test executable has no test-framework dependency and exits nonzero on failure. It verifies serialization and atomic save/reopen, IPv4/IPv6 validation, exceptions, deterministic ASCII/Unicode output, box alignment and width limits, link endpoints, long labels, line endings, Markdown, SVG and collapsed endpoints. Native smoke checks exercise view-model editing, connections, undo/redo, copy/paste, file reopen and Skia PNG generation. Generated sample exports are placed in `artifacts/verification`.

Create a self-contained release:

```powershell
./scripts/Publish-Windows.ps1
```

## Architecture and current limits

`src/RackTrail.Core` has no UI or operating-system dependencies. It contains the network model, validation, versioned JSON storage, container layout, orthogonal routing, ASCII/Unicode export and SVG export. `src/RackTrail.Desktop/EditorViewModel.cs` owns selection, history and editor operations. `MapCanvas.cs` handles drawing and pointer gestures. `MainWindow.cs` and `TextExportWindow.cs` compose the native interface. `IPlatformServices` isolates file pickers, clipboard and recovery paths; `DesktopPlatformServices` uses Avalonia and portable .NET paths. `PngExporter.cs` uses SkiaSharp for PNG rendering.

The layout and routing engines are deterministic heuristics, not a full obstacle-avoidance router. Dense arbitrary maps may benefit from manual positioning. Stored custom route points are rendered and preserved; the initial UI provides automatic routes and a reset command, without a waypoint editing tool. SVG/PNG labels on very dense diagrams can overlap; text exports avoid that through separate connection lanes and references. The initial release has no installer, code signing, automatic topology discovery or multi-user editing.

See the [Windows verification notes](docs/Verification.md).

### Device images and canvas controls

Select a device, enter its Model (for example `U6 Pro`), and choose its Device icon. Click **Use device image** to replace the card shape; **Use standard shape** restores it. Add a PNG/JPG model image such as `u6pro.png` to the install `icons` folder, then select Automatic or that image. Vendor logos remain available; supply a model image to show the actual hardware. Images are embedded in saved projects.

Drag selected nodes by the blue bottom-right handle to resize them, or drag the vertical panel dividers to resize the sidebars. Pan with the middle/right mouse button and zoom with the wheel. Free nodes can move left or above the origin; Fit and exports include them. Assigned nodes stay inside their containers, which expand as needed on assignment.

Add a Hypervisor (default Hyper-V), then use **Add virtual machine** or assign a VM through its Hypervisor host selector. This creates a hosting link and places it inside the host. Network connections can be added separately. VM container membership can also be changed to a subnet while retaining its host relationship.

RackTrail starts with an empty map and asks whether to load the sample. **Don't ask again** suppresses that prompt on future launches; it always starts blank unless you explicitly restore recovery. **Load sample network** remains available in the Properties panel when nothing is selected.
