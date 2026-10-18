#ifndef AppVersion
  #error AppVersion must be provided by Build-Installer.ps1
#endif
#ifndef SourceDir
  #error SourceDir must point to the published Windows folder
#endif
#ifndef ProjectDir
  #error ProjectDir must point to the source checkout
#endif

[Setup]
AppId={{A8E68DB8-7E36-4BE9-958D-C96224A73F41}
AppName=RackTrail
AppVersion={#AppVersion}
AppPublisher=xotan135
AppPublisherURL=https://github.com/xotan135/RackTrail
AppSupportURL=https://github.com/xotan135/RackTrail/issues
AppUpdatesURL=https://github.com/xotan135/RackTrail/releases/latest
DefaultDirName={localappdata}\Programs\RackTrail
DefaultGroupName=RackTrail
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename=RackTrail-Setup-{#AppVersion}-win-x64
SetupIconFile={#ProjectDir}\src\RackTrail.Desktop\Assets\RackTrail.ico
UninstallDisplayIcon={app}\RackTrail.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes

[Tasks]
Name: "startmenuicon"; Description: "Create a Start menu shortcut"; GroupDescription: "Shortcuts:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "icons\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\icons\*"; DestDir: "{app}\icons"; Flags: onlyifdoesntexist uninsneveruninstall recursesubdirs createallsubdirs

[Icons]
Name: "{group}\RackTrail"; Filename: "{app}\RackTrail.exe"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{autodesktop}\RackTrail"; Filename: "{app}\RackTrail.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\RackTrail.exe"; Description: "Launch RackTrail"; Flags: nowait postinstall skipifsilent
