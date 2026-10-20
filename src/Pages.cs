using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace GhostHunterLauncher
{
    /// <summary>Left navigation: brand, page list (with a mini progress bar under Play while downloading), launcher version.</summary>
    class Sidebar : Control
    {
        public static readonly string[] Labels = { "Play", "Patch Notes", "Settings" };
        static readonly string[] Glyphs = { Glyph.Play, Glyph.Notes, Glyph.Settings };

        public event Action<int> SelectedChanged;
        readonly float[] hoverT = new float[Labels.Length];
        readonly Timer timer = new Timer { Interval = 15 };
        int hover = -1, selected;
        double progress = -1;
        string footer = "";

        public Sidebar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Sidebar;
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

        /// <summary>0..1 shows a progress bar on the Play item; negative hides it.</summary>
        public double Progress { get { return progress; } set { progress = value; Invalidate(); } }
        public string Footer { get { return footer; } set { footer = value; Invalidate(); } }

        Rectangle ItemRect(int i) { return new Rectangle(Ui.S(14), Ui.S(108) + i * Ui.S(50), Width - Ui.S(28), Ui.S(44)); }

        int ItemAt(Point p)
        {
            for (var i = 0; i < Labels.Length; i++) if (ItemRect(i).Contains(p)) return i;
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

            var logoSize = Ui.S(44);
            var logoRect = new Rectangle(Ui.S(20), Ui.S(26), logoSize, logoSize);
            if (Resources.HexLogo != null) g.DrawImage(Resources.HexLogo, logoRect);
            var tx = logoRect.Right + Ui.S(12);
            var brand = Ui.Font(Face.DisplaySemibold, 17f);
            var brandH = Ui.Measure("W", brand).Height;
            var sub = Ui.Font(Face.TextSemibold, 10.5f);
            var subH = Ui.Measure("E", sub).Height;
            var top = logoRect.Y + (logoSize - brandH - subH - Ui.S(1)) / 2;
            Ui.Draw(g, "Who Haunts", brand, new Rectangle(tx, top, Width - tx - Ui.S(8), brandH), Theme.Text);
            Ui.Draw(g, "EARLY ACCESS", sub, new Point(tx + Ui.S(1), top + brandH + Ui.S(1)), Theme.AccentSoft);

            for (var i = 0; i < Labels.Length; i++)
            {
                var r = ItemRect(i);
                var isSel = i == selected;
                var fill = isSel ? Color.FromArgb(34, 28, 31) : Theme.Alpha(Color.White, (int)(10 * hoverT[i]));
                if (fill.A > 0) Theme.FillRounded(g, fill, new RectangleF(r.X, r.Y, r.Width, r.Height), Ui.S(8));
                if (isSel)
                {
                    var bar = new RectangleF(r.X, r.Y + Ui.S(12), Ui.S(3), r.Height - Ui.S(24));
                    Theme.FillRounded(g, Theme.Accent, bar, Ui.S(1.5f));
                }
                var color = isSel ? Theme.Text : Theme.Lerp(Theme.Muted, Theme.TextDim, hoverT[i] * 0.9f + 0.1f);
                var x = r.X + Ui.S(18);
                if (Ui.HasIcons)
                {
                    var iconFont = Ui.Font(Face.Icon, 16f);
                    var isz = Ui.Measure(Glyphs[i], iconFont);
                    Ui.Draw(g, Glyphs[i], iconFont, new Point(x, r.Y + (r.Height - isz.Height) / 2 + Ui.S(1)), isSel ? Theme.AccentSoft : color);
                    x += Ui.S(30);
                }
                var font = Ui.Font(isSel ? Face.TextSemibold : Face.Text, 14f);
                var fs = Ui.Measure(Labels[i], font);
                var showBar = i == 0 && progress >= 0;
                var ty = r.Y + (r.Height - fs.Height) / 2 - (showBar ? Ui.S(3) : 0);
                Ui.Draw(g, Labels[i], font, new Point(x, ty), color);

                if (showBar)
                {
                    var pct = (int)(progress * 100) + "%";
                    var pf = Ui.Font(Face.TextSemibold, 11f);
                    var ps = Ui.Measure(pct, pf);
                    Ui.Draw(g, pct, pf, new Point(r.Right - Ui.S(12) - ps.Width, ty + (fs.Height - ps.Height) / 2), Theme.TextDim);
                    var track = new RectangleF(x, r.Bottom - Ui.S(10), r.Right - Ui.S(12) - x, Ui.S(3));
                    Theme.FillRounded(g, Theme.Alpha(Color.White, 22), track, track.Height / 2);
                    var fill2 = new RectangleF(track.X, track.Y, (float)(track.Width * progress), track.Height);
                    if (fill2.Width >= track.Height) Theme.FillRounded(g, Theme.Accent, fill2, track.Height / 2);
                }
            }

            var small = Ui.Font(Face.Text, 11.5f);
            Ui.Draw(g, footer, small, new Rectangle(Ui.S(24), Height - Ui.S(40), Width - Ui.S(36), Ui.S(20)), Theme.Muted);
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

    /// <summary>Common page chrome: dark background with a faint red glow at the top, a big title and subtitle.</summary>
    abstract class Page : Control, IBackdrop
    {
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
                using (var brush = new LinearGradientBrush(new Rectangle(0, -1, Width, glow.Height + 2), Color.FromArgb(30, 170, 26, 18), Color.FromArgb(0, 170, 26, 18), LinearGradientMode.Vertical))
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

    /// <summary>Home: hero art with the logo, a "what's new" card and the play bar.</summary>
    class PlayPage : Page
    {
        public readonly PlayBar Bar = new PlayBar();
        public readonly TextLink NotesLink = new TextLink("Read full patch notes", Glyph.Forward);
        Bitmap surface;
        ReleaseInfo release;
        string message;
        List<string> highlights = new List<string>();

        public PlayPage()
        {
            Controls.Add(Bar);
            Controls.Add(NotesLink);
        }

        public void SetRelease(ReleaseInfo r, string errorMessage)
        {
            release = r;
            message = errorMessage;
            highlights = r != null && !string.IsNullOrWhiteSpace(r.Notes) ? Markdown.Highlights(r.Notes, 4) : new List<string>();
            NotesLink.Visible = r != null;
            InvalidateSurface();
        }

        Rectangle LogoRect
        {
            get
            {
                var card = CardRect;
                var h = Math.Min(Ui.S(300), card.Height);
                var w = Resources.FullLogo != null ? h * Resources.FullLogo.Width / Resources.FullLogo.Height : h;
                var x = Math.Max(Ui.S(24), (card.X - w) / 2);
                return new Rectangle(x, card.Y + (card.Height - h) / 2 - Ui.S(6), w, h);
            }
        }

        Rectangle CardRect
        {
            get
            {
                var w = Ui.S(318);
                return new Rectangle(Width - Ui.S(32) - w, Ui.S(78), w, Bar.Top - Ui.S(78) - Ui.S(34));
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            var barH = Ui.S(132);
            Bar.SetBounds(0, Height - barH, Width, barH);
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
            surface = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(surface))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Theme.Background);
                var full = new Rectangle(0, 0, Width, Height);

                // The diary page, burnt almost to black and stained red: a nod to the old launcher and the game's journal.
                if (Resources.Paper != null)
                {
                    var src = CoverCrop(Resources.Paper.Size, full.Size);
                    var m = new ColorMatrix(new[]
                    {
                        new[] { 0.16f, 0.06f, 0.06f, 0, 0 },
                        new[] { 0.16f, 0.06f, 0.06f, 0, 0 },
                        new[] { 0.05f, 0.02f, 0.02f, 0, 0 },
                        new[] { 0f, 0, 0, 1, 0 },
                        new[] { 0f, 0, 0, 0, 1 },
                    });
                    Resources.DrawWithMatrix(g, Resources.Paper, full, src, m);
                }

                var logo = LogoRect;
                var glowCenter = new PointF(logo.X + logo.Width * 0.62f, logo.Y + logo.Height * 0.38f);
                using (var path = new GraphicsPath())
                {
                    var r = new RectangleF(glowCenter.X - Ui.S(420), glowCenter.Y - Ui.S(330), Ui.S(840), Ui.S(660));
                    path.AddEllipse(r);
                    using (var brush = new PathGradientBrush(path) { CenterPoint = glowCenter, CenterColor = Color.FromArgb(120, 170, 22, 14), SurroundColors = new[] { Color.FromArgb(0, 120, 10, 8) } })
                        g.FillRectangle(brush, r);
                }

                // Vignette: darker at the top (under the title bar) and fading into the play bar at the bottom.
                using (var brush = new LinearGradientBrush(new Rectangle(0, 0, Width, Ui.S(110)), Color.FromArgb(140, 6, 5, 6), Color.FromArgb(0, 6, 5, 6), LinearGradientMode.Vertical))
                    g.FillRectangle(brush, 0, 0, Width, Ui.S(110));
                var fadeTop = Bar.Top - Ui.S(170);
                var fadeBottom = Bar.Top + Ui.S(30);
                using (var brush = new LinearGradientBrush(new Rectangle(0, fadeTop - 1, Width, fadeBottom - fadeTop + 2), Theme.Alpha(Theme.Background, 0), Theme.Background, LinearGradientMode.Vertical))
                    g.FillRectangle(brush, 0, fadeTop, Width, fadeBottom - fadeTop);
                using (var brush = new SolidBrush(Theme.Background)) g.FillRectangle(brush, 0, fadeBottom, Width, Height);

                if (Resources.FullLogo != null)
                {
                    // Soft shadow so the cream lettering lifts off the glow.
                    var shadow = new ColorMatrix(new[]
                    {
                        new[] { 0f, 0, 0, 0, 0 }, new[] { 0f, 0, 0, 0, 0 }, new[] { 0f, 0, 0, 0, 0 },
                        new[] { 0f, 0, 0, 0.22f, 0 }, new[] { 0f, 0, 0, 0, 1 },
                    });
                    var src = new Rectangle(0, 0, Resources.FullLogo.Width, Resources.FullLogo.Height);
                    foreach (var o in new[] { new Point(0, 6), new Point(4, 8), new Point(-4, 8), new Point(0, 12) })
                    {
                        var d = logo;
                        d.Offset(Ui.S(o.X), Ui.S(o.Y));
                        Resources.DrawWithMatrix(g, Resources.FullLogo, d, src, shadow);
                    }
                    g.DrawImage(Resources.FullLogo, logo);
                }

                // "What's new" card, translucent over the art.
                var card = CardRect;
                var cardF = new RectangleF(card.X + 0.5f, card.Y + 0.5f, card.Width - 1, card.Height - 1);
                Theme.FillRounded(g, Color.FromArgb(178, 16, 13, 15), cardF, Ui.S(14));
                Theme.StrokeRounded(g, Theme.Alpha(Color.White, 20), cardF, Ui.S(14));
                using (var brush = new LinearGradientBrush(new RectangleF(card.X, card.Y, card.Width, Ui.S(3)), Theme.Accent, Theme.Alpha(Theme.Accent, 0), LinearGradientMode.Horizontal))
                using (var path = Theme.Rounded(new RectangleF(card.X + Ui.S(22), card.Y, card.Width - Ui.S(44), Ui.S(2)), Ui.S(1)))
                    g.FillPath(brush, path);
            }
            return surface;
        }

        static Rectangle CoverCrop(Size image, Size target)
        {
            var scale = Math.Max(target.Width / (double)image.Width, target.Height / (double)image.Height);
            var w = (int)(target.Width / scale);
            var h = (int)(target.Height / scale);
            return new Rectangle((image.Width - w) / 2, (image.Height - h) / 2, w, h);
        }

        public override void PaintBackdrop(Graphics g, Rectangle rect)
        {
            var s = Surface();
            var state = g.Save();
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(s, rect, rect, GraphicsUnit.Pixel);
            g.Restore(state);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = CardRect;
            var x = card.X + Ui.S(24);
            var w = card.Width - Ui.S(48);
            var y = card.Y + Ui.S(24);
            Ui.Draw(g, release != null ? "WHAT'S NEW" : "PATCH NOTES", Ui.Font(Face.TextSemibold, 11f), new Point(x, y), Theme.AccentSoft);
            y += Ui.S(24);
            if (release == null)
            {
                var text = message ?? "Patch notes appear here once the launcher reaches the update server.";
                Ui.DrawWrapped(g, text, Ui.Font(Face.Text, 13.5f), new Rectangle(x, y, w, card.Bottom - y - Ui.S(20)), Theme.TextDim);
                return;
            }
            var title = string.IsNullOrWhiteSpace(release.Name) ? release.Tag : release.Name;
            var tf = Ui.Font(Face.DisplaySemibold, 20f);
            var th = Math.Min(Ui.MeasureWrapped(title, tf, w), Ui.Measure("Ag", tf).Height * 2);
            Ui.DrawWrapped(g, title, tf, new Rectangle(x, y, w, th), Theme.Text);
            y += th + Ui.S(4);
            var meta = release.Tag;
            if (release.PublishedAt != DateTime.MinValue) meta += "  ·  " + release.PublishedAt.ToLocalTime().ToString("MMMM d, yyyy");
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
                using (var brush = new SolidBrush(Theme.Accent)) g.FillEllipse(brush, x + Ui.S(1), y + (lineH - d) / 2 + Ui.S(1), d, d);
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

    /// <summary>Bottom of the home page: status, progress, versions and the big action button.</summary>
    class PlayBar : Control, IBackdrop
    {
        public readonly FlatButton ActionButton = new FlatButton("PLEASE WAIT", ButtonKind.Primary)
        {
            FontPx = 19f, Radius = 10f, TextFace = Face.DisplaySemibold,
        };
        string status = "", versions = "", detail = "";
        bool isError;
        double progress = -1;

        public PlayBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Controls.Add(ActionButton);
        }

        public void SetStatus(string text, bool error) { status = text ?? ""; isError = error; Invalidate(); }
        public string Status { get { return status; } }
        public string Versions { get { return versions; } set { versions = value ?? ""; Invalidate(); } }
        /// <summary>Replaces the versions line while set (download size, speed and time left).</summary>
        public string Detail { get { return detail; } set { detail = value ?? ""; Invalidate(); } }
        public double Progress { get { return progress; } set { progress = value; Invalidate(); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            var w = Ui.S(220);
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
            var showBar = progress >= 0;
            var y = ActionButton.Top + (showBar ? Ui.S(2) : Ui.S(8));
            string pct = null;
            var pctW = 0;
            if (showBar)
            {
                pct = (int)(progress * 100) + "%";
                pctW = Ui.Measure(pct, sf).Width + Ui.S(12);
                Ui.Draw(g, pct, sf, new Point(right - pctW + Ui.S(12), y), Theme.Text);
            }
            if (isError && Ui.HasIcons)
            {
                var wf = Ui.Font(Face.Icon, 15f);
                var ws = Ui.Measure(Glyph.Warning, wf);
                Ui.Draw(g, Glyph.Warning, wf, new Point(x, y + (sh - ws.Height) / 2 + Ui.S(1)), Theme.Error);
                x += ws.Width + Ui.S(8);
            }
            Ui.Draw(g, status, sf, new Rectangle(x, y, right - x - pctW, sh), isError ? Theme.Error : Theme.Text);
            y += sh + Ui.S(8);
            x = Ui.S(36);

            if (showBar)
            {
                var track = new RectangleF(x, y, right - x, Ui.S(6));
                Theme.FillRounded(g, Theme.Alpha(Color.White, 24), track, track.Height / 2);
                var fw = (float)(track.Width * progress);
                if (fw >= track.Height)
                {
                    var fill = new RectangleF(track.X, track.Y, fw, track.Height);
                    using (var path = Theme.Rounded(fill, track.Height / 2))
                    using (var brush = new LinearGradientBrush(new RectangleF(track.X - 1, track.Y, track.Width + 2, track.Height), Theme.AccentPressed, Theme.AccentHover, LinearGradientMode.Horizontal))
                        g.FillPath(brush, path);
                }
                y += Ui.S(16);
            }
            var hasDetail = detail.Length > 0;
            Ui.Draw(g, hasDetail ? detail : versions, Ui.Font(Face.Text, 12.5f), new Rectangle(x, y, right - x, Ui.S(20)), hasDetail ? Theme.TextDim : Theme.Muted);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Native.DragWindow(this);
            base.OnMouseDown(e);
        }
    }

    class NotesPage : Page
    {
        public readonly MarkdownView View = new MarkdownView();
        public readonly TextLink GitHubLink = new TextLink("View on GitHub", Glyph.External);
        string subtitle = "";

        public NotesPage()
        {
            Controls.Add(View);
            Controls.Add(GitHubLink);
            GitHubLink.Visible = false;
            View.SetMarkdown(null, "Checking for updates…");
        }

        public void SetRelease(ReleaseInfo release, string errorMessage)
        {
            if (release == null)
            {
                subtitle = "Release notes for the latest build";
                View.SetMarkdown(null, errorMessage ?? "No builds have been published yet. Check back soon!");
                GitHubLink.Visible = false;
            }
            else
            {
                subtitle = string.IsNullOrWhiteSpace(release.Name) ? release.Tag : release.Name;
                if (!string.IsNullOrWhiteSpace(release.Name) && release.Name.IndexOf(release.Tag, StringComparison.OrdinalIgnoreCase) < 0) subtitle += "  ·  " + release.Tag;
                if (release.PublishedAt != DateTime.MinValue) subtitle += "  ·  " + release.PublishedAt.ToLocalTime().ToString("MMMM d, yyyy");
                View.SetMarkdown(release.Notes, "No notes for this build.");
                var url = "https://github.com/" + LauncherConfig.RepoOwner + "/" + LauncherConfig.RepoName + "/releases/tag/" + Uri.EscapeDataString(release.Tag);
                GitHubLink.Tag = url;
                GitHubLink.Visible = true;
            }
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            View.SetBounds(Ui.S(36), Ui.S(130), Width - Ui.S(72), Height - Ui.S(130) - Ui.S(32));
            GitHubLink.FitSize();
            GitHubLink.Location = new Point(Width - Ui.S(36) - GitHubLink.Width, Ui.S(92) - Ui.S(2));
            base.OnLayout(e);
        }

        public override void Rescale() { View.Relayout(); base.Rescale(); }

        protected override void OnPaint(PaintEventArgs e) { DrawHeader(e.Graphics, "Patch Notes", subtitle); }
    }

    class SettingsPage : Page
    {
        public readonly FlatButton OpenFolderButton = new FlatButton("Open folder", ButtonKind.Secondary, Glyph.Folder);
        public readonly FlatButton RepairButton = new FlatButton("Repair", ButtonKind.Secondary, Glyph.Repair);
        public readonly FlatButton CheckButton = new FlatButton("Check for updates", ButtonKind.Secondary, Glyph.Refresh);
        public readonly TextLink GameReleasesLink = new TextLink("Game releases", Glyph.External);
        public readonly TextLink LauncherReleasesLink = new TextLink("Launcher releases", Glyph.External);

        public string InstallPath = "";
        public string InstalledText = "";
        public string UpdatesText = "";
        public string UpdatesDetail = "";

        public SettingsPage()
        {
            Controls.AddRange(new Control[] { OpenFolderButton, RepairButton, CheckButton, GameReleasesLink, LauncherReleasesLink });
            GameReleasesLink.Tag = "https://github.com/" + LauncherConfig.RepoOwner + "/" + LauncherConfig.RepoName + "/releases";
            LauncherReleasesLink.Tag = "https://github.com/" + LauncherConfig.LauncherRepoOwner + "/" + LauncherConfig.LauncherRepoName + "/releases";
        }

        Rectangle Card(int i)
        {
            var heights = new[] { 112, 96, 112 };
            var y = Ui.S(130);
            for (var k = 0; k < i; k++) y += Ui.S(heights[k]) + Ui.S(14);
            return new Rectangle(Ui.S(36), y, Width - Ui.S(72), Ui.S(heights[i]));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            var h = Ui.S(38);
            foreach (var b in new[] { OpenFolderButton, RepairButton, CheckButton }) b.Size = new Size(b.PreferredWidth(16), h);
            var c0 = Card(0);
            RepairButton.Location = new Point(c0.Right - Ui.S(24) - RepairButton.Width, c0.Y + (c0.Height - h) / 2);
            OpenFolderButton.Location = new Point(RepairButton.Left - Ui.S(10) - OpenFolderButton.Width, RepairButton.Top);
            var c1 = Card(1);
            CheckButton.Location = new Point(c1.Right - Ui.S(24) - CheckButton.Width, c1.Y + (c1.Height - h) / 2);
            var c2 = Card(2);
            GameReleasesLink.FitSize();
            LauncherReleasesLink.FitSize();
            GameReleasesLink.Location = new Point(c2.X + Ui.S(22), c2.Bottom - Ui.S(18) - GameReleasesLink.Height);
            LauncherReleasesLink.Location = new Point(GameReleasesLink.Right + Ui.S(20), GameReleasesLink.Top);
            base.OnLayout(e);
        }

        public override void PaintBackdrop(Graphics g, Rectangle rect)
        {
            base.PaintBackdrop(g, rect);
            for (var i = 0; i < 3; i++)
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
            DrawHeader(g, "Settings", "Manage your Who Haunts installation");
            var title = Ui.Font(Face.TextSemibold, 15f);
            var body = Ui.Font(Face.Text, 13f);
            var th = Ui.Measure("Ag", title).Height;
            var bh = Ui.Measure("Ag", body).Height;

            var c0 = Card(0);
            var textRight = OpenFolderButton.Left - Ui.S(20);
            var x = c0.X + Ui.S(24);
            var y = c0.Y + (c0.Height - th - bh * 2 - Ui.S(10)) / 2;
            Ui.Draw(g, "Game files", title, new Point(x, y), Theme.Text);
            Ui.Draw(g, InstalledText, body, new Rectangle(x, y + th + Ui.S(6), textRight - x, bh), Theme.TextDim);
            Ui.Draw(g, InstallPath, body, new Rectangle(x, y + th + Ui.S(10) + bh, textRight - x, bh), Theme.Muted, TextFormatFlags.PathEllipsis);

            var c1 = Card(1);
            textRight = CheckButton.Left - Ui.S(20);
            y = c1.Y + (c1.Height - th - bh - Ui.S(6)) / 2;
            Ui.Draw(g, "Updates", title, new Point(x, y), Theme.Text);
            Ui.Draw(g, UpdatesText, body, new Rectangle(x, y + th + Ui.S(6), textRight - x, bh), Theme.TextDim);

            var c2 = Card(2);
            y = c2.Y + Ui.S(20);
            Ui.Draw(g, "About", title, new Point(x, y), Theme.Text);
            var version = "Who Haunts Launcher " + LauncherUpdater.CurrentVersion;
            var vs = Ui.Measure(version, body);
            Ui.Draw(g, version, body, new Point(c2.Right - Ui.S(24) - vs.Width, y + (th - bh) / 2), Theme.Muted);
            Ui.Draw(g, "Installs, updates and launches Who Haunts early access builds. Keeps itself up to date.", body,
                new Rectangle(x, y + th + Ui.S(6), c2.Right - Ui.S(24) - x, bh), Theme.TextDim);
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
