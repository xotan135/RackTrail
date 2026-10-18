# Building RackTrail

[Back to the README](README.md)

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

### Automated Windows releases

GitHub Actions verifies each update and produces downloadable build artifacts containing the installer, portable ZIP and checksums. Pushing a version tag such as `v1.6.1` publishes those downloads as a GitHub release after all checks pass. The tag must match the app version and its changelog entry. Manual workflow runs build artifacts without publishing a release.

For a local installer build, install Inno Setup 6 and the .NET 10 SDK, then run `./scripts/Build-Installer.ps1`. Pass `-CompilerPath` if ISCC.exe is outside its default installation folder. Output is placed in `artifacts/releases/<version>/`. The existing `./scripts/Publish-Windows.ps1` remains available for portable-only builds.

## Architecture

`src/RackTrail.Core` has no UI or operating-system dependencies. It contains the network model, validation, versioned JSON storage, container layout, orthogonal routing, ASCII/Unicode export and SVG export. `src/RackTrail.Desktop/EditorViewModel.cs` owns selection, history and editor operations. `MapCanvas.cs` handles drawing and pointer gestures. `MainWindow.cs` and `TextExportWindow.cs` compose the native interface. `IPlatformServices` isolates file pickers, clipboard and recovery paths; `DesktopPlatformServices` uses Avalonia and portable .NET paths. `PngExporter.cs` uses SkiaSharp for PNG rendering.

## Application identity and artwork

The solution is `RackTrail.slnx`, the desktop project is `RackTrail.csproj`, and the executable is `RackTrail.exe`. Its multi-resolution icon is embedded in the executable and loaded by the main window, export window and dialogs for title-bar/taskbar display. `scripts/New-AppIcon.ps1` regenerates the checked-in icon assets on Windows.

The RT route monogram appears in the header and About window. Light mode uses
navy and blue; dark mode uses ice blue and blue. The navy app-icon tile works on
either desktop theme. Scalable light/dark marks and wordmarks are in
`src/RackTrail.Desktop/Assets`; the same production artwork generates the PNG
and seven-size ICO. Version 1.3.0 applies this identity throughout the app.

See the [Windows verification notes](docs/Verification.md).
