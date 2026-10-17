param([string]$Runtime = 'win-x64', [string]$OutputDirectory = 'artifacts/releases')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$releaseFolder = Join-Path $taskRoot "$OutputDirectory/RackTrail-$Runtime"
dotnet publish (Join-Path $taskRoot 'src/RackTrail.Desktop/RackTrail.csproj') -c Release -r $Runtime --self-contained true -p:UseSharedCompilation=false -o $releaseFolder
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$taskIconGenerator = Start-Process -FilePath (Join-Path $releaseFolder 'RackTrail.exe') -ArgumentList '--write-icons', ('"' + (Join-Path $releaseFolder 'icons') + '"') -WindowStyle Hidden -Wait -PassThru
if ($taskIconGenerator.ExitCode -ne 0) { throw 'Icon generation failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $releaseFolder
Copy-Item -LiteralPath (Join-Path $taskRoot 'CHANGELOG.md') -Destination $releaseFolder
Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $releaseFolder
Copy-Item -LiteralPath (Join-Path $taskRoot 'licenses') -Destination $releaseFolder -Recurse -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs') -Destination $releaseFolder -Recurse -Force
$taskSample = Join-Path $taskRoot 'samples'
if (Test-Path -LiteralPath $taskSample) { Copy-Item -LiteralPath $taskSample -Destination $releaseFolder -Recurse -Force }
$releaseZip = Join-Path $taskRoot "$OutputDirectory/RackTrail-$Runtime.zip"
Compress-Archive -Path (Join-Path $releaseFolder '*') -DestinationPath $releaseZip -Force
Get-FileHash -LiteralPath $releaseZip -Algorithm SHA256
Write-Output "Extract $releaseZip and launch RackTrail.exe."
