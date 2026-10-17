# Version history

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
