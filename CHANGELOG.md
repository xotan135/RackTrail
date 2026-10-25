# Version history

## 1.12.0 — 2026-10-25

- Prepare large-map performance improvements: revision-owned background routing, viewport culling, spatial hit testing and coalesced pointer gestures.
- Reuse route, caption, image and text resources; virtualize the inventory and bound history/cache memory.
- Move icon loading, autosave and exports to detached background work with cancellation.
- Add opt-in synthetic-map benchmarks and live interaction profiling; manual build and runtime verification are pending.

## 1.11.1 — 2026-10-23

- Keep obstacle avoidance on irregular large maps by routing connections on smaller local grids when the global grid exceeds its budget.
- Protect complete container headings, including subnet and VLAN text, and keep connection captions out of device names and other traces.
- Increase spacing between nearby parallel routes and favor simpler paths and site gutters.
- Add visible gaps at line crossings and draw highlighted links last so connections are easier to follow.
- Apply readability improvements to the canvas, PNG, SVG, and HTML without moving saved devices.

## 1.11.0 — 2026-10-23

- Add standalone HTML export with embedded device pictures and the same layout and routing as SVG/PNG.
- Add browser zoom, drag-to-pan, Fit map, actual-size view, keyboard controls, and Print / PDF.
- Keep the exported viewer offline, with no external scripts, fonts, images, or service requests.

## 1.10.0 — 2026-10-22

- Route connections around devices and container headings with separate attachment points, lane and crossing penalties, and a preference for gaps between unrelated sites.
- Add Arrange > Reroute connections without moving devices or replacing custom paths.
- Reduce overview label clutter, reveal connection labels on hover/selection, and highlight links related to selected devices or containers.
- Apply the same routing and automatic large-map label cleanup to PNG and SVG exports.
- Center and scale device labels as shapes and dimensions change.
- Place model pictures beside device information, preserving image proportions.
- Add live image-size adjustment and an image-only mode for compact APs.
- Cache decoded device pictures for smooth resizing on dense maps.
- Recognize NanoStation AC and UniFi AP model image names and retain larger source image detail.
- Exclude local Nagios host configuration files from publication.

## 1.9.0 — 2026-10-22

- Show nested site/container paths, VLAN IDs and CIDRs in membership, host and discovery pickers.
- Allow empty containers to resize to 120 pixels high; preserve space for contents.
- Match click targeting to drawing order and place new items above existing objects.
- Add persistent Bring to front / Send to back controls and open-area insertion near the current view.
- Add fit-to-contents, inherited geometry locks, six alignment modes, equal spacing, and drag alignment guides.
- Add batch subnet names, outline colors, and sizes with atomic Undo/Redo.
- Add search-to-zoom with ancestor expansion and compact/expanded map controls.
- Preserve layers and colors in SVG/PNG exports and maintain compatibility with older maps.


## 1.8.0 — 2026-10-20

- Refresh the interface with rounded controls, clearer panels, muted blue accents, and a softer grey light theme with stronger icon contrast.
- Make network scope and SNMP Off/v1/v2c selection visible in Scan setup.
- Correct device types during scan review, assign existing containers, or create a subnet under a chosen parent in one undoable import.
- Validate subnet membership before importing and give unassigned devices distinct initial positions.

## 1.7.0 — 2026-10-20

- Add optional read-only SNMP/LLDP/CDP and TCP service discovery with bounded requests, cancellation and session caching.
- Classify device types with confidence, evidence, hardware/OS details and separate roles; preserve manual corrections across rescans.
- Add individual phone, IoT and Unknown symbols, discovery details, map filters and a device/confidence legend.

## 1.6.1 — 2026-10-18

- Bound project node counts and container depth, repair containment iteratively, and roll back rejected edits.
- Reject embedded PNGs exceeding dimension or decoded-memory limits before rendering or SVG embedding.
- License RackTrail under MIT and include its license in Windows distributions.

## 1.6.0 — 2026-10-17

- Text export now defaults to a spatial map, preserving relative device positions, side-by-side subnet groups, nested containers and collapsed state.
- Draw actual network and hosting connections with an exact endpoint key, including redundant links and cycles.
- Keep labels readable by expanding the character grid when needed; long names wrap within cards.
- Retain the original fixed-width outline as a selectable layout, alongside ASCII/Unicode and export detail options.

## 1.5.1 — 2026-10-17

- Added a Windows installer with per-user installation, Start menu and optional desktop shortcuts, stable upgrade identity and an uninstaller.
- Preserve custom icons and user-created files during reinstall and uninstall.
- GitHub Actions now builds and tests the app, packages installer and portable ZIP downloads, verifies installation/reinstallation/uninstall, and publishes tagged versions with SHA256 checksums.

## 1.5.0 — 2026-10-16

- Added targeted IPv4 discovery with subnet/range/address input, exclusions, target preview, cancellation and review before importing; ping replies and hostnames become generic device cards, with duplicate prevention and undo.

- Added a Phones card for representing a group of phones on a subnet, with optional addresses, subnet membership, connections and exports.

## 1.4.0 — 2026-10-14

- Start with a blank map; offer a sample prompt with a persistent Don't ask again option.
- Optional device image mode with model images from the install icons folder; retain rounded rectangle, rectangle or circle until selected.
- Unrestricted canvas movement beyond the origin, with negative coordinates included in Fit, SVG and PNG.
- Mouse resizing for sidebar panels, device cards and containers.
- Hypervisor hosts, virtual machines and explicit hosting links with copy, undo and save support.
- Container assignment moves devices inside and maintains containment during dragging and resizing.

## 1.3.0 — 2026-10-13

- Applied the selected RT route monogram to the application header and About window.
- Added theme-aware navy/blue and ice-blue/blue logo treatments.
- Replaced the executable and taskbar icon with the monogram at seven resolutions.
- Added scalable logo assets and reproducible icon generation.

## 1.2.1 — 2026-10-13

- Established RackTrail branding across the solution, assemblies, application windows,
  executable metadata, icon assets, documentation and self-contained Windows package.
- Changed the application-data directory to RackTrail.
- Preserved existing project-file compatibility.

## 1.2.0 — 2026-10-13

- Added printers as connectable devices in the palette.
- Added vendor and operating-system logos, automatic icon matching and manual overrides.
- Added an end-user `icons` folder, automatic folder refresh and embedded project icons.
- Included device logos in SVG and PNG exports.
- Added About with application version and version history.

## 1.1.0 — 2026-10-13

- Added switch stacks with one shared management interface and physical member inventory.
- Added member-specific connection ports, member renumbering and stack validation.
- Added expandable stack diagrams and stack-aware exports.
- Added executable and native-window icons.

## 1.0.0 — 2026-10-13

- Initial Windows desktop editor using Avalonia and MVVM.
- Added devices, containers, connection editing, layout, selection and undo/redo.
- Added versioned project storage, autosave/recovery and IPv4/IPv6 validation.
- Added ASCII/Unicode previews, text/Markdown/clipboard exports and SVG/PNG exports.
- Added a sample network and self-contained Windows packaging.

Versions use semantic versioning: major for breaking changes, minor for new features,
and patch for compatible fixes. Project-file format versions are managed separately.
