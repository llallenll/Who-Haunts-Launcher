using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExtraLifeLauncher
{
    class LauncherForm : Form, IBackdrop
    {
        // Logical (96 DPI) window size; everything else is laid out relative to it.
        const int BaseWidth = 1060, BaseHeight = 640, SidebarWidth = 228, TitleBarHeight = 40;

        readonly GameSession[] sessions;
        readonly Sidebar sidebar;
        readonly TitleBar titleBar = new TitleBar();
        readonly HomePage homePage;
        readonly GamePage[] gamePages;
        readonly NotesPage[] notesPages;
        readonly SettingsPage settingsPage;
        readonly Page[] pages;
        readonly int[] gameTab; // 0 = overview, 1 = patch notes, remembered per game

        bool fixedScale; // --scale given (screenshots): ignore the monitor's DPI

        public LauncherForm()
        {
            sessions = LauncherConfig.Games.Select(g => new GameSession(g)).ToArray();
            sidebar = new Sidebar(sessions);
            homePage = new HomePage(sessions);
            gamePages = sessions.Select(s => new GamePage(s)).ToArray();
            notesPages = sessions.Select(s => new NotesPage(s)).ToArray();
            settingsPage = new SettingsPage(sessions);
            gameTab = new int[sessions.Length];
            var all = new List<Page> { homePage };
            all.AddRange(gamePages);
            all.AddRange(notesPages);
            all.Add(settingsPage);
            pages = all.ToArray();

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None; // scaling is done by Ui.S()
            Text = LauncherConfig.ProductName;
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
            homePage.OpenGame += i => sidebar.Selected = i + 1;

            for (var i = 0; i < sessions.Length; i++)
            {
                var index = i;
                var session = sessions[i];
                homePage.Buttons[i].Click += async (s, e) => await ActAsync(session);
                gamePages[i].Bar.ActionButton.Click += async (s, e) => await ActAsync(session);
                gamePages[i].NotesLink.Activated += () => ShowGame(index, 1);
                gamePages[i].Tabs.SelectedChanged += tab => ShowGame(index, tab);
                notesPages[i].Tabs.SelectedChanged += tab => ShowGame(index, tab);
                notesPages[i].GitHubLink.Activated += () => Ui.OpenUrl(notesPages[index].GitHubLink.Tag as string);
                settingsPage.FolderButtons[i].Click += (s, e) => OpenInstallFolder(session);
                settingsPage.RepairButtons[i].Click += async (s, e) => await RepairAsync(index);
            }
            settingsPage.CheckButton.Click += async (s, e) => await CheckAllAsync();
            foreach (var link in settingsPage.ReleaseLinks.Concat(new[] { settingsPage.LauncherReleasesLink }))
            {
                var l = link;
                l.Activated += () => Ui.OpenUrl(l.Tag as string);
            }

            // Title bar first: index 0 is the top of the z-order, so it sits over the pages.
            Controls.Add(titleBar);
            Controls.Add(sidebar);
            foreach (var p in pages) Controls.Add(p);
            LayoutWindow();
            int startPage;
            ShowPage(int.TryParse(ArgValue("--page"), out startPage) && startPage >= 0 && startPage <= sidebar.SettingsIndex ? startPage : 0);
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

        /// <summary>Shows the page for a sidebar item (0 = Home, 1..N = games, N+1 = Settings).</summary>
        void ShowPage(int index)
        {
            Page target = settingsPage;
            if (index == 0) target = homePage;
            else if (index <= sessions.Length) target = gameTab[index - 1] == 1 ? (Page)notesPages[index - 1] : gamePages[index - 1];
            foreach (var p in pages) p.Visible = p == target;
            if (sidebar.Selected != index) sidebar.Selected = index;
            titleBar.Invalidate(true);
        }

        void ShowGame(int game, int tab)
        {
            gameTab[game] = tab;
            gamePages[game].Tabs.Selected = 0;
            notesPages[game].Tabs.Selected = 1;
            if (sidebar.Selected == game + 1) ShowPage(game + 1);
            else sidebar.Selected = game + 1;
        }

        Page CurrentPage
        {
            get
            {
                foreach (var p in pages) if (p.Visible) return p;
                return homePage;
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

            foreach (var s in sessions) s.PrepareFolder();

            foreach (var s in sessions) if (s.Game.IsPublished) s.SetBusy("Checking for launcher updates…");
            await LauncherUpdater.CleanupAsync();
            if (await LauncherUpdater.TryUpdateAsync(status => { foreach (var s in sessions) if (s.Game.IsPublished) s.SetBusy(status); }))
            {
                Close(); // the new launcher is already starting
                return;
            }
            await CheckAllAsync();
        }

        Task CheckAllAsync()
        {
            return Task.WhenAll(sessions.Where(s => s.IsIdle || !s.Checked).Select(s => s.CheckForUpdatesAsync()));
        }

        async Task ActAsync(GameSession session)
        {
            if (session.Mode == Mode.Install || session.Mode == Mode.Update) ShowGame(Array.IndexOf(sessions, session), 0);
            if (!await session.ActAsync()) return;
            // The game is starting. Get out of the way, but don't cut off another game's download.
            if (sessions.Any(s => s.IsWorking)) WindowState = FormWindowState.Minimized;
            else Close();
        }

        async Task RepairAsync(int index)
        {
            var session = sessions[index];
            if (!session.IsIdle) return;
            if (session.Latest == null)
            {
                session.ShowError("Can't repair while offline.");
                return;
            }
            if (ConfirmDialog.Ask(this, "Repair " + session.Game.Name, "Re-download and reinstall " + session.Latest.Tag + "?\n\nYour saves and settings are kept.", "Repair", "Cancel"))
            {
                ShowGame(index, 0);
                await session.InstallAsync();
            }
        }

        static void OpenInstallFolder(GameSession session)
        {
            var dir = session.Installer.GameDir;
            if (!Directory.Exists(dir))
            {
                dir = session.Installer.Root;
                try { Directory.CreateDirectory(dir); } catch { return; }
            }
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"")); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (sessions.Any(s => s.IsWorking))
            {
                var stop = ConfirmDialog.Ask(this, "Quit the launcher?", "An install is in progress. Cancel it and quit?", "Cancel install", "Keep going", true);
                // Extracting can't be stopped halfway; only downloads cancel.
                if (!stop || sessions.Any(s => s.IsExtracting)) { e.Cancel = true; return; }
                foreach (var s in sessions) s.CancelDownload();
            }
            base.OnFormClosing(e);
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
                await CheckAllAsync();
                var suffix = fixedScale ? "-" + (int)Math.Round(Ui.Scale * 100) : "";
                Func<string, string> file = name => Path.Combine(dir, name + suffix + ".png");

                ShowPage(0);
                SaveScreenshot(this, file("home"));
                for (var i = 0; i < sessions.Length; i++)
                {
                    var id = sessions[i].Game.Id;
                    ShowGame(i, 0);
                    SaveScreenshot(this, file(id));
                    ShowGame(i, 1);
                    SaveScreenshot(this, file(id + "-notes"));
                    notesPages[i].View.ScrollToEnd();
                    SaveScreenshot(this, file(id + "-notes-end"));
                    ShowGame(i, 0);
                }
                ShowPage(sidebar.SettingsIndex);
                SaveScreenshot(this, file("settings"));

                // The download state, faked for the picture only (no download is started).
                var first = sessions[0];
                first.Fake(Mode.Downloading, "Downloading v1.4.2-early-access…", false, 0.42, "412.3 MB / 980.0 MB    ·    18.4 MB/s    ·    31s left");
                ShowGame(0, 0);
                SaveScreenshot(this, file(first.Game.Id + "-downloading"));
                ShowPage(0);
                SaveScreenshot(this, file("home-downloading"));
                first.Fake(Mode.Retry, "Couldn't reach GitHub: The remote name could not be resolved.", true);
                ShowGame(0, 0);
                SaveScreenshot(this, file(first.Game.Id + "-error"));
                first.Fake(Mode.Busy, "Done", false); // never leave the fake download state, or closing would ask to cancel it

                using (var dialog = new ConfirmDialog("Repair " + first.Game.Name, "Re-download and reinstall v1.4.2?\n\nYour saves and settings are kept.", "Repair", "Cancel"))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(Left + (Width - dialog.Width) / 2, Top + (Height - dialog.Height) / 2);
                    dialog.Show(this);
                    dialog.Refresh();
                    SaveScreenshot(dialog, file("confirm"));
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
    }
}
