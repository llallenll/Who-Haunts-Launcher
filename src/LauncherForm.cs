using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GhostHunterLauncher
{
    class LauncherForm : Form, IBackdrop
    {
        enum Mode { Busy, Install, Update, Play, Retry, Downloading }

        // Logical (96 DPI) window size; everything else is laid out relative to it.
        const int BaseWidth = 1060, BaseHeight = 640, SidebarWidth = 228, TitleBarHeight = 40;

        readonly Sidebar sidebar = new Sidebar();
        readonly TitleBar titleBar = new TitleBar();
        readonly PlayPage playPage = new PlayPage();
        readonly NotesPage notesPage = new NotesPage();
        readonly SettingsPage settingsPage = new SettingsPage();
        readonly Page[] pages;
        readonly PlayBar bar;
        readonly FlatButton actionButton;

        Mode mode = Mode.Busy;
        ReleaseInfo latest;
        CancellationTokenSource downloadCts;
        bool fixedScale; // --scale given (screenshots): ignore the monitor's DPI

        public LauncherForm()
        {
            pages = new Page[] { playPage, notesPage, settingsPage };
            bar = playPage.Bar;
            actionButton = bar.ActionButton;

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None; // scaling is done by Ui.S()
            Text = "Who Haunts Launcher";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            if (Resources.AppIcon != null) Icon = Resources.AppIcon;

            var scaleArg = ArgValue("--scale");
            float forced;
            if (scaleArg != null && float.TryParse(scaleArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out forced) && forced >= 0.5f && forced <= 4f)
            {
                fixedScale = true;
                Ui.SetScale(forced);
            }
            else
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) ApplyDpi((int)g.DpiX, false);
            }

            titleBar.MinimizeButton.Click += (s, e) => WindowState = FormWindowState.Minimized;
            titleBar.CloseButton.Click += (s, e) => Close();
            sidebar.SelectedChanged += ShowPage;
            sidebar.Footer = "Launcher v" + LauncherUpdater.CurrentVersion;

            actionButton.Click += async (s, e) => await OnActionAsync();
            playPage.NotesLink.Activated += () => sidebar.Selected = 1;
            notesPage.GitHubLink.Activated += () => Ui.OpenUrl(notesPage.GitHubLink.Tag as string);
            settingsPage.OpenFolderButton.Click += (s, e) => OpenInstallFolder();
            settingsPage.RepairButton.Click += async (s, e) => await RepairAsync();
            settingsPage.CheckButton.Click += async (s, e) => { if (mode != Mode.Downloading && mode != Mode.Busy) await CheckForUpdatesAsync(); };
            settingsPage.GameReleasesLink.Activated += () => Ui.OpenUrl(settingsPage.GameReleasesLink.Tag as string);
            settingsPage.LauncherReleasesLink.Activated += () => Ui.OpenUrl(settingsPage.LauncherReleasesLink.Tag as string);
            settingsPage.InstallPath = Installer.GameDir;

            // Title bar first: index 0 is the top of the z-order, so it sits over the pages.
            Controls.Add(titleBar);
            Controls.Add(sidebar);
            foreach (var p in pages) Controls.Add(p);
            LayoutWindow();
            int startPage;
            ShowPage(int.TryParse(ArgValue("--page"), out startPage) && startPage >= 0 && startPage < pages.Length ? startPage : 0);
            ActiveControl = actionButton;
            ResumeLayout(false);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000 | 0x00080000; // WS_MINIMIZEBOX | WS_SYSMENU: taskbar click minimizes/restores, Alt+Space works
                cp.ClassStyle |= 0x00020000;          // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleWindow(Handle);
            if (!fixedScale)
            {
                var dpi = Native.WindowDpi(Handle);
                if (dpi > 0) ApplyDpi(dpi, true);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED && !fixedScale)
            {
                // Per-monitor V2: moved to a monitor with another scale. Re-layout ourselves (WinForms on .NET Framework won't).
                var dpi = (int)((long)m.WParam & 0xFFFF);
                var suggested = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                ApplyDpi(dpi, true, new Point(suggested.Left, suggested.Top));
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        void ApplyDpi(int dpi, bool relayout, Point? location = null)
        {
            var scale = Math.Max(96, dpi) / 96f;
            // Keep the window on screen on small displays.
            var area = location.HasValue ? Screen.FromPoint(location.Value).WorkingArea : Screen.PrimaryScreen.WorkingArea;
            if (IsHandleCreated && !location.HasValue) area = Screen.FromHandle(Handle).WorkingArea;
            var fit = Math.Min(area.Width * 0.96f / BaseWidth, area.Height * 0.96f / BaseHeight);
            scale = Math.Max(0.75f, Math.Min(scale, fit));
            if (Math.Abs(scale - Ui.Scale) < 0.001f && !location.HasValue) return;
            Ui.SetScale(scale);
            if (!relayout) return;
            SuspendLayout();
            var center = new Point(Left + Width / 2, Top + Height / 2);
            LayoutWindow();
            if (location.HasValue) Location = location.Value;
            else if (Visible) Location = new Point(center.X - Width / 2, center.Y - Height / 2);
            foreach (var p in pages) p.Rescale();
            titleBar.PerformLayout();
            ResumeLayout(true);
            Invalidate(true);
        }

        void LayoutWindow()
        {
            ClientSize = new Size(Ui.S(BaseWidth), Ui.S(BaseHeight));
            var side = Ui.S(SidebarWidth);
            sidebar.SetBounds(0, 0, side, ClientSize.Height);
            titleBar.SetBounds(side, 0, ClientSize.Width - side, Ui.S(TitleBarHeight));
            foreach (var p in pages) p.SetBounds(side, 0, ClientSize.Width - side, ClientSize.Height);
            titleBar.PerformLayout();
        }

        void ShowPage(int index)
        {
            for (var i = 0; i < pages.Length; i++) pages[i].Visible = i == index;
            if (sidebar.Selected != index) sidebar.Selected = index;
            titleBar.Invalidate(true);
        }

        Page CurrentPage
        {
            get
            {
                foreach (var p in pages) if (p.Visible) return p;
                return playPage;
            }
        }

        public void PaintBackdrop(Graphics g, Rectangle rect)
        {
            var page = CurrentPage;
            var state = g.Save();
            g.TranslateTransform(page.Left, page.Top);
            var r = rect;
            r.Offset(-page.Left, -page.Top);
            page.PaintBackdrop(g, r);
            g.Restore(state);
        }

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            var screenshotDir = ArgValue("--screenshot");
            if (screenshotDir != null)
            {
                await CaptureScreenshotsAsync(screenshotDir);
                return;
            }

            Installer.EnsureRoot();
            Installer.CleanupLeftovers();

            SetMode(Mode.Busy, "Checking for launcher updates…");
            await LauncherUpdater.CleanupAsync();
            if (await LauncherUpdater.TryUpdateAsync(status => bar.SetStatus(status, false)))
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
            ShowNotes(latest, error);
            UpdateVersionLabel(installed);

            if (latest == null)
            {
                if (installed != null)
                    SetMode(Mode.Play, error != null ? "Couldn't check for updates — you can still play." : "No builds published yet.", error != null);
                else
                    SetMode(Mode.Retry, error ?? "No builds have been published yet. Check back soon!", error != null);
                return;
            }

            if (installed == null)
                SetMode(Mode.Install, "Ready to install " + latest.Tag + " (" + FormatBytes(latest.AssetSize) + ")");
            else if (installed != latest.Tag)
                SetMode(Mode.Update, "Update available: " + installed + " → " + latest.Tag + " (" + FormatBytes(latest.AssetSize) + ")");
            else
                SetMode(Mode.Play, "Up to date — ready to play");
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
            SetProgress(0);
            ShowPage(0);

            var download = Installer.DownloadFile;
            try
            {
                var dlProgress = new Progress<DownloadProgress>(p =>
                {
                    SetProgress(p.Total > 0 ? (double)p.Received / p.Total : 0);
                    var text = FormatBytes(p.Received) + " / " + FormatBytes(p.Total);
                    if (p.BytesPerSecond > 0)
                    {
                        text += "    ·    " + FormatBytes((long)p.BytesPerSecond) + "/s";
                        if (p.Total > p.Received) text += "    ·    " + FormatEta((p.Total - p.Received) / p.BytesPerSecond) + " left";
                    }
                    bar.SetStatus("Downloading " + latest.Tag + "…", false);
                    bar.Detail = text;
                });
                await GitHubReleases.DownloadAsync(latest, download, dlProgress, downloadCts.Token);

                SetMode(Mode.Busy, "Installing " + latest.Tag + "…");
                SetProgress(0);
                var tag = latest.Tag;
                var installProgress = new Progress<double>(v =>
                {
                    SetProgress(v);
                    bar.SetStatus("Installing " + tag + "…", false);
                });
                await Task.Run(() => Installer.Install(download, tag, installProgress));

                UpdateVersionLabel(Installer.InstalledTag);
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

        async Task RepairAsync()
        {
            if (mode == Mode.Busy || mode == Mode.Downloading) return;
            if (latest == null)
            {
                SetMode(mode, "Can't repair while offline.", true);
                return;
            }
            if (ConfirmDialog.Ask(this, "Repair Who Haunts", "Re-download and reinstall " + latest.Tag + "?\n\nYour saves and settings are kept.", "Repair", "Cancel"))
                await InstallAsync();
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

        bool installing; // Busy with a progress bar = extracting; can't be cancelled

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (mode == Mode.Downloading || (mode == Mode.Busy && installing))
            {
                var stop = ConfirmDialog.Ask(this, "Quit the launcher?", "An install is in progress. Cancel it and quit?", "Cancel install", "Keep going", true);
                if (!stop || mode == Mode.Busy) { e.Cancel = true; return; }
                if (downloadCts != null) downloadCts.Cancel();
            }
            base.OnFormClosing(e);
        }

        void SetProgress(double value)
        {
            installing = mode == Mode.Busy;
            bar.Progress = value;
            sidebar.Progress = value;
        }

        void SetMode(Mode newMode, string status, bool isError = false)
        {
            mode = newMode;
            bar.SetStatus(status, isError);
            bar.Detail = null;
            bar.Progress = -1;
            sidebar.Progress = -1;
            installing = false;

            actionButton.Kind = newMode == Mode.Downloading ? ButtonKind.Secondary : ButtonKind.Primary;
            switch (newMode)
            {
                case Mode.Busy: actionButton.Text = "PLEASE WAIT"; actionButton.Icon = null; break;
                case Mode.Install: actionButton.Text = "INSTALL"; actionButton.Icon = Glyph.Download; break;
                case Mode.Update: actionButton.Text = "UPDATE"; actionButton.Icon = Glyph.Download; break;
                case Mode.Play: actionButton.Text = "PLAY"; actionButton.Icon = Glyph.Play; break;
                case Mode.Retry: actionButton.Text = "RETRY"; actionButton.Icon = Glyph.Refresh; break;
                case Mode.Downloading: actionButton.Text = "CANCEL"; actionButton.Icon = Glyph.Cancel; break;
            }
            actionButton.Enabled = newMode != Mode.Busy;

            var idle = newMode != Mode.Busy && newMode != Mode.Downloading;
            settingsPage.RepairButton.Enabled = idle && Installer.InstalledTag != null && latest != null;
            settingsPage.CheckButton.Enabled = idle;
            settingsPage.UpdatesText = newMode == Mode.Busy ? status : UpdatesSummary();
            settingsPage.Invalidate();
        }

        string UpdatesSummary()
        {
            if (latest == null) return "Couldn't reach the update server.";
            var text = "Latest build: " + latest.Tag;
            if (latest.PublishedAt != DateTime.MinValue) text += "  ·  released " + latest.PublishedAt.ToLocalTime().ToString("MMMM d, yyyy");
            var installed = Installer.InstalledTag;
            if (installed != null) text += installed == latest.Tag ? "  ·  you're up to date" : "  ·  update available";
            return text;
        }

        void UpdateVersionLabel(string installed)
        {
            var text = "Installed " + (installed ?? "—");
            if (latest != null) text += "    ·    Latest " + latest.Tag;
            text += "    ·    Launcher v" + LauncherUpdater.CurrentVersion;
            bar.Versions = text;
            settingsPage.InstalledText = installed != null ? "Installed version: " + installed : "Not installed yet";
            settingsPage.Invalidate();
        }

        void ShowNotes(ReleaseInfo release, string error)
        {
            playPage.SetRelease(release, error);
            notesPage.SetRelease(release, error);
            titleBar.Invalidate(true);
        }

        /// <summary>
        /// --screenshot DIR [--scale N]: renders each page to PNG and exits. Read-only: no self-update, no install,
        /// nothing written except the PNGs. Used to review the UI without clicking through it.
        /// </summary>
        async Task CaptureScreenshotsAsync(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                await CheckForUpdatesAsync();
               var suffix = fixedScale ? "-" + (int)Math.Round(Ui.Scale * 100) : "";
                string[] names = { "play", "notes", "settings" };
                for (var i = 0; i < pages.Length; i++)
                {
                    ShowPage(i);
                    SaveScreenshot(this, Path.Combine(dir, names[i] + suffix + ".png"));
                }
                ShowPage(1);
                notesPage.View.ScrollToEnd();
                SaveScreenshot(this, Path.Combine(dir, "notes-end" + suffix + ".png"));

                // The download state, faked for the picture only (no download is started).
                ShowPage(0);
                SetMode(Mode.Downloading, "Downloading v1.4.2-early-access…");
                bar.Detail = "412.3 MB / 980.0 MB    ·    18.4 MB/s    ·    31s left";
                SetProgress(0.42);
                SaveScreenshot(this, Path.Combine(dir, "play-downloading" + suffix + ".png"));
                SetMode(Mode.Retry, "Couldn't reach GitHub: The remote name could not be resolved.", true);
                SaveScreenshot(this, Path.Combine(dir, "play-error" + suffix + ".png"));
                SetMode(Mode.Busy, "Done"); // never leave the fake download state, or closing would ask to cancel it

                using (var dialog = new ConfirmDialog("Repair Who Haunts", "Re-download and reinstall v1.4.2?\n\nYour saves and settings are kept.", "Repair", "Cancel"))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(Left + (Width - dialog.Width) / 2, Top + (Height - dialog.Height) / 2);
                    dialog.Show(this);
                    dialog.Refresh();
                    SaveScreenshot(dialog, Path.Combine(dir, "confirm" + suffix + ".png"));
                    dialog.Close();
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(dir, "screenshot-error.txt"), ex.ToString()); } catch { }
            }
            Close();
        }

        static void SaveScreenshot(Control control, string path)
        {
            control.Refresh();
            Application.DoEvents();
            // 32bppRgb: GDI text and fills leave alpha at 0, which an ARGB bitmap would treat as transparent.
            using (var bmp = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppRgb))
            {
                control.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(path, ImageFormat.Png);
            }
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
