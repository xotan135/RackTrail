param([string]$OutputDirectory = 'artifacts/releases', [string]$CompilerPath = '', [string]$DeviceImagesDirectory = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
[xml]$taskProject = Get-Content -LiteralPath (Join-Path $taskRoot 'src/RackTrail.Desktop/RackTrail.csproj') -Raw
$taskVersion = [string]$taskProject.Project.PropertyGroup.Version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'A three-part release version is required.' }
if (!$CompilerPath) {
    $taskCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($taskCommand) { $CompilerPath = $taskCommand.Source }
    else { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
}
if (!(Test-Path -LiteralPath $CompilerPath)) { throw 'Install Inno Setup 6 or pass -CompilerPath with the path to ISCC.exe.' }
$taskOutput = Join-Path $OutputDirectory $taskVersion
& (Join-Path $PSScriptRoot 'Publish-Windows.ps1') -OutputDirectory $taskOutput -DeviceImagesDirectory $DeviceImagesDirectory
$taskAbsoluteOutput = [IO.Path]::GetFullPath((Join-Path $taskRoot $taskOutput))
$taskRelease = Join-Path $taskAbsoluteOutput 'RackTrail-win-x64'
$taskExeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskRelease 'RackTrail.exe')).ProductVersion.Split('+')[0]
if ($taskExeVersion -ne $taskVersion) { throw 'Published application and installer versions do not match.' }
& $CompilerPath "/DAppVersion=$taskVersion" "/DSourceDir=$taskRelease" "/DProjectDir=$taskRoot" "/O$taskAbsoluteOutput" (Join-Path $taskRoot 'installer/RackTrail.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
foreach ($taskArtifact in @((Join-Path $taskAbsoluteOutput 'RackTrail-win-x64.zip'), (Join-Path $taskAbsoluteOutput "RackTrail-Setup-$taskVersion-win-x64.exe"))) {
    $taskHash = (Get-FileHash -LiteralPath $taskArtifact -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($taskArtifact + '.sha256', $taskHash + '  ' + [IO.Path]::GetFileName($taskArtifact) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
$taskChangelog = Get-Content -LiteralPath (Join-Path $taskRoot 'CHANGELOG.md') -Raw
$taskMatch = [regex]::Match($taskChangelog, '(?ms)^## ' + [regex]::Escape($taskVersion) + ' [^\r\n]*\r?\n(.*?)(?=^## |\z)')
if (!$taskMatch.Success) { throw 'The release version must have a changelog entry.' }
$taskNotes = $taskMatch.Groups[1].Value.Trim() + "`n`nDownload the Setup EXE for a per-user installation, or extract the portable ZIP and run RackTrail.exe. Both include the runtime. These Windows x64 downloads are unsigned. SHA256 checksum files are provided.`n"
[IO.File]::WriteAllText((Join-Path $taskAbsoluteOutput 'release-notes.md'), $taskNotes, [Text.UTF8Encoding]::new($false))
Write-Output "Installer and portable ZIP ready in $taskAbsoluteOutput"
