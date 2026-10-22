# Builds bin\ExtraLifeLauncher.exe with the C# compiler that ships with Visual Studio, then
# bin\ExtraLifeLauncherSetup.exe with Inno Setup (winget install JRSoftware.InnoSetup).
# Targets .NET Framework 4.x, which is built into Windows 10/11, so testers need nothing else installed.
param([switch] $NoInstaller)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$csc = $null
if (Test-Path $vswhere) {
    $csc = & $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (-not $csc) { throw "Couldn't find csc.exe. Install Visual Studio (any edition) with the .NET desktop workload." }

$out = Join-Path $here 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
$exe = Join-Path $out 'ExtraLifeLauncher.exe'
$assets = Join-Path $here 'assets'

$compilerArgs = @(
    '/nologo', '/target:winexe', '/optimize+', '/langversion:latest',
    "/out:$exe",
    "/win32manifest:$(Join-Path $here 'app.manifest')",
    "/win32icon:$(Join-Path $assets 'icon.ico')",
    "/resource:$(Join-Path $assets 'icon.ico'),ExtraLifeLauncher.icon.ico",
    "/resource:$(Join-Path $assets 'studio\mark.png'),ExtraLifeLauncher.studio.mark.png",
    "/resource:$(Join-Path $assets 'whohaunts\logo-full.png'),ExtraLifeLauncher.whohaunts.logo-full.png",
    "/resource:$(Join-Path $assets 'whohaunts\icon.png'),ExtraLifeLauncher.whohaunts.icon.png",
    "/resource:$(Join-Path $assets 'whohaunts\paper.png'),ExtraLifeLauncher.whohaunts.paper.png",
    "/resource:$(Join-Path $assets 'silo\logo-full.png'),ExtraLifeLauncher.silo.logo-full.png",
    "/resource:$(Join-Path $assets 'silo\icon.png'),ExtraLifeLauncher.silo.icon.png",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Net.Http.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll',
    '/r:System.Web.Extensions.dll'
)
$compilerArgs += (Get-ChildItem (Join-Path $here 'src') -Filter *.cs).FullName

& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "Launcher build failed." }
Write-Host "Built $exe"

if ($NoInstaller) { return }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Write-Warning "Inno Setup not found; skipping installer. Run: winget install JRSoftware.InnoSetup"; return }
& $iscc /Q (Join-Path $here 'installer\ExtraLifeLauncher.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }
Write-Host "Built $(Join-Path $out 'ExtraLifeLauncherSetup.exe')"
