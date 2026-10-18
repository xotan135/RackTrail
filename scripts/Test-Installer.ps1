param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Run installer integration checks on the disposable GitHub Actions Windows runner.' }
$taskPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
$taskInstallers = @(Get-ChildItem -LiteralPath $taskPackage -Filter 'RackTrail-Setup-*-win-x64.exe')
if ($taskInstallers.Count -ne 1) { throw 'Expected exactly one installer.' }
$taskId = [guid]::NewGuid().ToString('N')
$taskInstall = Join-Path $env:RUNNER_TEMP ('RackTrail-install-test-' + $taskId)
$taskGroup = 'RackTrail test ' + $taskId
$taskShortcut = Join-Path $env:APPDATA ("Microsoft/Windows/Start Menu/Programs/$taskGroup/RackTrail.lnk")
function Invoke-Setup {
    $taskSetup = Start-Process -FilePath $taskInstallers[0].FullName -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $taskInstall + '"'),('/GROUP="' + $taskGroup + '"'),'/TASKS=startmenuicon' -WindowStyle Hidden -Wait -PassThru
    if ($taskSetup.ExitCode -ne 0) { throw "Installer failed with exit code $($taskSetup.ExitCode)." }
}
Invoke-Setup
if (!(Test-Path -LiteralPath (Join-Path $taskInstall 'RackTrail.exe')) -or !(Test-Path -LiteralPath $taskShortcut)) { throw 'Installed app or Start menu shortcut is missing.' }
$taskIcon = Join-Path $taskInstall 'icons/custom-user-icon.png'
Copy-Item -LiteralPath (Join-Path $taskInstall 'icons/windows.png') -Destination $taskIcon
$taskIconHash = (Get-FileHash -LiteralPath $taskIcon).Hash
$taskOverride = Join-Path $taskInstall 'icons/windows.png'
Copy-Item -LiteralPath (Join-Path $taskInstall 'icons/cisco.png') -Destination $taskOverride -Force
$taskOverrideHash = (Get-FileHash -LiteralPath $taskOverride).Hash
$taskMap = Join-Path $taskInstall 'user-map.networkmap.json'
[IO.File]::WriteAllText($taskMap, '{"FormatVersion":1,"Name":"User map","Nodes":[],"Connections":[]}')
Invoke-Setup
if ((Get-FileHash -LiteralPath $taskOverride).Hash -ne $taskOverrideHash -or (Get-FileHash -LiteralPath $taskIcon).Hash -ne $taskIconHash -or !(Test-Path -LiteralPath $taskMap)) { throw 'Reinstallation changed user files.' }
$taskInstalled = Start-Process -FilePath (Join-Path $taskInstall 'RackTrail.exe') -ArgumentList '--smoke-test',('"' + (Join-Path $env:RUNNER_TEMP ('RackTrail-installed-checks-' + $taskId)) + '"') -WindowStyle Hidden -Wait -PassThru
if ($taskInstalled.ExitCode -ne 0) { throw 'Installed application checks failed.' }
$taskUninstall = Start-Process -FilePath (Join-Path $taskInstall 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
if ($taskUninstall.ExitCode -ne 0) { throw 'Uninstaller failed.' }
if ((Test-Path -LiteralPath (Join-Path $taskInstall 'RackTrail.exe')) -or (Test-Path -LiteralPath $taskShortcut)) { throw 'Uninstall left the app or shortcut behind.' }
if (!(Test-Path -LiteralPath $taskMap) -or !(Test-Path -LiteralPath $taskIcon)) { throw 'Uninstall removed user data.' }
Write-Output 'PASS installer, Start menu shortcut, reinstallation, installed app, uninstall and user-file preservation.'
