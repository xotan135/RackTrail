# Render the production vector artwork using the same renderer as the app.
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRoot 'src/RackTrail.Desktop/RackTrail.csproj'
$taskAssets = Join-Path $taskRoot 'src/RackTrail.Desktop/Assets'
dotnet run --project $taskProject -c Release -- --write-brand $taskAssets
if ($LASTEXITCODE -ne 0) { throw 'Brand icon generation failed.' }