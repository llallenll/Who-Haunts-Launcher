using System;
using System.Drawing;
using System.IO;

namespace ExtraLifeLauncher
{
    /// <summary>Launcher-wide settings: the studio, and where launcher updates come from.</summary>
    static class LauncherConfig
    {
        public const string StudioName = "Extra Life Interactive";
        public const string ProductName = "Extra Life Launcher";

        // Public repo whose Releases hold new launcher builds (see tools/publish-launcher.ps1).
        // Tags are the launcher version (v2.0.0); the launcher replaces itself when a newer one is out.
        public const string LauncherRepoOwner = "llallenll";
        public const string LauncherRepoName = "Ghost-Hunter-Launcher";
        // Releases also carry copies named WhoHauntsLauncher.exe and GhostHunterLauncher.exe, the names older launchers look for.
        public const string LauncherAssetName = "ExtraLifeLauncher.exe";

        static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        /// <summary>Every game the launcher offers, in sidebar order.</summary>
        public static readonly GameInfo[] Games =
        {
            new GameInfo
            {
                Id = "whohaunts",
                Name = "Who Haunts",
                Stage = "EARLY ACCESS",
                // Must be public: the launcher has no login.
                RepoOwner = "llallenll",
                RepoName = "Ghost-Hunter-Releases",
                AssetName = "WhoHaunts-Windows.zip",
                ExeName = "Who Haunts.exe",
                // %LOCALAPPDATA%\WhoHaunts, where the single-game launcher put it. (Was GhostHunter; see Migration.)
                InstallRoot = Path.Combine(LocalAppData, "WhoHaunts"),
                Accent = Color.FromArgb(198, 36, 28),
                AccentSoft = Color.FromArgb(240, 104, 88),
                Glow = Color.FromArgb(170, 22, 14),
                Logo = Resources.Load("whohaunts.logo-full.png"),
                Icon = Resources.Load("whohaunts.icon.png"),
                Backdrop = Resources.Load("whohaunts.paper.png"),
            },
            new GameInfo
            {
                Id = "thesilo",
                Name = "The Silo",
                Stage = "COMING SOON",
                // Not on GitHub yet. Set RepoOwner/RepoName to the public releases repo once the first build is up.
                RepoOwner = null,
                RepoName = null,
                AssetName = "TheSilo-Windows.zip",
                ExeName = "The Silo.exe",
                InstallRoot = Path.Combine(LocalAppData, "ExtraLifeInteractive", "TheSilo"),
                Accent = Color.FromArgb(232, 160, 40),
                AccentSoft = Color.FromArgb(246, 192, 96),
                Glow = Color.FromArgb(200, 120, 20),
                Logo = Resources.Load("silo.logo-full.png"),
                Icon = Resources.Load("silo.icon.png"),
            },
        };
    }

    /// <summary>One game in the launcher: where its builds come from, where it installs, and its art.</summary>
    class GameInfo
    {
        public string Id;
        public string Name;
        public string Stage;   // small caps label under the name ("EARLY ACCESS")

        // Public GitHub repo whose Releases hold the game builds; null while the game isn't published yet.
        public string RepoOwner;
        public string RepoName;
        // Zip asset attached to each release. Falls back to any .zip.
        public string AssetName;
        // Executable inside the zip (Unity names it after Player Settings > Product Name).
        public string ExeName;
        // Early access builds are usually marked "pre-release" on GitHub; count them as updates.
        public bool IncludePrereleases = true;

        public string InstallRoot;

        public Color Accent;     // buttons, progress and highlights on the game's pages
        public Color AccentSoft; // the accent as text on dark
        public Color Glow;     // the light behind its logo
        public Image Logo;     // hero logo, transparent
        public Image Icon;     // small square/round icon for the sidebar
        public Image Backdrop; // optional texture behind the hero

        public bool IsPublished { get { return RepoOwner != null && RepoName != null; } }
        public string ReleasesUrl { get { return IsPublished ? "https://github.com/" + RepoOwner + "/" + RepoName + "/releases" : null; } }
    }
}
