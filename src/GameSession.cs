using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ExtraLifeLauncher
{
    enum Mode { Busy, Install, Update, Play, Retry, Downloading, ComingSoon }

    /// <summary>
    /// One game's state in the launcher: the newest release, what's installed, and the install/update/play flow.
    /// The pages only draw it; every change raises <see cref="Changed"/>.
    /// </summary>
    class GameSession
    {
        public readonly GameInfo Game;
        public readonly Installer Installer;
        public event Action Changed;

        public Mode Mode { get; private set; }
        public ReleaseInfo Latest { get; private set; }
        public string Error { get; private set; }     // why the release check failed, for the notes
        public string Status { get; private set; }
        public bool IsError { get; private set; }
        public string Detail { get; private set; }    // download size, speed and time left
        public double Progress { get; private set; }  // 0..1, negative when there's no bar
        public bool Checked { get; private set; }     // the first release check has finished
        bool installing;                              // Busy with a progress bar = extracting; can't be cancelled
        CancellationTokenSource downloadCts;

        public GameSession(GameInfo game)
        {
            Game = game;
            Installer = new Installer(game);
            Progress = -1;
            if (game.IsPublished) SetMode(Mode.Busy, "Checking for updates…");
            else SetMode(Mode.ComingSoon, "Coming soon from " + LauncherConfig.StudioName);
        }

        public string InstalledTag { get { return Installer.InstalledTag; } }

        /// <summary>Downloading or extracting: closing the launcher now would interrupt it.</summary>
        public bool IsWorking { get { return Mode == Mode.Downloading || IsExtracting; } }
        public bool IsExtracting { get { return Mode == Mode.Busy && installing; } }
        public bool IsIdle { get { return Mode != Mode.Busy && Mode != Mode.Downloading; } }
        public bool CanRepair { get { return IsIdle && Latest != null && InstalledTag != null; } }

        public void PrepareFolder()
        {
            if (!Game.IsPublished && !Directory.Exists(Game.InstallRoot)) return; // nothing to tidy, and no folder for an unreleased game
            Installer.EnsureRoot();
            Installer.CleanupLeftovers();
        }

        public async Task CheckForUpdatesAsync()
        {
            if (!Game.IsPublished)
            {
                Checked = true;
                SetMode(Mode.ComingSoon, "Coming soon from " + LauncherConfig.StudioName);
                return;
            }
            SetMode(Mode.Busy, "Checking for updates…");
            string error = null;
            ReleaseInfo latest;
            try { latest = await GitHubReleases.GetLatestGameAsync(Game); }
            catch (LauncherException ex) { latest = null; error = ex.Message; }
            catch (Exception ex) { latest = null; error = "Update check failed: " + ex.Message; }
            Latest = latest;
            Error = error;
            Checked = true;

            var installed = InstalledTag;
            if (latest == null)
            {
                if (installed != null)
                    SetMode(Mode.Play, error != null ? "Couldn't check for updates — you can still play." : "No builds published yet.", error != null);
                else
                    SetMode(Mode.Retry, error ?? "No builds have been published yet. Check back soon!", error != null);
                return;
            }

            if (installed == null)
                SetMode(Mode.Install, "Ready to install " + latest.Tag + " (" + Format.Bytes(latest.AssetSize) + ")");
            else if (installed != latest.Tag)
                SetMode(Mode.Update, "Update available: " + installed + " → " + latest.Tag + " (" + Format.Bytes(latest.AssetSize) + ")");
            else
                SetMode(Mode.Play, "Up to date — ready to play");
        }

        /// <summary>What the big button does in the current mode. Returns true when the game was started.</summary>
        public async Task<bool> ActAsync()
        {
            switch (Mode)
            {
                case Mode.Install:
                case Mode.Update: await InstallAsync(); break;
                case Mode.Play: return Launch();
                case Mode.Retry: await CheckForUpdatesAsync(); break;
                case Mode.Downloading: CancelDownload(); break;
            }
            return false;
        }

        public void CancelDownload()
        {
            if (downloadCts != null) downloadCts.Cancel();
        }

        public async Task InstallAsync()
        {
            if (Latest == null) return;
            if (Installer.IsGameRunning())
            {
                SetMode(Mode, "Close " + Game.Name + " before updating.", true);
                return;
            }
            if (!HasRoomFor(Latest.AssetSize))
            {
                SetMode(Mode, "Not enough disk space (need about " + Format.Bytes(Latest.AssetSize * 3) + " free).", true);
                return;
            }

            var previousMode = Mode;
            var release = Latest;
            downloadCts = new CancellationTokenSource();
            SetMode(Mode.Downloading, "Starting download…");
            SetProgress(0);

            Installer.EnsureRoot();
            var download = Installer.DownloadFile;
            try
            {
                var dlProgress = new Progress<DownloadProgress>(p =>
                {
                    var text = Format.Bytes(p.Received) + " / " + Format.Bytes(p.Total);
                    if (p.BytesPerSecond > 0)
                    {
                        text += "    ·    " + Format.Bytes((long)p.BytesPerSecond) + "/s";
                        if (p.Total > p.Received) text += "    ·    " + Format.Eta((p.Total - p.Received) / p.BytesPerSecond) + " left";
                    }
                    Status = "Downloading " + release.Tag + "…";
                    IsError = false;
                    Detail = text;
                    SetProgress(p.Total > 0 ? (double)p.Received / p.Total : 0);
                });
                await GitHubReleases.DownloadAsync(release, download, dlProgress, downloadCts.Token);

                SetMode(Mode.Busy, "Installing " + release.Tag + "…");
                SetProgress(0);
                var tag = release.Tag;
                var installProgress = new Progress<double>(SetProgress);
                await Task.Run(() => Installer.Install(download, tag, installProgress));

                SetMode(Mode.Play, "Installed " + tag + " — ready to play");
            }
            catch (OperationCanceledException)
            {
                SetMode(previousMode, "Download cancelled.");
            }
            catch (LauncherException ex)
            {
                SetMode(previousMode, ex.Message, true);
            }
            catch (Exception ex)
            {
                SetMode(previousMode, "Install failed: " + ex.Message, true);
            }
            finally
            {
                Installer.TryDelete(download);
                downloadCts.Dispose();
                downloadCts = null;
            }
        }

        bool Launch()
        {
            var exe = Installer.FindGameExe();
            if (exe == null)
            {
                SetMode(Latest != null ? Mode.Install : Mode.Retry, "The game files are missing. Please reinstall.", true);
                return false;
            }
            try
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                SetMode(Mode.Play, "Couldn't start the game: " + ex.Message, true);
                return false;
            }
        }

        /// <summary>Holds the button while the launcher does something else first (its own update check).</summary>
        public void SetBusy(string status) { SetMode(Mode.Busy, status); }

        /// <summary>Shows an error in the status line without changing what the button does.</summary>
        public void ShowError(string message) { SetMode(Mode, message, true); }

        /// <summary>Fakes a state for --screenshot (no download is started).</summary>
        public void Fake(Mode mode, string status, bool error, double progress = -1, string detail = null)
        {
            SetMode(mode, status, error);
            Detail = detail ?? "";
            Progress = progress;
            installing = false;
            Raise();
        }

        void SetProgress(double value)
        {
            installing = Mode == Mode.Busy;
            Progress = value;
            Raise();
        }

        void SetMode(Mode mode, string status, bool isError = false)
        {
            Mode = mode;
            Status = status ?? "";
            IsError = isError;
            Detail = "";
            Progress = -1;
            installing = false;
            Raise();
        }

        void Raise()
        {
            if (Changed != null) Changed();
        }

        /// <summary>The status, short enough for a Library card.</summary>
        public string ShortStatus
        {
            get
            {
                if (IsError) return Status;
                switch (Mode)
                {
                    case Mode.ComingSoon: return "Coming soon";
                    case Mode.Install: return "Not installed  ·  " + Format.Bytes(Latest.AssetSize);
                    case Mode.Update: return "Update available  ·  " + Format.Bytes(Latest.AssetSize);
                    case Mode.Play: return InstalledTag != null ? "Installed " + InstalledTag : Status;
                    case Mode.Downloading: return string.IsNullOrEmpty(Detail) ? Status : "Downloading  ·  " + Detail.Split(new[] { "    ·    " }, StringSplitOptions.None)[0];
                    default: return Status;
                }
            }
        }

        /// <summary>One line for the Settings page about the newest build.</summary>
        public string UpdatesSummary()
        {
            if (!Game.IsPublished) return "Not released yet";
            if (!Checked) return "Checking for updates…";
            if (Latest == null) return Error != null ? "Couldn't reach the update server." : "No builds published yet";
            var text = "Latest build: " + Latest.Tag;
            if (Latest.PublishedAt != DateTime.MinValue) text += "  ·  released " + Latest.PublishedAt.ToLocalTime().ToString("MMMM d, yyyy");
            var installed = InstalledTag;
            if (installed != null) text += installed == Latest.Tag ? "  ·  up to date" : "  ·  update available";
            return text;
        }

        public string VersionsLine()
        {
            if (!Game.IsPublished) return "Launcher v" + LauncherUpdater.CurrentVersion;
            var text = "Installed " + (InstalledTag ?? "—");
            if (Latest != null) text += "    ·    Latest " + Latest.Tag;
            return text + "    ·    Launcher v" + LauncherUpdater.CurrentVersion;
        }

        bool HasRoomFor(long assetSize)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Game.InstallRoot));
                return drive.AvailableFreeSpace > assetSize * 3; // zip + extracted copy + headroom
            }
            catch { return true; }
        }
    }

    static class Format
    {
        public static string Bytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.00") + " GB";
            return (bytes / (double)(1L << 20)).ToString("0.0") + " MB";
        }

        public static string Eta(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(1, seconds));
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + "m " + t.Seconds + "s";
            return t.Seconds + "s";
        }

        public static string Date(DateTime d) { return d.ToLocalTime().ToString("MMMM d, yyyy"); }
    }
}
