using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ExtraLifeLauncher
{
    /// <summary>
    /// Left navigation: the studio brand, Home, one item per game (with a mini progress bar while it downloads),
    /// Settings at the bottom, and the launcher version.
    /// Indexes: 0 is Home, 1..N are the games in LauncherConfig.Games order, N+1 is Settings.
    /// </summary>
    class Sidebar : Control
    {
        public event Action<int> SelectedChanged;
        readonly GameSession[] games;
        readonly float[] hoverT;
        readonly Timer timer = new Timer { Interval = 15 };
        int hover = -1, selected;
        string footer = "";

        public Sidebar(GameSession[] games)
        {
            this.games = games;
            hoverT = new float[games.Length + 2];
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Sidebar;
            foreach (var g in games) g.Changed += Invalidate;
            timer.Tick += (s, e) =>
            {
                var busy = false;
                for (var i = 0; i < hoverT.Length; i++)
                {
                    var target = i == hover ? 1f : 0f;
                    var step = timer.Interval / 120f;
                    hoverT[i] = hoverT[i] < target ? Math.Min(target, hoverT[i] + step) : Math.Max(target, hoverT[i] - step);
                    if (hoverT[i] != target) busy = true;
                }
                if (!busy) timer.Stop();
                Invalidate();
            };
        }

        public int SettingsIndex { get { return games.Length + 1; } }

        public int Selected
        {
            get { return selected; }
            set
            {
                if (value == selected) return;
                selected = value;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(value);
            }
        }

        public string Footer { get { return footer; } set { footer = value; Invalidate(); } }

        const int GamesTop = 192; // below Home and the "GAMES" label

        Rectangle ItemRect(int i)
        {
            int x = Ui.S(14), w = Width - Ui.S(28), h = Ui.S(44);
            if (i == 0) return new Rectangle(x, Ui.S(108), w, h);
            if (i == SettingsIndex) return new Rectangle(x, Height - Ui.S(56) - h, w, h);
            return new Rectangle(x, Ui.S(GamesTop) + (i - 1) * Ui.S(50), w, h);
        }

        int ItemAt(Point p)
        {
            for (var i = 0; i < hoverT.Length; i++) if (ItemRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var i = ItemAt(e.Location);
            if (i != hover) { hover = i; timer.Start(); Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; timer.Start(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var i = ItemAt(e.Location);
                if (i >= 0) Selected = i;
                else Native.DragWindow(this);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // A faint red bloom behind the brand, so the sidebar isn't a flat slab.
            using (var path = new GraphicsPath())
            {
                var r = new Rectangle(-Ui.S(120), -Ui.S(140), Ui.S(360), Ui.S(300));
                path.AddEllipse(r);
                using (var brush = new PathGradientBrush(path) { CenterColor = Color.FromArgb(46, 150, 20, 14), SurroundColors = new[] { Color.FromArgb(0, 150, 20, 14) } })
                    g.FillRectangle(brush, r);
            }
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);

            var logoSize = Ui.S(42);
            var logoRect = new Rectangle(Ui.S(21), Ui.S(27), logoSize, logoSize);
            if (Resources.StudioMark != null) g.DrawImage(Resources.StudioMark, logoRect);
            var tx = logoRect.Right + Ui.S(12);
            var brand = Ui.Font(Face.DisplaySemibold, 17f);
            var brandH = Ui.Measure("W", brand).Height;
            var sub = Ui.Font(Face.TextSemibold, 10.5f);
            var subH = Ui.Measure("E", sub).Height;
            var top = logoRect.Y + (logoSize - brandH - subH - Ui.S(1)) / 2;
            Ui.Draw(g, "Extra Life", brand, new Rectangle(tx, top, Width - tx - Ui.S(8), brandH), Theme.Text);
            Ui.Draw(g, "INTERACTIVE", sub, new Point(tx + Ui.S(1), top + brandH + Ui.S(1)), Theme.AccentSoft);

            Ui.Draw(g, "GAMES", Ui.Font(Face.TextSemibold, 10.5f), new Point(Ui.S(32), Ui.S(GamesTop - 22)), Theme.Muted);
            using (var pen = new Pen(Theme.Border))
            {
                var sep = ItemRect(SettingsIndex).Y - Ui.S(10);
                g.DrawLine(pen, Ui.S(24), sep, Width - Ui.S(24), sep);
            }

            for (var i = 0; i < hoverT.Length; i++) PaintItem(g, i);

            var small = Ui.Font(Face.Text, 11.5f);
            Ui.Draw(g, footer, small, new Rectangle(Ui.S(24), Height - Ui.S(36), Width - Ui.S(36), Ui.S(20)), Theme.Muted);
        }

        void PaintItem(Graphics g, int i)
        {
            var r = ItemRect(i);
            var isSel = i == selected;
            var session = i >= 1 && i <= games.Length ? games[i - 1] : null;
            var accent = session != null ? session.Game.Accent : Theme.Accent;

            var fill = isSel ? Color.FromArgb(34, 28, 31) : Theme.Alpha(Color.White, (int)(10 * hoverT[i]));
            if (fill.A > 0) Theme.FillRounded(g, fill, new RectangleF(r.X, r.Y, r.Width, r.Height), Ui.S(8));
            if (isSel)
            {
                var bar = new RectangleF(r.X, r.Y + Ui.S(12), Ui.S(3), r.Height - Ui.S(24));
                Theme.FillRounded(g, accent, bar, Ui.S(1.5f));
            }
            var color = isSel ? Theme.Text : Theme.Lerp(Theme.Muted, Theme.TextDim, hoverT[i] * 0.9f + 0.1f);
            var x = r.X + Ui.S(16);
            if (session != null)
            {
                var size = Ui.S(26);
                var icon = session.Game.Icon;
                if (icon != null)
                {
                    var ir = new Rectangle(x, r.Y + (r.Height - size) / 2, size, size);
                    if (isSel || hoverT[i] > 0) g.DrawImage(icon, ir);
                    else
                    {
                        // Unselected games sit back a little.
                        var dim = new ColorMatrix { Matrix33 = 0.8f };
                        Resources.DrawWithMatrix(g, icon, ir, new Rectangle(0, 0, icon.Width, icon.Height), dim);
                    }
                }
                x += size + Ui.S(12);
            }
            else if (Ui.HasIcons)
            {
                var glyph = i == 0 ? Glyph.Home : Glyph.Settings;
                var iconFont = Ui.Font(Face.Icon, 16f);
                var isz = Ui.Measure(glyph, iconFont);
                Ui.Draw(g, glyph, iconFont, new Point(x + Ui.S(2), r.Y + (r.Height - isz.Height) / 2 + Ui.S(1)), isSel ? Theme.AccentSoft : color);
                x += Ui.S(32);
            }

            var label = i == 0 ? "Home" : session != null ? session.Game.Name : "Settings";
            var font = Ui.Font(isSel ? Face.TextSemibold : Face.Text, 14f);
            var fs = Ui.Measure(label, font);
            var showBar = session != null && session.Progress >= 0;
            var ty = r.Y + (r.Height - fs.Height) / 2 - (showBar ? Ui.S(3) : 0);
            var right = r.Right - Ui.S(12);

            if (showBar)
            {
                var pct = (int)(session.Progress * 100) + "%";
                var pf = Ui.Font(Face.TextSemibold, 11f);
                var ps = Ui.Measure(pct, pf);
                Ui.Draw(g, pct, pf, new Point(right - ps.Width, ty + (fs.Height - ps.Height) / 2), Theme.TextDim);
                right -= ps.Width + Ui.S(6);
                var track = new RectangleF(x, r.Bottom - Ui.S(10), r.Right - Ui.S(12) - x, Ui.S(3));
                Theme.FillRounded(g, Theme.Alpha(Color.White, 22), track, track.Height / 2);
                var fill2 = new RectangleF(track.X, track.Y, (float)(track.Width * session.Progress), track.Height);
                if (fill2.Width >= track.Height) Theme.FillRounded(g, accent, fill2, track.Height / 2);
            }
            else if (session != null && session.Mode == Mode.ComingSoon)
            {
                var pf = Ui.Font(Face.TextSemibold, 9.5f);
                var ps = Ui.Measure("SOON", pf);
                var pill = new RectangleF(right - ps.Width - Ui.S(12), r.Y + (r.Height - ps.Height - Ui.S(6)) / 2f, ps.Width + Ui.S(12), ps.Height + Ui.S(6));
                Theme.FillRounded(g, Theme.Alpha(session.Game.Accent, 34), pill, pill.Height / 2);
                Ui.Draw(g, "SOON", pf, new Point((int)pill.X + Ui.S(6), (int)pill.Y + Ui.S(3)), session.Game.AccentSoft);
                right = (int)pill.X - Ui.S(6);
            }
            else if (session != null && (session.Mode == Mode.Update || session.Mode == Mode.Install))
            {
                // A dot for "there's something to download".
                var d = Ui.S(7);
                using (var brush = new SolidBrush(accent)) g.FillEllipse(brush, right - d, r.Y + (r.Height - d) / 2f, d, d);
                right -= d + Ui.S(8);
            }
            Ui.Draw(g, label, font, new Rectangle(x, ty, right - x, fs.Height), color);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Draggable strip with minimize/close, painted over whatever page is showing.</summary>
    class TitleBar : Control, IBackdrop
    {
        public readonly CaptionButton MinimizeButton = new CaptionButton(Glyph.Minimize, false);
        public readonly CaptionButton CloseButton = new CaptionButton(Glyph.Close, true);

        public TitleBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Controls.Add(MinimizeButton);
            Controls.Add(CloseButton);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            var w = Ui.S(46);
            CloseButton.SetBounds(Width - w, 0, w, Height);
            MinimizeButton.SetBounds(Width - 2 * w, 0, w, Height);
            base.OnLayout(e);
        }

        public void PaintBackdrop(Graphics g, Rectangle rect) { Backdrop.Paint(this, g, rect); }
        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Paint(this, e.Graphics, ClientRectangle); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Native.DragWindow(this);
            base.OnMouseDown(e);
        }
    }

    /// <summary>Common page chrome: dark background with a faint glow at the top, a big title and subtitle.</summary>
    abstract class Page : Control, IBackdrop
    {
        protected Color Glow = Color.FromArgb(170, 26, 18);

        protected Page()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
        }

        public virtual void PaintBackdrop(Graphics g, Rectangle rect)
        {
            using (var brush = new SolidBrush(Theme.Background)) g.FillRectangle(brush, rect);
            var glow = new Rectangle(0, 0, Width, Ui.S(240));
            var area = Rectangle.Intersect(rect, glow);
            if (area.Width > 0 && area.Height > 0)
                using (var brush = new LinearGradientBrush(new Rectangle(0, -1, Width, glow.Height + 2), Theme.Alpha(Glow, 30), Theme.Alpha(Glow, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(brush, area);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            PaintBackdrop(e.Graphics, ClientRectangle);
        }

        protected void DrawHeader(Graphics g, string title, string subtitle)
        {
            var tf = Ui.Font(Face.DisplaySemibold, 30f);
            Ui.Draw(g, title, tf, new Point(Ui.S(36), Ui.S(46)), Theme.Text);
            if (!string.IsNullOrEmpty(subtitle))
                Ui.Draw(g, subtitle, Ui.Font(Face.Text, 13.5f), new Rectangle(Ui.S(38), Ui.S(90), Width - Ui.S(300), Ui.S(22)), Theme.Muted);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < Ui.S(120)) Native.DragWindow(this);
            base.OnMouseDown(e);
        }

        /// <summary>Re-applies sizes after a DPI change.</summary>
        public virtual void Rescale() { PerformLayout(); Invalidate(true); }
    }

    /// <summary>The key art behind a game's logo: its texture or the studio's rings, a glow, the logo with a shadow.</summary>
    static class GameArt
    {
        /// <summary>Logo rectangle that fits <paramref name="area"/>, at most <paramref name="maxHeight"/> tall.</summary>
        public static Rectangle FitLogo(GameInfo game, Rectangle area, int maxHeight)
        {
            var aspect = game.Logo != null ? game.Logo.Width / (float)game.Logo.Height : 1f;
            var h = Math.Min(maxHeight, area.Height);
            var w = (int)(h * aspect);
            if (w > area.Width) { w = area.Width; h = (int)(w / aspect); }
            return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        }

        /// <summary>
        /// Paints the art into <paramref name="area"/>, fading into <paramref name="baseColor"/> between
        /// <paramref name="fadeTop"/> and <paramref name="fadeBottom"/> (solid below).
        /// </summary>
        public static void Paint(Graphics g, GameInfo game, Rectangle area, Rectangle logo, Color baseColor, int fadeTop, int fadeBottom)
        {
            var state = g.Save();
            g.SetClip(area, CombineMode.Intersect);
            using (var brush = new SolidBrush(baseColor)) g.FillRectangle(brush, area);

            if (game.Backdrop != null)
            {
                // The Who Haunts diary page, burnt almost to black and stained red: a nod to the game's journal.
                var src = CoverCrop(game.Backdrop.Size, area.Size);
                var m = new ColorMatrix(new[]
                {
                    new[] { 0.16f, 0.06f, 0.06f, 0, 0 },
                    new[] { 0.16f, 0.06f, 0.06f, 0, 0 },
                    new[] { 0.05f, 0.02f, 0.02f, 0, 0 },
                    new[] { 0f, 0, 0, 1, 0 },
                    new[] { 0f, 0, 0, 0, 1 },
                });
                Resources.DrawWithMatrix(g, game.Backdrop, area, src, m);
            }
            else
            {
                // No texture: rings around the logo, like looking down a shaft.
                var c = new PointF(logo.X + logo.Width / 2f, logo.Y + logo.Height / 2f);
                var r = logo.Height * 0.3f;
                for (var k = 0; k < 12; k++, r *= 1.28f)
                    using (var pen = new Pen(Theme.Alpha(game.Glow, Math.Max(6, 46 - k * 4)), Math.Max(1f, logo.Height / 220f)))
                        g.DrawEllipse(pen, c.X - r * 1.25f, c.Y - r, r * 2.5f, r * 2f);
            }

            var glowCenter = new PointF(logo.X + logo.Width * 0.62f, logo.Y + logo.Height * 0.38f);
            using (var path = new GraphicsPath())
            {
                var gr = new RectangleF(glowCenter.X - logo.Height * 1.4f, glowCenter.Y - logo.Height * 1.1f, logo.Height * 2.8f, logo.Height * 2.2f);
                path.AddEllipse(gr);
                using (var brush = new PathGradientBrush(path) { CenterPoint = glowCenter, CenterColor = Theme.Alpha(game.Glow, 120), SurroundColors = new[] { Theme.Alpha(Theme.Lerp(game.Glow, Color.Black, 0.3f), 0) } })
                    g.FillRectangle(brush, gr);
            }

            // Vignette: darker at the top and fading into the base colour at the bottom.
            var vignette = Math.Min(Ui.S(110), area.Height / 3);
            using (var brush = new LinearGradientBrush(new Rectangle(area.X, area.Y - 1, area.Width, vignette + 2), Color.FromArgb(140, 6, 5, 6), Color.FromArgb(0, 6, 5, 6), LinearGradientMode.Vertical))
                g.FillRectangle(brush, area.X, area.Y, area.Width, vignette);
            using (var brush = new LinearGradientBrush(new Rectangle(area.X, fadeTop - 1, area.Width, fadeBottom - fadeTop + 2), Theme.Alpha(baseColor, 0), baseColor, LinearGradientMode.Vertical))
                g.FillRectangle(brush, area.X, fadeTop, area.Width, fadeBottom - fadeTop);
            using (var brush = new SolidBrush(baseColor)) g.FillRectangle(brush, area.X, fadeBottom, area.Width, area.Bottom - fadeBottom);

            if (game.Logo != null)
            {
                // Soft shadow so the lettering lifts off the glow.
                var shadow = new ColorMatrix(new[]
                {
                    new[] { 0f, 0, 0, 0, 0 }, new[] { 0f, 0, 0, 0, 0 }, new[] { 0f, 0, 0, 0, 0 },
                    new[] { 0f, 0, 0, 0.22f, 0 }, new[] { 0f, 0, 0, 0, 1 },
                });
                var src = new Rectangle(0, 0, game.Logo.Width, game.Logo.Height);
                var unit = logo.Height / 300f;
                foreach (var o in new[] { new Point(0, 6), new Point(4, 8), new Point(-4, 8), new Point(0, 12) })
                {
                    var d = logo;
                    d.Offset((int)Math.Round(Ui.S(o.X) * unit), (int)Math.Round(Ui.S(o.Y) * unit));
                    Resources.DrawWithMatrix(g, game.Logo, d, src, shadow);
                }
                g.DrawImage(game.Logo, logo);
            }
            g.Restore(state);
        }

        static Rectangle CoverCrop(Size image, Size target)
        {
            var scale = Math.Max(target.Width / (double)image.Width, target.Height / (double)image.Height);
            var w = (int)(target.Width / scale);
            var h = (int)(target.Height / scale);
            return new Rectangle((image.Width - w) / 2, (image.Height - h) / 2, w, h);
        }

        public static Bitmap NewSurface(Size size)
        {
            return new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
        }

        /// <summary>
        /// Nudges every (13,11,12) pixel one step lighter. That exact colour comes out black in the window and in
        /// captures (see Theme.Background), and faint glows over the background land on it in long streaks.
        /// </summary>
        public static void AvoidBlackoutColor(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new int[bmp.Width];
                for (var y = 0; y < bmp.Height; y++)
                {
                    var ptr = data.Scan0 + y * data.Stride;
                    System.Runtime.InteropServices.Marshal.Copy(ptr, row, 0, row.Length);
                    var changed = false;
                    for (var x = 0; x < row.Length; x++)
                        if ((row[x] & 0x00FFFFFF) == 0x000D0B0C && (row[x] >> 24 & 0xFF) == 0xFF) { row[x] = unchecked((int)0xFF0E0C0D); changed = true; }
                    if (changed) System.Runtime.InteropServices.Marshal.Copy(row, 0, ptr, row.Length);
                }
            }
            finally { bmp.UnlockBits(data); }
        }

        public static Graphics Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        /// <summary>Copies a cached surface 1:1 (it's already at the page's size).</summary>
        public static void Blit(Graphics g, Bitmap surface, Rectangle rect)
        {
            var state = g.Save();
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(surface, rect, rect, GraphicsUnit.Pixel);
            g.Restore(state);
        }
    }

    /// <summary>What a game's big button says and does in each mode.</summary>
    static class ActionButtons
    {
        public static void Apply(FlatButton button, GameSession session)
        {
            var mode = session.Mode;
            button.Accent = session.Game.Accent;
            button.Kind = mode == Mode.Downloading ? ButtonKind.Secondary : ButtonKind.Primary;
            switch (mode)
            {
                case Mode.Busy: button.Text = "PLEASE WAIT"; button.Icon = null; break;
                case Mode.Install: button.Text = "INSTALL"; button.Icon = Glyph.Download; break;
                case Mode.Update: button.Text = "UPDATE"; button.Icon = Glyph.Download; break;
                case Mode.Play: button.Text = "PLAY"; button.Icon = Glyph.Play; break;
                case Mode.Retry: button.Text = "RETRY"; button.Icon = Glyph.Refresh; break;
                case Mode.Downloading: button.Text = "CANCEL"; button.Icon = Glyph.Cancel; break;
                case Mode.ComingSoon: button.Text = "COMING SOON"; button.Icon = Glyph.Clock; break;
            }
            button.Enabled = mode != Mode.Busy && mode != Mode.ComingSoon;
        }
    }

    /// <summary>Segmented control for a game's sub-pages (Overview / Patch Notes).</summary>
    class TabStrip : FxControl
    {
        readonly string[] labels;
        int selected, hoverIndex = -1;
        public event Action<int> SelectedChanged;

        public TabStrip(params string[] labels)
        {
            this.labels = labels;
            TabStop = false;
        }

        public int Selected { get { return selected; } set { selected = value; Invalidate(); } }

        Font LabelFont { get { return Ui.Font(Face.TextSemibold, 13.5f); } }

        Rectangle TabRect(int i)
        {
            var x = Ui.S(4);
            for (var k = 0; k < i; k++) x += Ui.Measure(labels[k], LabelFont).Width + Ui.S(32);
            return new Rectangle(x, Ui.S(4), Ui.Measure(labels[i], LabelFont).Width + Ui.S(32), Height - Ui.S(8));
        }

        public void FitSize()
        {
            Height = Ui.S(38);
            Width = TabRect(labels.Length - 1).Right + Ui.S(4);
        }

        int TabAt(Point p)
        {
            for (var i = 0; i < labels.Length; i++) if (TabRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var i = TabAt(e.Location);
            if (i != hoverIndex) { hoverIndex = i; Invalidate(); }
            Cursor = i >= 0 && i != selected ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var i = TabAt(e.Location);
            if (i < 0 || i == selected) return;
            Selected = i;
            if (SelectedChanged != null) SelectedChanged(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRounded(g, Color.FromArgb(150, 10, 8, 9), outer, Ui.S(10));
            Theme.StrokeRounded(g, Theme.Alpha(Color.White, 18), outer, Ui.S(10));
            for (var i = 0; i < labels.Length; i++)
            {
                var r = TabRect(i);
                if (i == selected) Theme.FillRounded(g, Theme.Alpha(Color.White, 22), new RectangleF(r.X, r.Y, r.Width, r.Height), Ui.S(7));
                else if (i == hoverIndex) Theme.FillRounded(g, Theme.Alpha(Color.White, 8), new RectangleF(r.X, r.Y, r.Width, r.Height), Ui.S(7));
                var color = i == selected ? Theme.Text : i == hoverIndex ? Theme.TextDim : Theme.Muted;
                var size = Ui.Measure(labels[i], LabelFont);
                Ui.Draw(g, labels[i], LabelFont, new Point(r.X + (r.Width - size.Width) / 2, r.Y + (r.Height - size.Height) / 2), color);
            }
        }
    }

    /// <summary>Library: a card per game with its art, status and button.</summary>
    class HomePage : Page
    {
        readonly GameSession[] games;
        public readonly FlatButton[] Buttons;
        public event Action<int> OpenGame;
        Bitmap surface;
        int hover = -1;

        const int InfoHeight = 112;

        public HomePage(GameSession[] games)
        {
            this.games = games;
            Buttons = new FlatButton[games.Length];
            for (var i = 0; i < games.Length; i++)
            {
                var session = games[i];
                var button = new FlatButton("", ButtonKind.Primary) { FontPx = 15f, Radius = 8f, TextFace = Face.DisplaySemibold };
                Buttons[i] = button;
                ActionButtons.Apply(button, session);
                session.Changed += () =>
                {
                    var before = button.Text;
                    ActionButtons.Apply(button, session);
                    if (button.Text != before) PerformLayout(); // the label decides the width
                    Invalidate();
                };
            }
            Controls.AddRange(Buttons); // after the loop: adding one lays out all of them
        }

        Rectangle CardRect(int i)
        {
            var gap = Ui.S(20);
            var top = Ui.S(132);
            var w = (Width - Ui.S(72) - gap * (games.Length - 1)) / Math.Max(1, games.Length);
            return new Rectangle(Ui.S(36) + i * (w + gap), top, w, Height - top - Ui.S(36));
        }

        Rectangle ArtRect(Rectangle card) { return new Rectangle(card.X, card.Y, card.Width, card.Height - Ui.S(InfoHeight)); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            for (var i = 0; i < games.Length; i++)
            {
                var card = CardRect(i);
                var art = ArtRect(card);
                var w = Math.Max(Ui.S(150), Buttons[i].PreferredWidth(18));
                var h = Ui.S(46);
                Buttons[i].SetBounds(card.Right - Ui.S(22) - w, art.Bottom + (card.Bottom - art.Bottom - h) / 2, w, h);
            }
            if (surface != null && surface.Size != Size) InvalidateSurface();
            base.OnLayout(e);
        }

        public override void Rescale() { InvalidateSurface(); base.Rescale(); }

        void InvalidateSurface()
        {
            if (surface != null) { surface.Dispose(); surface = null; }
            Invalidate(true);
        }

        Bitmap Surface()
        {
            if (surface != null && surface.Size == Size) return surface;
            if (surface != null) surface.Dispose();
            surface = GameArt.NewSurface(Size);
            using (var g = GameArt.Prepare(Graphics.FromImage(surface)))
            {
                base.PaintBackdrop(g, new Rectangle(0, 0, Width, Height));
                for (var i = 0; i < games.Length; i++)
                {
                    var game = games[i].Game;
                    var card = CardRect(i);
                    var cardF = new RectangleF(card.X + 0.5f, card.Y + 0.5f, card.Width - 1, card.Height - 1);
                    using (var path = Theme.Rounded(cardF, Ui.S(14)))
                    {
                        var state = g.Save();
                        g.SetClip(path);
                        var art = ArtRect(card);
                        var logoArea = Rectangle.Inflate(art, -Ui.S(36), -Ui.S(40));
                        logoArea.Offset(0, Ui.S(4));
                        var logo = GameArt.FitLogo(game, logoArea, Ui.S(240));
                        GameArt.Paint(g, game, card, logo, Theme.Surface, art.Bottom - Ui.S(80), art.Bottom + Ui.S(2));
                        g.Restore(state);
                    }
                    Theme.StrokeRounded(g, Theme.Alpha(Color.White, 20), cardF, Ui.S(14));

                    // Stage pill in the art's corner.
                    var pf = Ui.Font(Face.TextSemibold, 10f);
                    var ps = Ui.Measure(game.Stage, pf);
                    var pill = new RectangleF(card.X + Ui.S(18), card.Y + Ui.S(18), ps.Width + Ui.S(20), ps.Height + Ui.S(10));
                    Theme.FillRounded(g, Color.FromArgb(170, 12, 10, 11), pill, pill.Height / 2);
                    Theme.StrokeRounded(g, Theme.Alpha(game.Accent, 110), pill, pill.Height / 2);
                    Ui.Draw(g, game.Stage, pf, new Point((int)pill.X + Ui.S(10), (int)pill.Y + Ui.S(5)), game.AccentSoft);
                }
            }
            GameArt.AvoidBlackoutColor(surface);
            return surface;
        }

        public override void PaintBackdrop(Graphics g, Rectangle rect) { GameArt.Blit(g, Surface(), rect); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawHeader(g, "Library", games.Length + (games.Length == 1 ? " game" : " games") + " from " + LauncherConfig.StudioName);

            for (var i = 0; i < games.Length; i++)
            {
                var session = games[i];
                var card = CardRect(i);
                var art = ArtRect(card);
                if (i == hover)
                    Theme.StrokeRounded(g, Theme.Alpha(session.Game.Accent, 150), new RectangleF(card.X + 0.5f, card.Y + 0.5f, card.Width - 1, card.Height - 1), Ui.S(14));

                var x = card.X + Ui.S(24);
                var right = Buttons[i].Left - Ui.S(18);
                var nf = Ui.Font(Face.DisplaySemibold, 21f);
                var nh = Ui.Measure("Ag", nf).Height;
                var sf = Ui.Font(Face.Text, 12.5f);
                var sh = Ui.Measure("Ag", sf).Height;
                var showBar = session.Progress >= 0;
                var block = nh + Ui.S(6) + sh + (showBar ? Ui.S(12) : 0);
                var y = art.Bottom + (card.Bottom - art.Bottom - block) / 2 - Ui.S(2);
                Ui.Draw(g, session.Game.Name, nf, new Rectangle(x, y, right - x, nh), Theme.Text);
                y += nh + Ui.S(6);

                var status = session.ShortStatus;
                if (showBar) status = (int)(session.Progress * 100) + "%  ·  " + status;
                Ui.Draw(g, status, sf, new Rectangle(x, y, right - x, sh), session.IsError ? Theme.Error : Theme.TextDim);
                if (showBar)
                {
                    var track = new RectangleF(x, y + sh + Ui.S(8), right - x, Ui.S(4));
                    Theme.FillRounded(g, Theme.Alpha(Color.White, 24), track, track.Height / 2);
                    var fill = new RectangleF(track.X, track.Y, (float)(track.Width * session.Progress), track.Height);
                    if (fill.Width >= track.Height) Theme.FillRounded(g, session.Game.Accent, fill, track.Height / 2);
                }
            }
        }

        int CardAt(Point p)
        {
            for (var i = 0; i < games.Length; i++) if (CardRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var i = CardAt(e.Location);
            if (i != hover) { hover = i; Invalidate(); }
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { if (hover >= 0) { hover = -1; Invalidate(); } base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var i = CardAt(e.Location);
            if (e.Button == MouseButtons.Left && i >= 0)
            {
                if (OpenGame != null) OpenGame(i);
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && surface != null) surface.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>A game's home: hero art with its logo, a "what's new" card and the play bar.</summary>
    class GamePage : Page
    {
        public const int TabsTop = 50;
        public readonly GameSession Session;
        public readonly PlayBar Bar;
        public readonly TabStrip Tabs = new TabStrip("Overview", "Patch Notes");
        public readonly TextLink NotesLink = new TextLink("Read full patch notes", Glyph.Forward);
        Bitmap surface;
        List<string> highlights = new List<string>();
        ReleaseInfo shownRelease;

        public GamePage(GameSession session)
        {
            Session = session;
            Glow = session.Game.Glow;
            Bar = new PlayBar(session);
            NotesLink.Color = session.Game.AccentSoft;
            NotesLink.HoverColor = Theme.Lerp(session.Game.AccentSoft, Color.White, 0.3f);
            Controls.Add(Bar);
            Controls.Add(Tabs);
            Controls.Add(NotesLink);
            session.Changed += OnSessionChanged;
            OnSessionChanged();
        }

        void OnSessionChanged()
        {
            var release = Session.Latest;
            if (release != shownRelease)
            {
                shownRelease = release;
                highlights = release != null && !string.IsNullOrWhiteSpace(release.Notes) ? Markdown.Highlights(release.Notes, 4) : new List<string>();
            }
            NotesLink.Visible = release != null;
            Invalidate();
        }

        Rectangle CardRect
        {
            get
            {
                var w = Ui.S(318);
                var top = Ui.S(TabsTop);
                return new Rectangle(Width - Ui.S(32) - w, top, w, Bar.Top - top - Ui.S(34));
            }
        }

        Rectangle LogoRect
        {
            get
            {
                var card = CardRect;
                var top = Tabs.Bottom + Ui.S(10);
                var area = new Rectangle(Ui.S(40), top, card.X - Ui.S(80), Bar.Top - Ui.S(10) - top);
                return GameArt.FitLogo(Session.Game, area, Ui.S(300));
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            var barH = Ui.S(132);
            Bar.SetBounds(0, Height - barH, Width, barH);
            Tabs.FitSize();
            Tabs.Location = new Point(Ui.S(32), Ui.S(TabsTop));
            NotesLink.FitSize();
            var card = CardRect;
            NotesLink.Location = new Point(card.X + Ui.S(22), card.Bottom - Ui.S(20) - NotesLink.Height);
            InvalidateSurface();
            base.OnLayout(e);
        }

        public override void Rescale() { InvalidateSurface(); base.Rescale(); }

        void InvalidateSurface()
        {
            if (surface != null) { surface.Dispose(); surface = null; }
            Invalidate(true);
            var form = FindForm();
            if (form != null) foreach (Control c in form.Controls) if (c is TitleBar) c.Invalidate(true);
        }

        Bitmap Surface()
        {
            if (surface != null && surface.Size == Size) return surface;
            if (surface != null) surface.Dispose();
            surface = GameArt.NewSurface(Size);
            using (var g = GameArt.Prepare(Graphics.FromImage(surface)))
            {
                GameArt.Paint(g, Session.Game, new Rectangle(0, 0, Width, Height), LogoRect, Theme.Background, Bar.Top - Ui.S(170), Bar.Top + Ui.S(30));

                // "What's new" card, translucent over the art.
                var card = CardRect;
                var cardF = new RectangleF(card.X + 0.5f, card.Y + 0.5f, card.Width - 1, card.Height - 1);
                Theme.FillRounded(g, Color.FromArgb(178, 16, 13, 15), cardF, Ui.S(14));
                Theme.StrokeRounded(g, Theme.Alpha(Color.White, 20), cardF, Ui.S(14));
                var accent = Session.Game.Accent;
                using (var brush = new LinearGradientBrush(new RectangleF(card.X, card.Y, card.Width, Ui.S(3)), accent, Theme.Alpha(accent, 0), LinearGradientMode.Horizontal))
                using (var path = Theme.Rounded(new RectangleF(card.X + Ui.S(22), card.Y, card.Width - Ui.S(44), Ui.S(2)), Ui.S(1)))
                    g.FillPath(brush, path);
            }
            GameArt.AvoidBlackoutColor(surface);
            return surface;
        }

        public override void PaintBackdrop(Graphics g, Rectangle rect) { GameArt.Blit(g, Surface(), rect); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var game = Session.Game;
            var release = Session.Latest;
            var card = CardRect;
            var x = card.X + Ui.S(24);
            var w = card.Width - Ui.S(48);
            var y = card.Y + Ui.S(24);
            var label = !game.IsPublished ? "COMING SOON" : release != null ? "WHAT'S NEW" : "PATCH NOTES";
            Ui.Draw(g, label, Ui.Font(Face.TextSemibold, 11f), new Point(x, y), game.AccentSoft);
            y += Ui.S(24);
            if (release == null)
            {
                string text;
                if (!game.IsPublished)
                {
                    var tf0 = Ui.Font(Face.DisplaySemibold, 20f);
                    var h0 = Ui.Measure("Ag", tf0).Height;
                    Ui.Draw(g, game.Name, tf0, new Rectangle(x, y, w, h0), Theme.Text);
                    y += h0 + Ui.S(14);
                    text = game.Name + " isn't out yet. When the first build is ready it shows up here, one click from installing.";
                }
                else text = Session.Error ?? (Session.Checked ? "No builds have been published yet. Check back soon!" : "Patch notes appear here once the launcher reaches the update server.");
                Ui.DrawWrapped(g, text, Ui.Font(Face.Text, 13.5f), new Rectangle(x, y, w, card.Bottom - y - Ui.S(20)), Theme.TextDim);
                return;
            }
            var title = string.IsNullOrWhiteSpace(release.Name) ? release.Tag : release.Name;
            var tf = Ui.Font(Face.DisplaySemibold, 20f);
            var th = Math.Min(Ui.MeasureWrapped(title, tf, w), Ui.Measure("Ag", tf).Height * 2);
            Ui.DrawWrapped(g, title, tf, new Rectangle(x, y, w, th), Theme.Text);
            y += th + Ui.S(4);
            var meta = release.Tag;
            if (release.PublishedAt != DateTime.MinValue) meta += "  ·  " + Format.Date(release.PublishedAt);
            Ui.Draw(g, meta, Ui.Font(Face.Text, 12.5f), new Rectangle(x, y, w, Ui.S(20)), Theme.Muted);
            y += Ui.S(34);

            var bf = Ui.Font(Face.Text, 13.5f);
            var lineH = Ui.Measure("Ag", bf).Height;
            var limit = NotesLink.Top - Ui.S(14);
            if (highlights.Count == 0)
                Ui.Draw(g, "No notes for this build.", bf, new Rectangle(x, y, w, lineH), Theme.Muted);
            var maxLines = highlights.Count <= 2 ? 4 : 2;
            var bodyW = w - Ui.S(18);
            foreach (var h in highlights)
            {
                var needed = Math.Min(Ui.MeasureWrapped(h, bf, bodyW), lineH * maxLines);
                var fitLines = (limit - y) / lineH;
                if (fitLines < 1) break;
                var height = Math.Min(needed, fitLines * lineH);
                var d = Ui.S(6);
                using (var brush = new SolidBrush(game.Accent)) g.FillEllipse(brush, x + Ui.S(1), y + (lineH - d) / 2 + Ui.S(1), d, d);
                Ui.DrawWrapped(g, h, bf, new Rectangle(x + Ui.S(18), y, bodyW, height), Theme.TextDim);
                y += height + Ui.S(12);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Native.DragWindow(this); // the whole hero is a drag handle
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && surface != null) surface.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Bottom of a game page: status, progress, versions and the big action button.</summary>
    class PlayBar : Control, IBackdrop
    {
        public readonly FlatButton ActionButton = new FlatButton("PLEASE WAIT", ButtonKind.Primary)
        {
            FontPx = 19f, Radius = 10f, TextFace = Face.DisplaySemibold,
        };
        readonly GameSession session;

        public PlayBar(GameSession session)
        {
            this.session = session;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Controls.Add(ActionButton);
            session.Changed += () =>
            {
                var before = ActionButton.Text;
                ActionButtons.Apply(ActionButton, session);
                if (ActionButton.Text != before) PerformLayout();
                Invalidate();
            };
            ActionButtons.Apply(ActionButton, session);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            var w = Math.Max(Ui.S(220), ActionButton.PreferredWidth(26));
            var h = Ui.S(64);
            ActionButton.SetBounds(Width - Ui.S(32) - w, Ui.S(38), w, h);
            base.OnLayout(e);
        }

        public void PaintBackdrop(Graphics g, Rectangle rect) { Backdrop.Paint(this, g, rect); }
        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Paint(this, e.Graphics, ClientRectangle); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Alpha(Color.White, 16)))
                g.DrawLine(pen, Ui.S(32), Ui.S(14), Width - Ui.S(32), Ui.S(14));

            var x = Ui.S(36);
            var right = ActionButton.Left - Ui.S(36);
            var sf = Ui.Font(Face.DisplaySemibold, 15.5f);
            var sh = Ui.Measure("Ag", sf).Height;
            var progress = session.Progress;
            var showBar = progress >= 0;
            var y = ActionButton.Top + (showBar ? Ui.S(2) : Ui.S(8));
            var pctW = 0;
            if (showBar)
            {
                var pct = (int)(progress * 100) + "%";
                pctW = Ui.Measure(pct, sf).Width + Ui.S(12);
                Ui.Draw(g, pct, sf, new Point(right - pctW + Ui.S(12), y), Theme.Text);
            }
            if (session.IsError && Ui.HasIcons)
            {
                var wf = Ui.Font(Face.Icon, 15f);
                var ws = Ui.Measure(Glyph.Warning, wf);
                Ui.Draw(g, Glyph.Warning, wf, new Point(x, y + (sh - ws.Height) / 2 + Ui.S(1)), Theme.Error);
                x += ws.Width + Ui.S(8);
            }
            Ui.Draw(g, session.Status, sf, new Rectangle(x, y, right - x - pctW, sh), session.IsError ? Theme.Error : Theme.Text);
            y += sh + Ui.S(8);
            x = Ui.S(36);

            if (showBar)
            {
                var accent = session.Game.Accent;
                var track = new RectangleF(x, y, right - x, Ui.S(6));
                Theme.FillRounded(g, Theme.Alpha(Color.White, 24), track, track.Height / 2);
                var fw = (float)(track.Width * progress);
                if (fw >= track.Height)
                {
                    var fill = new RectangleF(track.X, track.Y, fw, track.Height);
                    using (var path = Theme.Rounded(fill, track.Height / 2))
                    using (var brush = new LinearGradientBrush(new RectangleF(track.X - 1, track.Y, track.Width + 2, track.Height), Theme.Pressed(accent), Theme.Hover(accent), LinearGradientMode.Horizontal))
                        g.FillPath(brush, path);
                }
                y += Ui.S(16);
            }
            var hasDetail = !string.IsNullOrEmpty(session.Detail);
            Ui.Draw(g, hasDetail ? session.Detail : session.VersionsLine(), Ui.Font(Face.Text, 12.5f), new Rectangle(x, y, right - x, Ui.S(20)), hasDetail ? Theme.TextDim : Theme.Muted);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Native.DragWindow(this);
            base.OnMouseDown(e);
        }
    }

    /// <summary>A game's full release notes, under the same tabs as its home.</summary>
    class NotesPage : Page
    {
        public readonly GameSession Session;
        public readonly MarkdownView View = new MarkdownView();
        public readonly TabStrip Tabs = new TabStrip("Overview", "Patch Notes") { Selected = 1 };
        public readonly TextLink GitHubLink = new TextLink("View on GitHub", Glyph.External);
        string title = "", subtitle = "", shownKey;

        public NotesPage(GameSession session)
        {
            Session = session;
            Glow = session.Game.Glow;
            GitHubLink.Color = session.Game.AccentSoft;
            GitHubLink.HoverColor = Theme.Lerp(session.Game.AccentSoft, Color.White, 0.3f);
            Controls.Add(Tabs);
            Controls.Add(View);
            Controls.Add(GitHubLink);
            GitHubLink.Visible = false;
            session.Changed += ShowRelease;
            ShowRelease();
        }

        void ShowRelease()
        {
            var release = Session.Latest;
            var game = Session.Game;
            string message = null;
            if (release == null)
            {
                if (!game.IsPublished) message = "Patch notes appear here once " + game.Name + " has its first build.";
                else if (!Session.Checked) message = "Checking for updates…";
                else message = Session.Error ?? "No builds have been published yet. Check back soon!";
            }
            // Session changes on every download tick; only rebuild the view when there's something new to show.
            var key = release != null ? "release:" + release.Tag : "message:" + message;
            if (key == shownKey) return;
            shownKey = key;

            if (release == null)
            {
                title = game.Name;
                subtitle = "Release notes for the latest build";
                View.SetMarkdown(null, message);
                GitHubLink.Visible = false;
            }
            else
            {
                title = string.IsNullOrWhiteSpace(release.Name) ? release.Tag : release.Name;
                subtitle = release.Tag;
                if (release.PublishedAt != DateTime.MinValue) subtitle += "  ·  " + Format.Date(release.PublishedAt);
                View.SetMarkdown(release.Notes, "No notes for this build.");
                GitHubLink.Tag = game.ReleasesUrl + "/tag/" + Uri.EscapeDataString(release.Tag);
                GitHubLink.Visible = true;
            }
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            Tabs.FitSize();
            Tabs.Location = new Point(Ui.S(32), Ui.S(GamePage.TabsTop));
            View.SetBounds(Ui.S(36), Ui.S(176), Width - Ui.S(72), Height - Ui.S(176) - Ui.S(28));
            GitHubLink.FitSize();
            GitHubLink.Location = new Point(Width - Ui.S(36) - GitHubLink.Width, Ui.S(140) - Ui.S(2));
            base.OnLayout(e);
        }

        public override void Rescale() { View.Relayout(); base.Rescale(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var tf = Ui.Font(Face.DisplaySemibold, 26f);
            var right = Width - Ui.S(36);
            Ui.Draw(g, title, tf, new Rectangle(Ui.S(36), Ui.S(100), right - Ui.S(36), Ui.Measure("Ag", tf).Height), Theme.Text);
            var linkLeft = GitHubLink.Visible ? GitHubLink.Left - Ui.S(16) : right;
            Ui.Draw(g, subtitle, Ui.Font(Face.Text, 13.5f), new Rectangle(Ui.S(38), Ui.S(140), linkLeft - Ui.S(38), Ui.S(22)), Theme.Muted);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < Ui.S(170)) Native.DragWindow(this);
        }
    }

    class SettingsPage : Page
    {
        readonly GameSession[] games;
        public readonly FlatButton[] FolderButtons, RepairButtons;
        public readonly FlatButton CheckButton = new FlatButton("Check for updates", ButtonKind.Secondary, Glyph.Refresh);
        public readonly TextLink LauncherReleasesLink = new TextLink("Launcher releases", Glyph.External);
        public readonly List<TextLink> ReleaseLinks = new List<TextLink>();

        const int GameCardHeight = 88, AboutHeight = 104, Gap = 12;

        public SettingsPage(GameSession[] games)
        {
            this.games = games;
            SuspendLayout(); // OnLayout needs every control in place
            FolderButtons = new FlatButton[games.Length];
            RepairButtons = new FlatButton[games.Length];
            for (var i = 0; i < games.Length; i++)
            {
                FolderButtons[i] = new FlatButton("Open folder", ButtonKind.Secondary, Glyph.Folder);
                RepairButtons[i] = new FlatButton("Repair", ButtonKind.Secondary, Glyph.Repair);
                Controls.Add(FolderButtons[i]);
                Controls.Add(RepairButtons[i]);
                games[i].Changed += UpdateState;
                if (games[i].Game.IsPublished)
                {
                    var link = new TextLink(games[i].Game.Name + " releases", Glyph.External) { Tag = games[i].Game.ReleasesUrl };
                    ReleaseLinks.Add(link);
                    Controls.Add(link);
                }
            }
            Controls.Add(CheckButton);
            LauncherReleasesLink.Tag = "https://github.com/" + LauncherConfig.LauncherRepoOwner + "/" + LauncherConfig.LauncherRepoName + "/releases";
            Controls.Add(LauncherReleasesLink);
            ResumeLayout(false);
            UpdateState();
        }

        void UpdateState()
        {
            for (var i = 0; i < games.Length; i++)
            {
                FolderButtons[i].Enabled = games[i].Game.IsPublished || Directory.Exists(games[i].Installer.GameDir);
                RepairButtons[i].Enabled = games[i].CanRepair;
            }
            var anyIdle = false;
            foreach (var s in games) if (s.Game.IsPublished && s.IsIdle) anyIdle = true;
            CheckButton.Enabled = anyIdle;
            Invalidate();
        }

        int UpdatesHeight { get { return 52 + 22 * games.Length; } }

        Rectangle Card(int i)
        {
            var y = Ui.S(130);
            for (var k = 0; k < i; k++) y += Ui.S(CardHeight(k)) + Ui.S(Gap);
            return new Rectangle(Ui.S(36), y, Width - Ui.S(72), Ui.S(CardHeight(i)));
        }

        int CardHeight(int i) { return i < games.Length ? GameCardHeight : i == games.Length ? UpdatesHeight : AboutHeight; }
        int CardCount { get { return games.Length + 2; } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            var h = Ui.S(38);
            for (var i = 0; i < games.Length; i++)
            {
                var c = Card(i);
                var repair = RepairButtons[i];
                var folder = FolderButtons[i];
                repair.Size = new Size(repair.PreferredWidth(16), h);
                folder.Size = new Size(folder.PreferredWidth(16), h);
                repair.Location = new Point(c.Right - Ui.S(24) - repair.Width, c.Y + (c.Height - h) / 2);
                folder.Location = new Point(repair.Left - Ui.S(10) - folder.Width, repair.Top);
            }
            var cu = Card(games.Length);
            CheckButton.Size = new Size(CheckButton.PreferredWidth(16), h);
            CheckButton.Location = new Point(cu.Right - Ui.S(24) - CheckButton.Width, cu.Y + (cu.Height - h) / 2);

            var ca = Card(games.Length + 1);
            var x = ca.X + Ui.S(22);
            foreach (var link in ReleaseLinks)
            {
                link.FitSize();
                link.Location = new Point(x, ca.Bottom - Ui.S(18) - link.Height);
                x = link.Right + Ui.S(20);
            }
            LauncherReleasesLink.FitSize();
            LauncherReleasesLink.Location = new Point(x, ca.Bottom - Ui.S(18) - LauncherReleasesLink.Height);
            base.OnLayout(e);
        }

        public override void PaintBackdrop(Graphics g, Rectangle rect)
        {
            base.PaintBackdrop(g, rect);
            for (var i = 0; i < CardCount; i++)
            {
                var c = Card(i);
                if (!c.IntersectsWith(rect)) continue;
                var cf = new RectangleF(c.X + 0.5f, c.Y + 0.5f, c.Width - 1, c.Height - 1);
                Theme.FillRounded(g, Theme.Surface, cf, Ui.S(12));
                Theme.StrokeRounded(g, Theme.Border, cf, Ui.S(12));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            DrawHeader(g, "Settings", "Manage your games and the launcher");
            var title = Ui.Font(Face.TextSemibold, 15f);
            var body = Ui.Font(Face.Text, 13f);
            var th = Ui.Measure("Ag", title).Height;
            var bh = Ui.Measure("Ag", body).Height;

            for (var i = 0; i < games.Length; i++)
            {
                var session = games[i];
                var c = Card(i);
                var iconSize = Ui.S(40);
                var icon = session.Game.Icon;
                if (icon != null) g.DrawImage(icon, new Rectangle(c.X + Ui.S(22), c.Y + (c.Height - iconSize) / 2, iconSize, iconSize));
                var x = c.X + Ui.S(22) + iconSize + Ui.S(16);
                var textRight = FolderButtons[i].Left - Ui.S(20);
                var y = c.Y + (c.Height - th - bh * 2 - Ui.S(10)) / 2;
                Ui.Draw(g, session.Game.Name, title, new Point(x, y), Theme.Text);
                var installed = session.InstalledTag;
                var line = installed != null ? "Installed version: " + installed : session.Game.IsPublished ? "Not installed yet" : "Not released yet";
                Ui.Draw(g, line, body, new Rectangle(x, y + th + Ui.S(6), textRight - x, bh), Theme.TextDim);
                Ui.Draw(g, session.Installer.GameDir, body, new Rectangle(x, y + th + Ui.S(10) + bh, textRight - x, bh), Theme.Muted, TextFormatFlags.PathEllipsis);
            }

            var cu = Card(games.Length);
            var ux = cu.X + Ui.S(24);
            var uRight = CheckButton.Left - Ui.S(20);
            var uy = cu.Y + (cu.Height - th - Ui.S(6) - games.Length * Ui.S(22) + Ui.S(4)) / 2;
            Ui.Draw(g, "Updates", title, new Point(ux, uy), Theme.Text);
            uy += th + Ui.S(6);
            var nameFont = Ui.Font(Face.TextSemibold, 13f);
            foreach (var session in games)
            {
                var name = session.Game.Name;
                var nw = Ui.Measure(name, nameFont).Width;
                Ui.Draw(g, name, nameFont, new Point(ux, uy), Theme.TextDim);
                Ui.Draw(g, session.UpdatesSummary(), body, new Rectangle(ux + nw + Ui.S(10), uy, uRight - ux - nw - Ui.S(10), bh), Theme.Muted);
                uy += Ui.S(22);
            }

            var ca = Card(games.Length + 1);
            var ay = ca.Y + Ui.S(18);
            var ax = ca.X + Ui.S(24);
            Ui.Draw(g, "About", title, new Point(ax, ay), Theme.Text);
            var version = LauncherConfig.ProductName + " " + LauncherUpdater.CurrentVersion;
            var vs = Ui.Measure(version, body);
            Ui.Draw(g, version, body, new Point(ca.Right - Ui.S(24) - vs.Width, ay + (th - bh) / 2), Theme.Muted);
            Ui.Draw(g, "Installs, updates and launches games from " + LauncherConfig.StudioName + ". Keeps itself up to date.", body,
                new Rectangle(ax, ay + th + Ui.S(6), ca.Right - Ui.S(24) - ax, bh), Theme.TextDim);
        }
    }

    /// <summary>Dark confirmation dialog matching the launcher (replaces MessageBox).</summary>
    class ConfirmDialog : Form, IBackdrop
    {
        readonly string title, message;
        readonly FlatButton okButton, cancelButton;
        readonly bool danger;

        public ConfirmDialog(string title, string message, string okText, string cancelText, bool danger = false)
        {
            this.title = title;
            this.message = message;
            this.danger = danger;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface;
            Text = title;
            KeyPreview = true;
            okButton = new FlatButton(okText, ButtonKind.Primary);
            cancelButton = new FlatButton(cancelText, ButtonKind.Secondary);
            okButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            LayoutDialog();
        }

        void LayoutDialog()
        {
            var w = Ui.S(440);
            var pad = Ui.S(28);
            var msgFont = Ui.Font(Face.Text, 14f);
            var msgH = Ui.MeasureWrapped(message, msgFont, w - pad * 2);
            var titleH = Ui.Measure("Ag", Ui.Font(Face.DisplaySemibold, 20f)).Height;
            var bh = Ui.S(38);
            var h = pad + titleH + Ui.S(12) + msgH + Ui.S(28) + bh + Ui.S(24);
            ClientSize = new Size(w, h);
            okButton.Size = new Size(Math.Max(Ui.S(96), okButton.PreferredWidth(18)), bh);
            cancelButton.Size = new Size(Math.Max(Ui.S(96), cancelButton.PreferredWidth(18)), bh);
            okButton.Location = new Point(w - Ui.S(24) - okButton.Width, h - Ui.S(24) - bh);
            cancelButton.Location = new Point(okButton.Left - Ui.S(10) - cancelButton.Width, okButton.Top);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleWindow(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            cancelButton.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            base.OnKeyDown(e);
        }

        public void PaintBackdrop(Graphics g, Rectangle rect)
        {
            using (var brush = new SolidBrush(Theme.Surface)) g.FillRectangle(brush, rect);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, Width, Ui.S(3)), danger ? Theme.Accent : Theme.AccentSoft, Theme.Alpha(Theme.Accent, 0), LinearGradientMode.Horizontal))
                g.FillRectangle(brush, 0, 0, Width, Ui.S(3));
            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            var pad = Ui.S(28);
            var tf = Ui.Font(Face.DisplaySemibold, 20f);
            Ui.Draw(g, title, tf, new Point(pad, pad), Theme.Text);
            var y = pad + Ui.Measure("Ag", tf).Height + Ui.S(12);
            Ui.DrawWrapped(g, message, Ui.Font(Face.Text, 14f), new Rectangle(pad, y, Width - pad * 2, okButton.Top - y), Theme.TextDim, false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Native.DragWindow(this);
            base.OnMouseDown(e);
        }

        public static bool Ask(IWin32Window owner, string title, string message, string okText, string cancelText, bool danger = false)
        {
            using (var dialog = new ConfirmDialog(title, message, okText, cancelText, danger))
            {
                if (Resources.AppIcon != null) dialog.Icon = Resources.AppIcon;
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }
}
