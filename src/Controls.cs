using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GhostHunterLauncher
{
    static class Theme
    {
        // The game's burnt-paper diary: scorched parchment with ink on it, a charred leather bar, blood-red ink.
        public static readonly Color Background = Color.FromArgb(150, 117, 80);   // paper (behind assets\paper.png)
        public static readonly Color Panel = Color.FromArgb(126, 96, 64);         // the patch-notes inset on the page
        public static readonly Color Ink = Color.FromArgb(24, 15, 9);             // text on paper
        public static readonly Color Pencil = Color.FromArgb(58, 40, 26);         // quieter text on paper
        public static readonly Color RedInk = Color.FromArgb(122, 18, 10);
        public static readonly Color Bar = Color.FromArgb(34, 20, 13);            // charred leather along the bottom
        public static readonly Color Border = Color.FromArgb(78, 52, 34);         // progress track, disabled button
        public static readonly Color Text = Color.FromArgb(236, 222, 196);        // text on the bar
        public static readonly Color Muted = Color.FromArgb(172, 146, 112);       // quieter text on the bar
        public static readonly Color Accent = Color.FromArgb(150, 24, 14);
        public static readonly Color AccentHover = Color.FromArgb(182, 38, 24);
        public static readonly Color AccentText = Color.FromArgb(240, 226, 200);
        public static readonly Color Error = Color.FromArgb(255, 140, 120);

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>Big rounded call-to-action button (Install / Update / Play).</summary>
    class ActionButton : Control
    {
        bool hover;

        public ActionButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var fill = !Enabled ? Theme.Border : hover ? Theme.AccentHover : Theme.Accent;
            var textColor = Enabled ? Theme.AccentText : Theme.Muted;
            using (var path = Theme.Rounded(rect, 8))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, Text, Font, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    class SlimProgressBar : Control
    {
        double value;

        public SlimProgressBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public double Value
        {
            get { return value; }
            set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            var radius = Math.Max(1, Height / 2);
            using (var track = Theme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius))
            using (var brush = new SolidBrush(Theme.Border))
                g.FillPath(brush, track);
            var fillWidth = (int)((Width - 1) * value);
            if (fillWidth >= Height)
            {
                using (var bar = Theme.Rounded(new Rectangle(0, 0, fillWidth, Height - 1), radius))
                using (var brush = new SolidBrush(Theme.Accent))
                    g.FillPath(brush, bar);
            }
        }
    }

    /// <summary>Painted header: the Who Haunts logo and "early access" in red ink, on the window's burnt paper.</summary>
    class HeaderPanel : Panel
    {
        readonly Image logo = Resources.FullLogo;

        public HeaderPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // The same paper as the window behind it, so the header has no seam.
            var form = FindForm();
            if (Resources.Paper != null && form != null)
                g.DrawImage(Resources.Paper, new Rectangle(-Left, -Top, form.ClientSize.Width, form.ClientSize.Height));
            else
                g.Clear(Theme.Background);

            var scale = DeviceDpi / 96f;
            var x = (int)(28 * scale);
            if (logo != null)
            {
                var height = Height - (int)(10 * scale);
                var width = height * logo.Width / logo.Height;
                g.DrawImage(logo, new Rectangle(x, (Height - height) / 2, width, height));
                x += width + (int)(18 * scale);
            }
            using (var subFont = new Font("Georgia", 11f, FontStyle.Bold | FontStyle.Italic))
                TextRenderer.DrawText(g, LauncherConfig.Subtitle, subFont, new Point(x, Height - (int)(40 * scale)), Theme.RedInk);
        }
    }

    /// <summary>Images compiled into the exe by build.ps1 (/resource).</summary>
    static class Resources
    {
        public static readonly Image FullLogo = LoadImage("GhostHunterLauncher.logo-full.png");
        public static readonly Image Paper = LoadImage("GhostHunterLauncher.paper.png");
        public static readonly Icon AppIcon = LoadIcon("GhostHunterLauncher.icon.ico");

        static Image LoadImage(string name)
        {
            var stream = typeof(Resources).Assembly.GetManifestResourceStream(name);
            return stream == null ? null : Image.FromStream(stream);
        }

        static Icon LoadIcon(string name)
        {
            using (var stream = typeof(Resources).Assembly.GetManifestResourceStream(name))
                return stream == null ? null : new Icon(stream);
        }
    }

    class LinkButton : Label
    {
        public LinkButton(string text)
        {
            Text = text;
            AutoSize = true;
            Cursor = Cursors.Hand;
            ForeColor = Theme.Pencil;
            BackColor = Color.Transparent;
            Font = new Font("Georgia", 9.5f, FontStyle.Italic);
        }

        protected override void OnMouseEnter(EventArgs e) { if (Enabled) ForeColor = Theme.RedInk; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { ForeColor = Theme.Pencil; base.OnMouseLeave(e); }
    }
}
