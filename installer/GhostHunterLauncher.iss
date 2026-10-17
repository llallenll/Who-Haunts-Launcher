; Installer for the Who Haunts Launcher. Built by ..\build.ps1 into bin\WhoHauntsLauncherSetup.exe.
; Per-user install: no admin prompt, and the launcher can replace itself when it self-updates.

#define AppName "Who Haunts Launcher"
#define AppExe "WhoHauntsLauncher.exe"
; The exe's name from before the game was renamed, cleaned up when setup upgrades that install.
#define LegacyExe "GhostHunterLauncher.exe"
#define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + "..\bin\" + AppExe)

[Setup]
; Unchanged since "Ghost Hunter", so setup upgrades that install in place (and keeps its folder).
AppId={{6F3B2C1E-8D4A-4E7B-9C5F-2A1D0E8B7F43}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName}
AppPublisher=Who Haunts
AppPublisherURL=https://github.com/llallenll/Ghost-Hunter-Launcher
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Who Haunts Launcher
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir=..\bin
OutputBaseFilename=WhoHauntsLauncherSetup
SetupIconFile=..\assets\icon.ico
; Burnt-paper art matching the game and launcher (100% and 200% copies; setup picks by screen DPI).
WizardImageFile=..\assets\wizard-1x.bmp,..\assets\wizard-2x.bmp
WizardSmallImageFile=..\assets\wizard-small-1x.bmp,..\assets\wizard-small-2x.bmp
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=Who Haunts
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Matches the launcher's single-instance mutex, so setup asks to close a running launcher.
AppMutex=GhostHunterLauncher.SingleInstance

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[InstallDelete]
; What a "Ghost Hunter" install leaves behind.
Type: files; Name: "{app}\{#LegacyExe}"
Type: files; Name: "{app}\{#LegacyExe}.old"
Type: files; Name: "{app}\{#LegacyExe}.new"
Type: files; Name: "{autoprograms}\Ghost Hunter.lnk"
Type: files; Name: "{autodesktop}\Ghost Hunter.lnk"

[Files]
Source: "..\bin\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Who Haunts"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Who Haunts"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Who Haunts"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Leftovers from launcher self-updates, and the game files the launcher downloaded.
; Unity saves live in the registry and AppData\LocalLow and are kept.
Type: files; Name: "{app}\{#AppExe}.old"
Type: files; Name: "{app}\{#AppExe}.new"
Type: files; Name: "{app}\{#LegacyExe}"
Type: files; Name: "{app}\{#LegacyExe}.old"
Type: files; Name: "{app}\{#LegacyExe}.new"
Type: filesandordirs; Name: "{localappdata}\WhoHaunts"
Type: filesandordirs; Name: "{localappdata}\GhostHunter"
