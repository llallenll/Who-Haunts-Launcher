using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GhostHunterLauncher
{
    static class Theme
    {
        // Near-black with the logo's blood red. Warm off-white text, like the "Who" lettering.
        // Not (13,11,12): that exact colour came out transparent/black in the window and in captures (seen on Windows 11 26200).
        public static readonly Color Background = Color.FromArgb(14, 12, 13);
        public static readonly Color Sidebar = Color.FromArgb(18, 15, 16);
        public static readonly Color Surface = Color.FromArgb(23, 19, 21);      // cards
        public static readonly Color SurfaceRaised = Color.FromArgb(36, 30, 33); // secondary buttons
        public static readonly Color SurfaceHover = Color.FromArgb(50, 42, 46);
        public static readonly Color Border = Color.FromArgb(42, 35, 38);
        public static readonly Color Text = Color.FromArgb(243, 236, 228);
        public static readonly Color TextDim = Color.FromArgb(196, 186, 182);
        public static readonly Color Muted = Color.FromArgb(132, 120, 121);
        public static readonly Color Accent = Color.FromArgb(198, 36, 28);
        public static readonly Color AccentHover = Color.FromArgb(222, 54, 40);
        public static readonly Color AccentPressed = Color.FromArgb(160, 26, 20);
        public static readonly Color AccentSoft = Color.FromArgb(240, 104, 88);  // red text on dark
        public static readonly Color AccentText = Color.FromArgb(255, 247, 242);
        public static readonly Color Disabled = Color.FromArgb(44, 38, 41);
        public static readonly Color Error = Color.FromArgb(255, 128, 110);

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
        {
            using (var path = Rounded(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void StrokeRounded(Graphics g, Color color, RectangleF r, float radius)
        {
            using (var path = Rounded(r, radius))
            using (var pen = new Pen(color, 1f))
                g.DrawPath(pen, path);
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c); }
    }

    enum Face { Text, TextSemibold, Display, DisplaySemibold, Mono, Icon }

    /// <summary>
    /// DPI scaling and fonts. Everything is laid out in 96-DPI "logical" pixels and scaled with S(), and fonts are
    /// created in pixels from the same factor, so the window is crisp at any DPI and re-scales on WM_DPICHANGED.
    /// </summary>
    static class Ui
    {
        public static float Scale = 1f;
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        static readonly Dictionary<Face, string> families = new Dictionary<Face, string>();

        static Ui()
        {
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var collection = new InstalledFontCollection())
                    foreach (var family in collection.Families) installed.Add(family.Name);
            }
            catch { }
            Func<string[], string> pick = names =>
            {
                foreach (var n in names) if (installed.Contains(n)) return n;
                return null;
            };
            families[Face.Text] = pick(new[] { "Segoe UI Variable Text", "Segoe UI" }) ?? FontFamily.GenericSansSerif.Name;
            families[Face.TextSemibold] = pick(new[] { "Segoe UI Variable Text Semibold", "Segoe UI Semibold" }) ?? families[Face.Text];
            families[Face.Display] = pick(new[] { "Segoe UI Variable Display", "Segoe UI" }) ?? families[Face.Text];
            families[Face.DisplaySemibold] = pick(new[] { "Segoe UI Variable Display Semib", "Segoe UI Semibold" }) ?? families[Face.TextSemibold];
            families[Face.Mono] = pick(new[] { "Cascadia Mono", "Consolas", "Courier New" }) ?? FontFamily.GenericMonospace.Name;
            families[Face.Icon] = pick(new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" });
        }

        public static bool HasIcons { get { return families[Face.Icon] != null; } }

        public static int S(float logical) { return (int)Math.Round(logical * Scale); }

        public static Font Font(Face face, float logicalPx, FontStyle style = FontStyle.Regular)
        {
            var family = families[face] ?? families[Face.Text];
            var px = Math.Max(6f, logicalPx * Scale);
            var key = family + "|" + px.ToString("0.00") + "|" + (int)style;
            Font font;
            if (fonts.TryGetValue(key, out font)) return font;
            try { font = new Font(family, px, style, GraphicsUnit.Pixel); }
            catch { font = new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel); }
            fonts[key] = font;
            return font;
        }

        /// <summary>Called on a DPI change. Old fonts are left to the GC: a paint may still be using one.</summary>
        public static void SetScale(float scale)
        {
            Scale = scale;
            fonts.Clear();
        }

        public const TextFormatFlags Line = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        static Graphics measureGraphics;

        /// <summary>Exact GDI text size. (Only the IDeviceContext overload of MeasureText honours NoPadding.)</summary>
        public static Size Measure(string text, Font font)
        {
            if (measureGraphics == null) measureGraphics = Graphics.FromHwnd(IntPtr.Zero);
            if (string.IsNullOrEmpty(text)) return new Size(0, TextRenderer.MeasureText(measureGraphics, "Ag", font, new Size(int.MaxValue, int.MaxValue), Line).Height);
            return TextRenderer.MeasureText(measureGraphics, text, font, new Size(int.MaxValue, int.MaxValue), Line);
        }

        /// <summary>Height of word-wrapped text in a box of the given width (same flags DrawWrapped uses).</summary>
        public static int MeasureWrapped(string text, Font font, int width)
        {
            if (measureGraphics == null) measureGraphics = Graphics.FromHwnd(IntPtr.Zero);
            return TextRenderer.MeasureText(measureGraphics, text, font, new Size(Math.Max(1, width), int.MaxValue), Wrapped).Height;
        }

        public const TextFormatFlags Wrapped = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

        public static void DrawWrapped(Graphics g, string text, Font font, Rectangle box, Color color, bool ellipsis = true)
        {
            TextRenderer.DrawText(g, text, font, box, color, Wrapped | (ellipsis ? TextFormatFlags.EndEllipsis : 0));
        }

        public static void Draw(Graphics g, string text, Font font, Point at, Color color)
        {
            TextRenderer.DrawText(g, text, font, at, color, Line);
        }

        public static void Draw(Graphics g, string text, Font font, Rectangle box, Color color, TextFormatFlags extra = TextFormatFlags.Default)
        {
            TextRenderer.DrawText(g, text, font, box, color, Line | TextFormatFlags.EndEllipsis | extra);
        }

        public static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }

    /// <summary>Segoe Fluent Icons / MDL2 Assets code points.</summary>
    static class Glyph
    {
        public const string Play = "";
        public const string Notes = "";
        public const string Settings = "";
        public const string Download = "";
        public const string Refresh = "";
        public const string Cancel = "";
        public const string Minimize = "";
        public const string Close = "";
        public const string Folder = "";
        public const string Repair = "";
        public const string External = "";
        public const string Forward = "";
        public const string Warning = "";
    }

    static class Native
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);

        public const int WM_DPICHANGED = 0x02E0;

        /// <summary>Dark, rounded (Windows 11), DWM-shadowed borderless window. Every call is best-effort.</summary>
        public static void StyleWindow(IntPtr hwnd)
        {
            try
            {
                var on = 1;
                DwmSetWindowAttribute(hwnd, 20, ref on, 4);           // DWMWA_USE_IMMERSIVE_DARK_MODE
                var policy = 2;
                DwmSetWindowAttribute(hwnd, 2, ref policy, 4);        // DWMWA_NCRENDERING_POLICY = enabled (shadow)
                var round = 2;
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);        // DWMWA_WINDOW_CORNER_PREFERENCE = round
                var border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16);
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);       // DWMWA_BORDER_COLOR (Windows 11)
            }
            catch { } // dwmapi missing or too old: a square window without shadow is fine
        }

        /// <summary>Starts a native window drag, as if the user grabbed a title bar.</summary>
        public static void DragWindow(Control from)
        {
            var form = from.FindForm();
            if (form == null) return;
            ReleaseCapture();
            SendMessage(form.Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
        }

        public static int WindowDpi(IntPtr hwnd)
        {
            try { return (int)GetDpiForWindow(hwnd); }
            catch { return 0; } // before Windows 10 1607
        }
    }

    /// <summary>A container that can paint what is behind a child, so children can have rounded/transparent edges.</summary>
    interface IBackdrop
    {
        /// <summary>Paints this control's background for <paramref name="rect"/> (own client coordinates). GDI+ only: g may be translated.</summary>
        void PaintBackdrop(Graphics g, Rectangle rect);
    }

    static class Backdrop
    {
        public static void Paint(Control child, Graphics g, Rectangle childRect)
        {
            var parent = child.Parent;
            var backdrop = parent as IBackdrop;
            if (backdrop == null)
            {
                using (var brush = new SolidBrush(parent != null ? parent.BackColor : Theme.Background))
                    g.FillRectangle(brush, childRect);
                return;
            }
            var state = g.Save();
            g.TranslateTransform(-child.Left, -child.Top);
            var r = childRect;
            r.Offset(child.Location);
            backdrop.PaintBackdrop(g, r);
            g.Restore(state);
        }
    }

    /// <summary>Base for owner-drawn controls: double buffered, transparent over an IBackdrop parent, animated hover.</summary>
    abstract class FxControl : Control
    {
        protected float hoverT;
        protected bool hovering, pressed;
        readonly Timer timer = new Timer { Interval = 15 };

        protected FxControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            timer.Tick += (s, e) =>
            {
                var target = hovering && Enabled ? 1f : 0f;
                var step = timer.Interval / 120f;
                hoverT = hoverT < target ? Math.Min(target, hoverT + step) : Math.Max(target, hoverT - step);
                if (hoverT == target) timer.Stop();
                Invalidate();
            };
        }

        void Animate() { timer.Start(); }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; Animate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; pressed = false; Animate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            if (!Enabled) { hovering = false; pressed = false; hoverT = 0; }
            Invalidate();
            base.OnEnabledChanged(e);
        }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Backdrop.Paint(this, e.Graphics, ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    enum ButtonKind { Primary, Secondary, Ghost }

    /// <summary>Rounded button with an optional Fluent icon. Kind can change at runtime (the hero button turns into Cancel).</summary>
    class FlatButton : FxControl
    {
        ButtonKind kind;
        string icon;
        public float FontPx = 13.5f;
        public float Radius = 6f;
        public Face TextFace = Face.TextSemibold;

        public FlatButton(string text, ButtonKind kind, string icon = null)
        {
            Text = text;
            this.kind = kind;
            this.icon = icon;
            TabStop = true;
        }

        public ButtonKind Kind { get { return kind; } set { kind = value; Invalidate(); } }
        public string Icon { get { return icon; } set { icon = value; Invalidate(); } }

        /// <summary>Width that fits the label at the current scale (for buttons laid out by content).</summary>
        public int PreferredWidth(float paddingLogical = 18)
        {
            var font = Ui.Font(TextFace, FontPx);
            var w = Ui.Measure(Text, font).Width + Ui.S(paddingLogical * 2);
            if (icon != null && Ui.HasIcons) w += Ui.Measure(icon, Ui.Font(Face.Icon, FontPx)).Width + Ui.S(8);
            return w;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            var radius = Ui.S(Radius);
            Color fill, text;
            if (!Enabled)
            {
                fill = kind == ButtonKind.Ghost ? Color.Transparent : Theme.Disabled;
                text = Theme.Muted;
            }
            else if (kind == ButtonKind.Primary)
            {
                fill = pressed ? Theme.AccentPressed : Theme.Lerp(Theme.Accent, Theme.AccentHover, hoverT);
                text = Theme.AccentText;
            }
            else if (kind == ButtonKind.Secondary)
            {
                fill = pressed ? Theme.SurfaceRaised : Theme.Lerp(Theme.SurfaceRaised, Theme.SurfaceHover, hoverT);
                text = Theme.Text;
            }
            else
            {
                fill = Theme.Alpha(Color.White, (int)((pressed ? 10 : 16) * hoverT));
                text = Theme.Lerp(Theme.TextDim, Theme.Text, hoverT);
            }

            if (fill.A > 0)
            {
                if (kind == ButtonKind.Primary && Enabled)
                {
                    // A touch of depth: lighter at the top, a faint inner highlight.
                    using (var path = Theme.Rounded(rect, radius))
                    using (var brush = new LinearGradientBrush(new RectangleF(0, 0, Width, Height + 1),
                        Theme.Lerp(fill, Color.White, 0.08f), Theme.Lerp(fill, Color.Black, 0.12f), LinearGradientMode.Vertical))
                        g.FillPath(brush, path);
                    var inner = RectangleF.Inflate(rect, -1, -1);
                    Theme.StrokeRounded(g, Theme.Alpha(Color.White, 34), inner, radius - 1);
                }
                else
                {
                    Theme.FillRounded(g, fill, rect, radius);
                    if (kind == ButtonKind.Secondary)
                        Theme.StrokeRounded(g, Enabled ? Theme.Alpha(Color.White, 18 + (int)(14 * hoverT)) : Theme.Alpha(Color.White, 8), rect, radius);
                }
            }
            if (Focused && ShowFocusCues)
                Theme.StrokeRounded(g, Theme.Alpha(Color.White, 140), RectangleF.Inflate(rect, -2, -2), radius - 2);

            var font = Ui.Font(TextFace, FontPx);
            var textSize = Ui.Measure(Text, font);
            var showIcon = icon != null && Ui.HasIcons;
            Font iconFont = null;
            var iconSize = Size.Empty;
            var gap = Ui.S(FontPx * 0.6f);
            if (showIcon)
            {
                iconFont = Ui.Font(Face.Icon, FontPx * 1.05f);
                iconSize = Ui.Measure(icon, iconFont);
            }
            var total = textSize.Width + (showIcon ? iconSize.Width + gap : 0);
            var x = (Width - total) / 2;
            if (showIcon)
            {
                Ui.Draw(g, icon, iconFont, new Point(x, (Height - iconSize.Height) / 2 + 1), text);
                x += iconSize.Width + gap;
            }
            Ui.Draw(g, Text, font, new Point(x, (Height - textSize.Height) / 2), text);
        }
    }

    /// <summary>Text link with an optional trailing glyph. Sizes itself to its text.</summary>
    class TextLink : FxControl
    {
        public string TrailingGlyph;
        public float FontPx = 13f;
        public Color Color = Theme.AccentSoft;
        public Color HoverColor = Color.FromArgb(255, 150, 134);
        public event Action Activated;

        public TextLink(string text, string trailingGlyph = null)
        {
            Text = text;
            TrailingGlyph = trailingGlyph;
            TabStop = true;
            Click += (s, e) => { if (Activated != null) Activated(); };
        }

        public void FitSize()
        {
            var t = Ui.Measure(Text, Ui.Font(Face.TextSemibold, FontPx));
            var w = t.Width;
            if (TrailingGlyph != null && Ui.HasIcons) w += Ui.S(6) + Ui.Measure(TrailingGlyph, Ui.Font(Face.Icon, FontPx * 0.85f)).Width;
            Size = new Size(w + Ui.S(4), t.Height + Ui.S(6));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var color = Enabled ? Theme.Lerp(Color, HoverColor, hoverT) : Theme.Muted;
            var font = Ui.Font(Face.TextSemibold, FontPx);
            var size = Ui.Measure(Text, font);
            var y = (Height - size.Height) / 2;
            Ui.Draw(g, Text, font, new Point(Ui.S(2), y), color);
            if (TrailingGlyph != null && Ui.HasIcons)
            {
                var gf = Ui.Font(Face.Icon, FontPx * 0.85f);
                var gs = Ui.Measure(TrailingGlyph, gf);
                var shift = (int)(Ui.S(2) * hoverT);
                Ui.Draw(g, TrailingGlyph, gf, new Point(Ui.S(2) + size.Width + Ui.S(6) + shift, (Height - gs.Height) / 2 + Ui.S(1)), color);
            }
            if (hoverT > 0)
                using (var pen = new Pen(Theme.Alpha(color, (int)(160 * hoverT)), Math.Max(1, Ui.S(1))))
                    g.DrawLine(pen, Ui.S(2), y + size.Height + Ui.S(1), Ui.S(2) + size.Width, y + size.Height + Ui.S(1));
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Theme.Alpha(Color.White, 120)) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Minimize / close in the custom title bar.</summary>
    class CaptionButton : FxControl
    {
        readonly string glyph;
        readonly bool isClose;

        public CaptionButton(string glyph, bool isClose)
        {
            this.glyph = glyph;
            this.isClose = isClose;
            Cursor = Cursors.Default;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var hoverFill = isClose ? Color.FromArgb(196, 43, 28) : Color.FromArgb(28, 255, 255, 255);
            var fill = Theme.Alpha(hoverFill, (int)(hoverFill.A * hoverT * (pressed ? 0.8f : 1f)));
            if (fill.A > 0)
                using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, ClientRectangle);
            var color = Theme.Lerp(Theme.TextDim, Color.White, hoverT);
            if (Ui.HasIcons)
            {
                var font = Ui.Font(Face.Icon, 10f);
                var size = Ui.Measure(glyph, font);
                Ui.Draw(g, glyph, font, new Point((Width - size.Width) / 2, (Height - size.Height) / 2), color);
            }
            else
            {
                using (var pen = new Pen(color, Math.Max(1, Ui.S(1))))
                {
                    var c = new Point(Width / 2, Height / 2);
                    var r = Ui.S(5);
                    if (isClose) { g.DrawLine(pen, c.X - r, c.Y - r, c.X + r, c.Y + r); g.DrawLine(pen, c.X - r, c.Y + r, c.X + r, c.Y - r); }
                    else g.DrawLine(pen, c.X - r, c.Y, c.X + r, c.Y);
                }
            }
        }
    }

    /// <summary>Images compiled into the exe by build.ps1 (/resource).</summary>
    static class Resources
    {
        public static readonly Image FullLogo = LoadImage("GhostHunterLauncher.logo-full.png");
        public static readonly Image HexLogo = LoadImage("GhostHunterLauncher.logo.png");
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

        /// <summary>Draws an image tinted/darkened through a colour matrix.</summary>
        public static void DrawWithMatrix(Graphics g, Image image, Rectangle dest, Rectangle src, ColorMatrix matrix)
        {
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix);
                attributes.SetWrapMode(WrapMode.TileFlipXY); // no dark seams at the edges when scaling
                g.DrawImage(image, dest, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attributes);
            }
        }
    }
}
