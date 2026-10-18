# Windows verification

Verified on the available Windows x64 development machine with .NET SDK 10.0.402 and Avalonia 11.3.21.

- RackTrail 1.6.0 release solution build: zero warnings and errors.
- Core regression runner: 178 checks covering JSON round trips and stored routes, file save/reopen, subnet/address validation including IPv6, exceptions, ASCII/Unicode width and box alignment, deterministic text, link endpoint labels, long names, line endings, Markdown, SVG, collapse, printer types and vendor/OS icon selection.
- Native workflow smoke runner: 55 checks covering device/subnet creation, properties, membership, connections, undo/redo, subtree and connection copying, deletion/undo, save/reopen, icon folder discovery, skipped corrupt images, embedded image portability, About version history, PNG rendering/decoding, seven monogram sizes and light/dark logo treatments. This invokes the production view model, storage and export code after Avalonia initialization.
- Spatial text export: relative positions, negative coordinates, side-by-side devices, real cyclic links, collapsed containers, ASCII/Unicode, line endings, long names and the retained Outline option are covered by regression checks.
- Targeted discovery: exclusions, ranges, address limits, cancellation, ping errors and import behavior were verified with controlled probes. No live subnet scan was performed during release checks.
- Windows GUI inspection: application launch, layout, sample canvas, device creation and property-field editing were inspected through the native Windows UI. Automated smoke checks cover the remaining editor/storage/export workflow without requiring browser or server components.
- Self-contained Windows x64 package: generated with `dotnet publish --self-contained true`. The packaged executable is smoke-tested directly with its bundled runtime.

Core test results are written to `artifacts/core-test-results.txt`. Sample JSON, text, SVG and PNG artifacts are in `samples`. Packaged executable test results are in `artifacts/package-smoke-results.txt` after the release verification run.

This is verification on one Windows host. It does not establish broad Windows version, accessibility, high-DPI or installer compatibility. Both Windows installer and portable distributions are unsigned. Installer integration checks run on disposable GitHub Windows runners and cover shortcuts, reinstallation, the installed app, uninstall and user-file preservation.
