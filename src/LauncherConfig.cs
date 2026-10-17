using System;
using System.IO;

namespace GhostHunterLauncher
{
    /// <summary>Everything a tester's launcher needs to know about where builds live.</summary>
    static class LauncherConfig
    {
        // Public GitHub repo whose Releases hold the game builds. Must be public: the launcher has no login.
        public const string RepoOwner = "llallenll";
        public const string RepoName = "Ghost-Hunter-Releases";

        // Zip asset attached to each release (see Tools/publish-release.ps1). Falls back to any .zip.
        public const string AssetName = "WhoHaunts-Windows.zip";

        // Executable inside the zip (Unity names it after Player Settings > Product Name).
        public const string GameExeName = "Who Haunts.exe";

        // Early access builds are usually marked "pre-release" on GitHub; count them as updates.
        public const bool IncludePrereleases = true;

        // Public repo whose Releases hold new launcher builds (see tools/publish-launcher.ps1).
        // Tags are the launcher version (v1.0.1); the launcher replaces itself when a newer one is out.
        public const string LauncherRepoOwner = "llallenll";
        public const string LauncherRepoName = "Ghost-Hunter-Launcher";
        // Releases also carry a copy named GhostHunterLauncher.exe, the name launchers before the rename look for.
        public const string LauncherAssetName = "WhoHauntsLauncher.exe";

        public const string Title = "WHO HAUNTS";
        public const string Subtitle = "EARLY ACCESS";

        // %LOCALAPPDATA%\WhoHaunts — per-user, no admin rights needed. (Was GhostHunter; see Migration.)
        public static readonly string InstallRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhoHaunts");
    }
}
