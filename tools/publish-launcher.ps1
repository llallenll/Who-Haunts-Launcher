<#
.SYNOPSIS
  Builds the launcher and its installer, then publishes them as a GitHub release.
  Installed launchers see the new version on their next start and update themselves.

.EXAMPLE
  # Bump the version in src\AssemblyInfo.cs first, then:
  .\tools\publish-launcher.ps1 -Notes "Shows download speed"
#>
param(
    [string] $Notes,
    [string] $Repo = 'llallenll/Ghost-Hunter-Launcher'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$gh = Get-Command gh -ErrorAction SilentlyContinue
$gh = if ($gh) { $gh.Source } elseif (Test-Path 'C:\Program Files\GitHub CLI\gh.exe') { 'C:\Program Files\GitHub CLI\gh.exe' }
if (-not $gh) { throw "GitHub CLI not found. Run: winget install GitHub.cli ; gh auth login" }

$info = Get-Content (Join-Path $root 'src\AssemblyInfo.cs') -Raw
$version = [regex]::Match($info, 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)').Groups[1..3].Value -join '.'
if (-not $version) { throw "Couldn't read AssemblyVersion from src\AssemblyInfo.cs" }
$tag = "v$version"

# Windows PowerShell turns a native command's stderr into an error under 'Stop', so relax it for this probe.
$ErrorActionPreference = 'Continue'
& $gh release view $tag --repo $Repo 2>$null | Out-Null
$exists = $LASTEXITCODE -eq 0
$ErrorActionPreference = 'Stop'
if ($exists) { throw "$tag is already published. Bump AssemblyVersion in src\AssemblyInfo.cs first." }

& (Join-Path $root 'build.ps1')
$exe = Join-Path $root 'bin\ExtraLifeLauncher.exe'
$setup = Join-Path $root 'bin\ExtraLifeLauncherSetup.exe'
if (-not (Test-Path $setup)) { throw "Installer wasn't built. Install Inno Setup: winget install JRSoftware.InnoSetup" }

# Older launchers update themselves from GhostHunterLauncher.exe (Ghost Hunter) or WhoHauntsLauncher.exe (Who Haunts),
# and older links point at those setups, so every release also carries copies under those names.
$legacy = Join-Path $root 'bin\legacy'
New-Item -ItemType Directory -Force $legacy | Out-Null
$legacyFiles = foreach ($name in 'GhostHunterLauncher', 'WhoHauntsLauncher') {
    $legacyExe = Join-Path $legacy "$name.exe"
    $legacySetup = Join-Path $legacy "${name}Setup.exe"
    Copy-Item $exe $legacyExe -Force
    Copy-Item $setup $legacySetup -Force
    $legacyExe, $legacySetup
}

if (-not $Notes) { $Notes = "Extra Life Launcher $tag" }
Write-Host "Publishing launcher $tag to $Repo..."
& $gh release create $tag $exe $setup @legacyFiles --repo $Repo --title "Launcher $tag" --notes $Notes --target main
if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }
Write-Host "Done. Installer link: https://github.com/$Repo/releases/latest/download/ExtraLifeLauncherSetup.exe"
