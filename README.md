# Extra Life Launcher

The game launcher for Extra Life Interactive. It lists the studio's games, installs and updates each one from GitHub Releases, launches them, and keeps itself up to date. Players don't need to log in.

Games right now:

- **Who Haunts** (early access), from `llallenll/Ghost-Hunter-Releases`
- **The Silo** (coming soon), not on GitHub yet, so the launcher shows it as Coming Soon

## Giving it to testers

Send them the installer:

https://github.com/llallenll/Ghost-Hunter-Launcher/releases/latest/download/ExtraLifeLauncherSetup.exe

It installs per user (no admin prompt) to `%LOCALAPPDATA%\Programs\Extra Life Launcher` (an upgraded install keeps its old folder), adds Start menu and desktop shortcuts, and appears in Apps & features. Uninstalling also removes the downloaded games. Game settings (in the registry) are kept.

The exe isn't code-signed, so Windows SmartScreen shows "Windows protected your PC" the first time. Testers click **More info**, then **Run anyway**.

## Adding a game, or releasing The Silo

Every game is an entry in `LauncherConfig.Games` (`src/LauncherConfig.cs`): its name, release repo, zip asset name, exe name, install folder, colours and art. The art lives in `assets/<game>/` and is embedded by `build.ps1` (add a `/resource` line for each new file).

To put The Silo on the launcher for real, create a **public** releases repo, attach `TheSilo-Windows.zip` (with `The Silo.exe` inside) to a release, then set `RepoOwner` and `RepoName` on its entry. Leaving them `null` keeps it as Coming Soon. Then publish a launcher update (below).

## How it works

- **Game updates:** for each game, reads releases from its public repo through the anonymous GitHub API. It picks the newest non-draft release, pre-releases included, that has the game's zip asset, and installs it to the game's folder (`%LOCALAPPDATA%\WhoHaunts\Game` for Who Haunts, `%LOCALAPPDATA%\ExtraLifeInteractive\<Game>\Game` for new games). The new build is extracted next to the old one and then swapped in, so a failed update never leaves a broken install. It verifies the SHA-256 digest GitHub reports for the asset, and still lets you play offline. Games download independently, so two can update at once.
- **Launcher updates:** on every start it checks this repo's releases. If the newest tag (for example `v2.0.1`) is higher than its own `AssemblyVersion`, it downloads `ExtraLifeLauncher.exe` from that release, swaps it in for itself and restarts. Older launchers look for `WhoHauntsLauncher.exe` or `GhostHunterLauncher.exe`, so releases carry copies under those names too. Run the launcher with `--no-self-update` to skip this.

## Build

Requires Visual Studio 2022 (for its C# compiler) and Inno Setup (`winget install JRSoftware.InnoSetup`) for the installer.

```powershell
.\build.ps1            # -> bin\ExtraLifeLauncher.exe and bin\ExtraLifeLauncherSetup.exe
.\build.ps1 -NoInstaller
```

The output targets .NET Framework 4.x, which ships with Windows 10/11.

`bin\ExtraLifeLauncher.exe --screenshot <dir> [--scale 1.5]` renders every page to PNG and exits, without installing anything.

## Publishing a launcher update

1. Bump `AssemblyVersion` and `AssemblyFileVersion` in `src/AssemblyInfo.cs`.
2. Commit and push.
3. Run `.\tools\publish-launcher.ps1 -Notes "What changed"`.

The script builds the exe and the installer, then creates release `v<version>` here with both attached (plus the legacy-named copies). Installed launchers update on their next start, and the installer link above now serves the new version.

## Art

- `assets/studio/logo-source.png` is the Extra Life Interactive logo; `mark-source.png` is the heart cut from it as a rounded tile. `tools/make-icon.ps1` turns that into `assets/studio/mark.png` (sidebar) and `assets/icon.ico` (exe and installer icon). Rerun it after replacing the mark.
- `assets/whohaunts/` and `assets/silo/` hold each game's hero logo (`logo-full.png`, transparent), sidebar icon (`icon.png`) and source art.
- `assets/wizard-*.bmp` are the installer's side and header images.

## Publishing game builds

The Who Haunts repo has `Tools/publish-release.ps1` and the **Who Haunts > Build Windows Release** menu item. Together they build the game, zip it and create the GitHub release.

## Renames

`src/Migration.cs` runs on every start and carries older installs over. From "Ghost Hunter": it moves `%LOCALAPPDATA%\GhostHunter` to `WhoHaunts` (no re-download) and copies the game's saved settings from the `Ghost Hunter` registry key to `Who Haunts` (Unity keys PlayerPrefs by product name). From the Ghost Hunter and Who Haunts launchers: it renames the Start menu / desktop shortcuts and the Apps & features entry to Extra Life Launcher. Each step only runs while there's something old to carry over.
