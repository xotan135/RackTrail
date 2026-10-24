param([string]$Runtime = 'win-x64', [string]$OutputDirectory = 'artifacts/releases', [string]$DeviceImagesDirectory = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Check-PublishContents.ps1')
$releaseFolder = Join-Path $taskRoot "$OutputDirectory/RackTrail-$Runtime"
# Recompile from clean intermediates so older symbol-bearing assemblies cannot be reused.
dotnet clean (Join-Path $taskRoot 'src/RackTrail.Desktop/RackTrail.csproj') -c Release -r $Runtime -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Release clean failed.' }
foreach ($taskProjectName in @('RackTrail.Core', 'RackTrail.Desktop')) {
    $taskIntermediateFolder = Join-Path $taskRoot "src/$taskProjectName/obj/Release"
    if (Test-Path -LiteralPath $taskIntermediateFolder) {
        Get-ChildItem -LiteralPath $taskIntermediateFolder -File -Filter '*.pdb' -Recurse | Remove-Item -Force
    }
}
dotnet publish (Join-Path $taskRoot 'src/RackTrail.Desktop/RackTrail.csproj') -c Release -r $Runtime --self-contained true -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$taskRoot=/_/RackTrail" -o $releaseFolder
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
# Older builds may have left debugging files in a reused output folder.
Get-ChildItem -LiteralPath $releaseFolder -File -Filter '*.pdb' | Remove-Item -Force
foreach ($taskAssembly in Get-ChildItem -LiteralPath $releaseFolder -File -Filter 'RackTrail*.dll') {
    $taskAssemblyBytes = [IO.File]::ReadAllBytes($taskAssembly.FullName)
    foreach ($taskEncoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
        $taskAssemblyText = $taskEncoding.GetString($taskAssemblyBytes)
        if ($taskAssemblyText -match '(?i)[A-Z]:[\\/]+Users[\\/]+(?!Public[\\/]|Default[\\/])[^\s"<>]+') {
            throw "Personal build path found in $($taskAssembly.Name); publication stopped."
        }
    }
}
$taskIconGenerator = Start-Process -FilePath (Join-Path $releaseFolder 'RackTrail.exe') -ArgumentList '--write-icons', ('"' + (Join-Path $releaseFolder 'icons') + '"') -WindowStyle Hidden -Wait -PassThru
if ($taskIconGenerator.ExitCode -ne 0) { throw 'Icon generation failed.' }
if ($DeviceImagesDirectory) {
    foreach ($taskImage in Get-ChildItem -LiteralPath $DeviceImagesDirectory -File | Where-Object { $_.Extension -in '.png','.jpg','.jpeg' }) {
        if($taskImage.Length -gt 4MB){throw 'Device pictures must be under 4 MB.'}
        Copy-Item -LiteralPath $taskImage.FullName -Destination (Join-Path $releaseFolder "icons/$($taskImage.Name)") -Force
    }
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $releaseFolder
Copy-Item -LiteralPath (Join-Path $taskRoot 'CHANGELOG.md') -Destination $releaseFolder
Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $releaseFolder
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination $releaseFolder
Copy-Item -LiteralPath (Join-Path $taskRoot 'licenses') -Destination $releaseFolder -Recurse -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs') -Destination $releaseFolder -Recurse -Force
$taskSample = Join-Path $taskRoot 'samples'
if (Test-Path -LiteralPath $taskSample) { Copy-Item -LiteralPath $taskSample -Destination $releaseFolder -Recurse -Force }
$releaseZip = Join-Path $taskRoot "$OutputDirectory/RackTrail-$Runtime.zip"
Compress-Archive -Path (Join-Path $releaseFolder '*') -DestinationPath $releaseZip -Force
Get-FileHash -LiteralPath $releaseZip -Algorithm SHA256
Write-Output "Extract $releaseZip and launch RackTrail.exe."
