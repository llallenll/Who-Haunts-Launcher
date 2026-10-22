; Installer for the Extra Life Launcher. Built by ..\build.ps1 into bin\ExtraLifeLauncherSetup.exe.
; Per-user install: no admin prompt, and the launcher can replace itself when it self-updates.

#define AppName "Extra Life Launcher"
#define AppExe "ExtraLifeLauncher.exe"
; The exe's earlier names (Ghost Hunter, then Who Haunts), cleaned up when setup upgrades those installs.
#define LegacyExe "GhostHunterLauncher.exe"
#define LegacyExe2 "WhoHauntsLauncher.exe"
#define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + "..\bin\" + AppExe)

[Setup]
; Unchanged since "Ghost Hunter", so setup upgrades the Ghost Hunter and Who Haunts installs in place (and keeps their folder).
AppId={{6F3B2C1E-8D4A-4E7B-9C5F-2A1D0E8B7F43}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName}
AppPublisher=Extra Life Interactive
AppPublisherURL=https://github.com/llallenll/Ghost-Hunter-Launcher
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Extra Life Launcher
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir=..\bin
OutputBaseFilename=ExtraLifeLauncherSetup
SetupIconFile=..\assets\icon.ico
; The studio logo on its red (100% and 200% copies; setup picks by screen DPI).
WizardImageFile=..\assets\wizard-1x.bmp,..\assets\wizard-2x.bmp
WizardSmallImageFile=..\assets\wizard-small-1x.bmp,..\assets\wizard-small-2x.bmp
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=Extra Life Launcher
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
Type: files; Name: "{app}\{#LegacyExe2}"
Type: files; Name: "{app}\{#LegacyExe2}.old"
Type: files; Name: "{app}\{#LegacyExe2}.new"
Type: files; Name: "{autoprograms}\Ghost Hunter.lnk"
Type: files; Name: "{autodesktop}\Ghost Hunter.lnk"
Type: files; Name: "{autoprograms}\Who Haunts.lnk"
Type: files; Name: "{autodesktop}\Who Haunts.lnk"

[Files]
Source: "..\bin\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Extra Life Launcher"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Extra Life Launcher"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Extra Life Launcher"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Leftovers from launcher self-updates, and the games the launcher downloaded.
; Unity saves live in the registry and AppData\LocalLow and are kept.
Type: files; Name: "{app}\{#AppExe}.old"
Type: files; Name: "{app}\{#AppExe}.new"
Type: files; Name: "{app}\{#LegacyExe}"
Type: files; Name: "{app}\{#LegacyExe}.old"
Type: files; Name: "{app}\{#LegacyExe}.new"
Type: files; Name: "{app}\{#LegacyExe2}"
Type: files; Name: "{app}\{#LegacyExe2}.old"
Type: files; Name: "{app}\{#LegacyExe2}.new"
Type: filesandordirs; Name: "{localappdata}\WhoHaunts"
Type: filesandordirs; Name: "{localappdata}\ExtraLifeInteractive"
Type: filesandordirs; Name: "{localappdata}\GhostHunter"
