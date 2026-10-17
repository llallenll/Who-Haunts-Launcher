using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GhostHunterLauncher
{
    class LauncherForm : Form
    {
        enum Mode { Busy, Install, Update, Play, Retry, Downloading }

        readonly HeaderPanel header = new HeaderPanel();
        readonly Label notesTitle = new Label();
        readonly TextBox notesBox = new TextBox();
        readonly Panel bottomBar = new Panel();
        readonly Label statusLabel = new Label();
        readonly Label versionLabel = new Label();
        readonly SlimProgressBar progressBar = new SlimProgressBar();
        readonly ActionButton actionButton = new ActionButton();
        readonly LinkButton openFolderLink = new LinkButton("Open install folder");
        readonly LinkButton repairLink = new LinkButton("Repair");
        readonly LinkButton refreshLink = new LinkButton("Check for updates");

        Mode mode = Mode.Busy;
        ReleaseInfo latest;
        CancellationTokenSource downloadCts;

        public LauncherForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Who Haunts Launcher";
            ClientSize = new Size(900, 560);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            if (Resources.Paper != null)
            {
                BackgroundImage = Resources.Paper;
                BackgroundImageLayout = ImageLayout.Stretch;
            }
            Font = new Font("Segoe UI", 9.5f);
            if (Resources.AppIcon != null) Icon = Resources.AppIcon;

            header.SetBounds(0, 0, 900, 120);

            notesTitle.SetBounds(32, 132, 836, 22);
            notesTitle.Font = new Font("Georgia", 9.5f, FontStyle.Bold);
            notesTitle.ForeColor = Theme.RedInk;
            notesTitle.BackColor = Color.Transparent;
            notesTitle.Text = "PATCH NOTES";

            var notesFrame = new Panel { BackColor = Theme.Panel, Padding = new Padding(14, 12, 6, 12) };
            notesFrame.SetBounds(32, 158, 836, 262);
            notesBox.Dock = DockStyle.Fill;
            notesBox.Multiline = true;
            notesBox.ReadOnly = true;
            notesBox.ScrollBars = ScrollBars.Vertical;
            notesBox.BorderStyle = BorderStyle.None;
            notesBox.BackColor = Theme.Panel;
            notesBox.ForeColor = Theme.Ink;
            notesBox.Font = new Font("Georgia", 10f);
            notesBox.TabStop = false;
            notesFrame.Controls.Add(notesBox);

            openFolderLink.Location = new Point(32, 428);
            openFolderLink.Click += (s, e) => OpenInstallFolder();
            repairLink.Location = new Point(170, 428);
            repairLink.Click += async (s, e) => await RepairAsync();
            refreshLink.Location = new Point(230, 428);
            refreshLink.Click += async (s, e) => { if (mode != Mode.Downloading && mode != Mode.Busy) await CheckForUpdatesAsync(); };

            bottomBar.SetBounds(0, 460, 900, 100);
            bottomBar.BackColor = Theme.Bar;

            statusLabel.SetBounds(32, 22, 600, 22);
            statusLabel.Font = new Font("Georgia", 10.5f, FontStyle.Bold);
            statusLabel.AutoEllipsis = true;
            progressBar.SetBounds(32, 50, 600, 6);
            progressBar.Visible = false;
            versionLabel.SetBounds(32, 64, 600, 20);
            versionLabel.ForeColor = Theme.Muted;
            versionLabel.Font = new Font("Segoe UI", 8.5f);

            actionButton.SetBounds(668, 24, 200, 52);
            actionButton.Font = new Font("Georgia", 14f, FontStyle.Bold);
            actionButton.Click += async (s, e) => await OnActionAsync();

            bottomBar.Controls.AddRange(new Control[] { statusLabel, progressBar, versionLabel, actionButton });
            Controls.AddRange(new Control[] { header, notesTitle, notesFrame, openFolderLink, repairLink, refreshLink, bottomBar });
            ActiveControl = actionButton; // otherwise the notes box takes focus and selects all its text
            ResumeLayout(false);
            PerformLayout();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Installer.EnsureRoot();
            Installer.CleanupLeftovers();
            versionLabel.Text = "Launcher v" + LauncherUpdater.CurrentVersion;

            SetMode(Mode.Busy, "Checking for launcher updates…");
            await LauncherUpdater.CleanupAsync();
            if (await LauncherUpdater.TryUpdateAsync(status => statusLabel.Text = status))
            {
                Close(); // the new launcher is already starting
                return;
            }
            await CheckForUpdatesAsync();
        }

        async Task CheckForUpdatesAsync()
        {
            SetMode(Mode.Busy, "Checking for updates…");
            string error = null;
            try { latest = await GitHubReleases.GetLatestGameAsync(); }
            catch (LauncherException ex) { latest = null; error = ex.Message; }
            catch (Exception ex) { latest = null; error = "Update check failed: " + ex.Message; }

            var installed = Installer.InstalledTag;
            ShowNotes(latest);
            UpdateVersionLabel(installed);

            if (latest == null)
            {
                if (installed != null)
                    SetMode(Mode.Play, error != null ? "Couldn't check for updates — you can still play." : "No builds published yet.", error != null);
                else
                    SetMode(Mode.Retry, error ?? "No builds have been published yet. Check back soon!", error != null);
                if (error != null) { notesBox.Text = error; notesBox.Select(0, 0); }
                return;
            }

            if (installed == null)
                SetMode(Mode.Install, "Ready to install " + latest.Tag + " (" + FormatBytes(latest.AssetSize) + ")");
            else if (installed != latest.Tag)
                SetMode(Mode.Update, "Update available: " + installed + " → " + latest.Tag + " (" + FormatBytes(latest.AssetSize) + ")");
            else
                SetMode(Mode.Play, "Up to date — ready to play.");
        }

        async Task OnActionAsync()
        {
            switch (mode)
            {
                case Mode.Install:
                case Mode.Update: await InstallAsync(); break;
                case Mode.Play: LaunchGame(); break;
                case Mode.Retry: await CheckForUpdatesAsync(); break;
                case Mode.Downloading: if (downloadCts != null) downloadCts.Cancel(); break;
            }
        }

        async Task InstallAsync()
        {
            if (latest == null) return;
            if (Installer.IsGameRunning())
            {
                SetMode(mode, "Close Who Haunts before updating.", true);
                return;
            }
            if (!HasRoomFor(latest.AssetSize))
            {
                SetMode(mode, "Not enough disk space (need about " + FormatBytes(latest.AssetSize * 3) + " free).", true);
                return;
            }

            var previousMode = mode;
            downloadCts = new CancellationTokenSource();
            SetMode(Mode.Downloading, "Starting download…");
            progressBar.Visible = true;
            progressBar.Value = 0;

            var download = Installer.DownloadFile;
            try
            {
                var dlProgress = new Progress<DownloadProgress>(p =>
                {
                    progressBar.Value = p.Total > 0 ? (double)p.Received / p.Total : 0;
                    var text = "Downloading " + latest.Tag + " — " + FormatBytes(p.Received) + " / " + FormatBytes(p.Total);
                    if (p.BytesPerSecond > 0)
                    {
                        text += "  ·  " + FormatBytes((long)p.BytesPerSecond) + "/s";
                        if (p.Total > p.Received) text += "  ·  " + FormatEta((p.Total - p.Received) / p.BytesPerSecond) + " left";
                    }
                    statusLabel.Text = text;
                });
                await GitHubReleases.DownloadAsync(latest, download, dlProgress, downloadCts.Token);

                SetMode(Mode.Busy, "Installing " + latest.Tag + "…");
                progressBar.Visible = true;
                progressBar.Value = 0;
                var tag = latest.Tag;
                var installProgress = new Progress<double>(v =>
                {
                    progressBar.Value = v;
                    statusLabel.Text = "Installing " + tag + "… " + (int)(v * 100) + "%";
                });
                await Task.Run(() => Installer.Install(download, tag, installProgress));

                UpdateVersionLabel(Installer.InstalledTag);
                SetMode(Mode.Play, "Installed " + tag + " — ready to play.");
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

        async Task RepairAsync()
        {
            if (mode == Mode.Busy || mode == Mode.Downloading) return;
            if (latest == null)
            {
                SetMode(mode, "Can't repair while offline.", true);
                return;
            }
            var answer = MessageBox.Show(this, "Re-download and reinstall " + latest.Tag + "?\n\nYour saves and settings are kept.",
                "Repair Who Haunts", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.OK) await InstallAsync();
        }

        void LaunchGame()
        {
            var exe = Installer.FindGameExe();
            if (exe == null)
            {
                SetMode(latest != null ? Mode.Install : Mode.Retry, "The game files are missing. Please reinstall.", true);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
                Close();
            }
            catch (Exception ex)
            {
                SetMode(Mode.Play, "Couldn't start the game: " + ex.Message, true);
            }
        }

        void OpenInstallFolder()
        {
            var dir = Directory.Exists(Installer.GameDir) ? Installer.GameDir : LauncherConfig.InstallRoot;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"")); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (mode == Mode.Downloading || (mode == Mode.Busy && progressBar.Visible))
            {
                var answer = MessageBox.Show(this, "An install is in progress. Cancel it and quit?", "Who Haunts Launcher",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes || mode == Mode.Busy) { e.Cancel = true; return; }
                if (downloadCts != null) downloadCts.Cancel();
            }
            base.OnFormClosing(e);
        }

        void SetMode(Mode newMode, string status, bool isError = false)
        {
            mode = newMode;
            statusLabel.Text = status;
            statusLabel.ForeColor = isError ? Theme.Error : Theme.Text;
            progressBar.Visible = false;

            switch (newMode)
            {
                case Mode.Busy: actionButton.Text = "PLEASE WAIT"; break;
                case Mode.Install: actionButton.Text = "INSTALL"; break;
                case Mode.Update: actionButton.Text = "UPDATE"; break;
                case Mode.Play: actionButton.Text = "PLAY"; break;
                case Mode.Retry: actionButton.Text = "RETRY"; break;
                case Mode.Downloading: actionButton.Text = "CANCEL"; break;
            }
            actionButton.Enabled = newMode != Mode.Busy;

            var idle = newMode != Mode.Busy && newMode != Mode.Downloading;
            repairLink.Visible = idle && Installer.InstalledTag != null && latest != null;
            refreshLink.Location = new Point(repairLink.Visible ? repairLink.Right + 16 : repairLink.Left, refreshLink.Top);
            refreshLink.Enabled = idle;
        }

        void UpdateVersionLabel(string installed)
        {
            var text = "Installed: " + (installed ?? "none");
            if (latest != null) text += "    Latest: " + latest.Tag;
            text += "    Launcher v" + LauncherUpdater.CurrentVersion;
            versionLabel.Text = text;
        }

        void ShowNotes(ReleaseInfo release)
        {
            if (release == null)
            {
                notesTitle.Text = "PATCH NOTES";
                notesBox.Text = "";
                return;
            }
            var title = "PATCH NOTES — " + (string.IsNullOrWhiteSpace(release.Name) ? release.Tag : release.Name.ToUpperInvariant());
            if (release.PublishedAt != DateTime.MinValue) title += "  ·  " + release.PublishedAt.ToLocalTime().ToString("MMM d, yyyy");
            notesTitle.Text = title;
            notesBox.Text = string.IsNullOrWhiteSpace(release.Notes) ? "No notes for this build." : CleanMarkdown(release.Notes);
            notesBox.Select(0, 0);
        }

        /// <summary>Release notes are GitHub markdown; strip the syntax so they read well as plain text.</summary>
        static string CleanMarkdown(string markdown)
        {
            var sb = new StringBuilder();
            foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                var heading = Regex.Match(line, @"^#{1,6}\s+(.*)$");
                if (heading.Success) line = heading.Groups[1].Value.ToUpperInvariant();
                line = Regex.Replace(line, @"^(\s*)[-*+]\s+", "$1•  ");
                line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1");
                line = line.Replace("**", "").Replace("__", "").Replace("`", "");
                sb.Append(line).Append("\r\n");
            }
            return sb.ToString().TrimEnd();
        }

        static bool HasRoomFor(long assetSize)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(LauncherConfig.InstallRoot));
                return drive.AvailableFreeSpace > assetSize * 3; // zip + extracted copy + headroom
            }
            catch { return true; }
        }

        static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.00") + " GB";
            return (bytes / (double)(1L << 20)).ToString("0.0") + " MB";
        }

        static string FormatEta(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(1, seconds));
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + "m " + t.Seconds + "s";
            return t.Seconds + "s";
        }
    }
}
