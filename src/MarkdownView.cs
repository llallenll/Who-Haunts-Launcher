using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace GhostHunterLauncher
{
    enum MdKind { Heading, Paragraph, Bullet, Numbered, Quote, Code, Rule }

    class MdRun
    {
        public string Text;
        public bool Bold, Italic, Code;
        public string Url;
    }

    class MdBlock
    {
        public MdKind Kind;
        public int Level;      // heading level, or list nesting depth
        public string Marker;  // "3." for numbered items
        public string Raw = "";
        public List<MdRun> Runs;
    }

    /// <summary>Just enough GitHub-flavoured markdown for release notes: headings, lists, quotes, code, emphasis, links.</summary>
    static class Markdown
    {
        public static List<MdBlock> Parse(string markdown)
        {
            var blocks = new List<MdBlock>();
            var lines = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder para = null;
            MdBlock openItem = null;

            Action flush = () =>
            {
                if (para != null && para.Length > 0) blocks.Add(new MdBlock { Kind = MdKind.Paragraph, Raw = para.ToString() });
                para = null;
            };

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Replace("\t", "    ");
                var t = line.Trim();

                if (t.StartsWith("```") || t.StartsWith("~~~"))
                {
                    flush(); openItem = null;
                    var fence = t.Substring(0, 3);
                    var code = new StringBuilder();
                    for (i++; i < lines.Length && !lines[i].Trim().StartsWith(fence); i++)
                        code.Append(lines[i].Replace("\t", "    ").TrimEnd()).Append('\n');
                    blocks.Add(new MdBlock { Kind = MdKind.Code, Raw = code.ToString().TrimEnd('\n') });
                    continue;
                }
                if (t.Length == 0) { flush(); openItem = null; continue; }

                // Setext headings: a paragraph line underlined with === or ---.
                if (para != null && Regex.IsMatch(t, @"^(=+|-+)$"))
                {
                    blocks.Add(new MdBlock { Kind = MdKind.Heading, Level = t[0] == '=' ? 1 : 2, Raw = para.ToString() });
                    para = null;
                    continue;
                }

                var m = Regex.Match(t, @"^(#{1,6})\s+(.*?)\s*#*$");
                if (m.Success)
                {
                    flush(); openItem = null;
                    blocks.Add(new MdBlock { Kind = MdKind.Heading, Level = m.Groups[1].Length, Raw = m.Groups[2].Value });
                    continue;
                }
                if (Regex.IsMatch(t, @"^([-*_])(\s*\1){2,}$"))
                {
                    flush(); openItem = null;
                    blocks.Add(new MdBlock { Kind = MdKind.Rule });
                    continue;
                }
                m = Regex.Match(line, @"^(\s*)[-*+]\s+(.*)$");
                if (m.Success)
                {
                    flush();
                    openItem = new MdBlock { Kind = MdKind.Bullet, Level = Math.Min(3, m.Groups[1].Length / 2), Raw = m.Groups[2].Value };
                    blocks.Add(openItem);
                    continue;
                }
                m = Regex.Match(line, @"^(\s*)(\d{1,3})[.)]\s+(.*)$");
                if (m.Success)
                {
                    flush();
                    openItem = new MdBlock { Kind = MdKind.Numbered, Level = Math.Min(3, m.Groups[1].Length / 3), Marker = m.Groups[2].Value + ".", Raw = m.Groups[3].Value };
                    blocks.Add(openItem);
                    continue;
                }
                if (t.StartsWith(">"))
                {
                    flush(); openItem = null;
                    var text = t.TrimStart('>').Trim();
                    var last = blocks.Count > 0 ? blocks[blocks.Count - 1] : null;
                    if (last != null && last.Kind == MdKind.Quote && i > 0 && lines[i - 1].Trim().StartsWith(">")) last.Raw += " " + text;
                    else blocks.Add(new MdBlock { Kind = MdKind.Quote, Raw = text });
                    continue;
                }
                if (openItem != null && (line.StartsWith(" ") || para == null))
                {
                    openItem.Raw += " " + t; // continuation of a list item
                    continue;
                }
                if (para == null) para = new StringBuilder(t);
                else para.Append(' ').Append(t);
            }
            flush();

            foreach (var block in blocks)
                if (block.Kind != MdKind.Code && block.Kind != MdKind.Rule) block.Runs = ParseInline(block.Raw);
            return blocks;
        }

        static string Clean(string text)
        {
            text = Regex.Replace(text, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", "");
            return WebUtility.HtmlDecode(text);
        }

        public static List<MdRun> ParseInline(string text)
        {
            text = Clean(text ?? "");
            var runs = new List<MdRun>();
            var cur = new StringBuilder();
            bool bold = false, italic = false;
            Action flushRun = () =>
            {
                if (cur.Length > 0) runs.Add(new MdRun { Text = cur.ToString(), Bold = bold, Italic = italic });
                cur.Clear();
            };

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                var prev = i > 0 ? text[i - 1] : ' ';

                if (c == '\\' && next != '\0' && char.IsPunctuation(next) || c == '\\' && char.IsSymbol(next)) { cur.Append(next); i++; continue; }
                if (c == '`')
                {
                    var end = text.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        flushRun();
                        runs.Add(new MdRun { Text = text.Substring(i + 1, end - i - 1), Code = true });
                        i = end;
                        continue;
                    }
                }
                if ((c == '*' || c == '_') && next == c)
                {
                    var marker = new string(c, 2);
                    if (bold || text.IndexOf(marker, i + 2, StringComparison.Ordinal) > i + 2)
                    {
                        flushRun(); bold = !bold; i++;
                        continue;
                    }
                }
                if (c == '*' || (c == '_' && (italic ? !char.IsLetterOrDigit(next) : !char.IsLetterOrDigit(prev))))
                {
                    if (italic ? !char.IsWhiteSpace(prev) : (!char.IsWhiteSpace(next) && next != '\0' && text.IndexOf(c, i + 1) > i + 1))
                    {
                        flushRun(); italic = !italic;
                        continue;
                    }
                }
                if (c == '!' && next == '[')
                {
                    var img = Regex.Match(text.Substring(i), @"^!\[([^\]]*)\]\([^)]*\)");
                    if (img.Success) { i += img.Length - 1; continue; } // images aren't shown
                }
                if (c == '[')
                {
                    var link = Regex.Match(text.Substring(i), @"^\[([^\]]+)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)");
                    if (link.Success)
                    {
                        flushRun();
                        runs.Add(new MdRun { Text = link.Groups[1].Value.Replace("**", "").Replace("`", ""), Url = link.Groups[2].Value, Bold = bold });
                        i += link.Length - 1;
                        continue;
                    }
                }
                if ((c == 'h' || c == 'H') && (prev == ' ' || prev == '(' || i == 0))
                {
                    var bare = Regex.Match(text.Substring(i), @"^https?://[^\s)<>]+", RegexOptions.IgnoreCase);
                    if (bare.Success)
                    {
                        flushRun();
                        var url = bare.Value.TrimEnd('.', ',', ';', ':', '!', '?');
                        runs.Add(new MdRun { Text = url, Url = url });
                        i += url.Length - 1;
                        continue;
                    }
                }
                cur.Append(c);
            }
            flushRun();
            return runs;
        }

        public static string PlainText(List<MdRun> runs)
        {
            var sb = new StringBuilder();
            if (runs != null) foreach (var r in runs) sb.Append(r.Text);
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        /// <summary>The first few list items (or paragraphs, or headings) as plain text, for the home page summary.</summary>
        public static List<string> Highlights(string markdown, int max)
        {
            var blocks = Parse(markdown);
            var result = new List<string>();
            foreach (var kind in new[] { MdKind.Bullet, MdKind.Numbered, MdKind.Paragraph, MdKind.Heading })
            {
                foreach (var b in blocks)
                {
                    if (b.Kind != kind) continue;
                    if (kind == MdKind.Bullet && b.Level > 0) continue;
                    var text = PlainText(b.Runs);
                    if (text.Length > 0) result.Add(text);
                    if (result.Count >= max) return result;
                }
                if (result.Count > 0) return result;
            }
            return result;
        }
    }

    /// <summary>Owner-drawn, scrollable release-notes viewer for parsed markdown.</summary>
    class MarkdownView : Control
    {
        enum PieceKind { Text, InlineCode, Dot, Line, CodeBlock, QuoteBar }

        class Piece
        {
            public PieceKind Kind;
            public string Text;
            public Font Font;
            public Color Color;
            public Rectangle Bounds;
            public int LinkId = -1;
            public string Url;
        }

        List<MdBlock> blocks = new List<MdBlock>();
        string emptyMessage = "";
        readonly List<Piece> pieces = new List<Piece>();
        int contentHeight, laidOutWidth = -1;
        float scroll, scrollTarget;
        int hoverLink = -1;
        bool thumbHover, dragging;
        int dragStartY;
        float dragStartScroll;
        readonly Timer scrollTimer = new Timer { Interval = 15 };

        public MarkdownView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = true;
            scrollTimer.Tick += (s, e) =>
            {
                var delta = scrollTarget - scroll;
                if (Math.Abs(delta) < 0.5f) { scroll = scrollTarget; scrollTimer.Stop(); }
                else scroll += delta * 0.3f;
                Invalidate();
            };
        }

        public void SetMarkdown(string markdown, string emptyText)
        {
            blocks = string.IsNullOrWhiteSpace(markdown) ? new List<MdBlock>() : Markdown.Parse(markdown);
            emptyMessage = emptyText ?? "";
            scroll = scrollTarget = 0;
            Relayout();
        }

        public void ScrollToEnd() { EnsureLayout(); ScrollTo(MaxScroll, false); }

        public void Relayout()
        {
            laidOutWidth = -1;
            Invalidate();
        }

        Rectangle Inner { get { return new Rectangle(Ui.S(30), Ui.S(26), Math.Max(10, Width - Ui.S(30) - Ui.S(34)), Math.Max(10, Height - Ui.S(52))); } }
        int MaxScroll { get { return Math.Max(0, contentHeight - Inner.Height); } }

        void EnsureLayout()
        {
            var inner = Inner;
            if (laidOutWidth == inner.Width) return;
            laidOutWidth = inner.Width;
            pieces.Clear();
            var width = inner.Width;
            var y = 0;
            var linkId = 0;
            for (var bi = 0; bi < blocks.Count; bi++)
            {
                var b = blocks[bi];
                var first = bi == 0;
                var nextKind = bi + 1 < blocks.Count ? blocks[bi + 1].Kind : (MdKind?)null;
                switch (b.Kind)
                {
                    case MdKind.Heading:
                    {
                        var px = b.Level == 1 ? 23f : b.Level == 2 ? 19f : 16f;
                        var face = b.Level <= 2 ? Face.DisplaySemibold : Face.TextSemibold;
                        if (!first) y += Ui.S(b.Level <= 2 ? 24 : 16);
                        y = Flow(b.Runs, 0, width, y, face, px, Theme.Text, Ui.S(2), ref linkId);
                        if (b.Level <= 2)
                        {
                            y += Ui.S(9);
                            pieces.Add(new Piece { Kind = PieceKind.Line, Bounds = new Rectangle(0, y, width, Math.Max(1, Ui.S(1))), Color = Theme.Alpha(Color.White, 22) });
                            y += Ui.S(12);
                        }
                        else y += Ui.S(8);
                        break;
                    }
                    case MdKind.Paragraph:
                        y = Flow(b.Runs, 0, width, y, Face.Text, 14.5f, Theme.TextDim, Ui.S(6), ref linkId);
                        y += Ui.S(12);
                        break;
                    case MdKind.Bullet:
                    case MdKind.Numbered:
                    {
                        var indent = Ui.S(4) + b.Level * Ui.S(22);
                        var textX = indent + (b.Kind == MdKind.Numbered ? Ui.S(26) : Ui.S(20));
                        var bodyFont = Ui.Font(Face.Text, 14.5f);
                        var lineH = Ui.Measure("Ag", bodyFont).Height;
                        if (b.Kind == MdKind.Bullet)
                        {
                            var d = Math.Max(4, Ui.S(b.Level == 0 ? 6 : 5));
                            pieces.Add(new Piece
                            {
                                Kind = PieceKind.Dot, Bounds = new Rectangle(indent + Ui.S(3), y + (lineH - d) / 2 + Ui.S(1), d, d),
                                Color = b.Level == 0 ? Theme.Accent : Theme.Muted
                            });
                        }
                        else
                        {
                            var mf = Ui.Font(Face.TextSemibold, 14.5f);
                            pieces.Add(new Piece { Kind = PieceKind.Text, Text = b.Marker, Font = mf, Color = Theme.AccentSoft, Bounds = new Rectangle(new Point(indent, y), Ui.Measure(b.Marker, mf)) });
                        }
                        y = Flow(b.Runs, textX, width, y, Face.Text, 14.5f, Theme.TextDim, Ui.S(6), ref linkId);
                        y += nextKind == MdKind.Bullet || nextKind == MdKind.Numbered ? Ui.S(7) : Ui.S(14);
                        break;
                    }
                    case MdKind.Quote:
                    {
                        var top = y;
                        y = Flow(b.Runs, Ui.S(18), width, y, Face.Text, 14.5f, Theme.Muted, Ui.S(6), ref linkId, FontStyle.Italic);
                        pieces.Add(new Piece { Kind = PieceKind.QuoteBar, Bounds = new Rectangle(0, top - Ui.S(2), Ui.S(3), y - top + Ui.S(4)), Color = Theme.Alpha(Theme.Accent, 200) });
                        y += Ui.S(14);
                        break;
                    }
                    case MdKind.Code:
                    {
                        var font = Ui.Font(Face.Mono, 13f);
                        var lineH = Ui.Measure("Ag", font).Height + Ui.S(3);
                        var codeLines = b.Raw.Split('\n');
                        var pad = Ui.S(12);
                        var h = pad * 2 + codeLines.Length * lineH;
                        pieces.Add(new Piece { Kind = PieceKind.CodeBlock, Bounds = new Rectangle(0, y, width, h), Color = Color.FromArgb(31, 26, 29) });
                        for (var li = 0; li < codeLines.Length; li++)
                            pieces.Add(new Piece { Kind = PieceKind.Text, Text = codeLines[li], Font = font, Color = Color.FromArgb(230, 206, 196), Bounds = new Rectangle(pad, y + pad + li * lineH, width - pad * 2, lineH) });
                        y += h + Ui.S(14);
                        break;
                    }
                    case MdKind.Rule:
                        y += Ui.S(6);
                        pieces.Add(new Piece { Kind = PieceKind.Line, Bounds = new Rectangle(0, y, width, Math.Max(1, Ui.S(1))), Color = Theme.Alpha(Color.White, 22) });
                        y += Ui.S(18);
                        break;
                }
            }
            contentHeight = y;
            scrollTarget = Math.Min(scrollTarget, MaxScroll);
            scroll = Math.Min(scroll, MaxScroll);
        }

        /// <summary>Word-wraps runs between x0 and right starting at y; returns the y below the last line.</summary>
        int Flow(List<MdRun> runs, int x0, int right, int y, Face face, float px, Color color, int lineGap, ref int linkId, FontStyle extraStyle = FontStyle.Regular)
        {
            if (runs == null || runs.Count == 0) return y;
            var line = new List<Piece>();
            var x = x0;
            var baseFont = Ui.Font(face, px, extraStyle);
            var baseHeight = Ui.Measure("Ag", baseFont).Height;
            var pendingSpace = false;

            Action finishLine = () =>
            {
                var h = baseHeight;
                foreach (var p in line) h = Math.Max(h, p.Bounds.Height);
                foreach (var p in line) p.Bounds.Y = y + (h - p.Bounds.Height) / 2;
                pieces.AddRange(line);
                line.Clear();
                y += h + lineGap;
            };

            foreach (var run in runs)
            {
                Font font;
                Color c = color;
                if (run.Code) { font = Ui.Font(Face.Mono, px * 0.88f); c = Color.FromArgb(236, 200, 188); }
                else
                {
                    var f = run.Bold && face == Face.Text ? Face.TextSemibold : face;
                    font = Ui.Font(f, px, (run.Italic ? FontStyle.Italic : FontStyle.Regular) | extraStyle);
                    if (run.Bold) c = Theme.Text;
                }
                if (run.Url != null) c = Theme.AccentSoft;
                var id = run.Url != null ? linkId++ : -1;
                var spaceW = Ui.Measure("x x", font).Width - Ui.Measure("xx", font).Width;

                foreach (var token in Regex.Split(run.Text, @"(\s+)"))
                {
                    if (token.Length == 0) continue;
                    if (char.IsWhiteSpace(token[0])) { pendingSpace = x > x0; continue; }
                    var size = Ui.Measure(token, font);
                    if (run.Code) size.Width += Ui.S(8);
                    var gap = pendingSpace ? spaceW : 0;
                    if (x + gap + size.Width > right && x > x0)
                    {
                        finishLine();
                        x = x0;
                        gap = 0;
                    }
                    x += gap;
                    pendingSpace = false;
                    line.Add(new Piece
                    {
                        Kind = run.Code ? PieceKind.InlineCode : PieceKind.Text, Text = token, Font = font, Color = c,
                        Bounds = new Rectangle(x, 0, size.Width, size.Height), LinkId = id, Url = run.Url
                    });
                    x += size.Width;
                }
            }
            if (line.Count > 0) finishLine();
            return y - lineGap;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Backdrop.Paint(this, e.Graphics, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            var radius = Ui.S(12);
            Theme.FillRounded(g, Theme.Surface, card, radius);
            Theme.StrokeRounded(g, Theme.Border, card, radius);

            EnsureLayout();
            var inner = Inner;
            if (blocks.Count == 0)
            {
                var font = Ui.Font(Face.Text, 14.5f);
                TextRenderer.DrawText(g, emptyMessage, font, Rectangle.Inflate(inner, -Ui.S(20), 0), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }

            // Content clips at the card edge (with a little breathing room) and fades out under the top/bottom edges.
            var clip = new Rectangle(inner.X - Ui.S(8), Ui.S(2), inner.Width + Ui.S(16), Height - Ui.S(4));
            var oldClip = g.Clip;
            g.SetClip(clip);
            var dx = inner.X;
            var dy = inner.Y - (int)Math.Round(scroll);
            foreach (var p in pieces)
            {
                var r = p.Bounds;
                r.Offset(dx, dy);
                if (r.Bottom < clip.Top || r.Top > clip.Bottom) continue;
                switch (p.Kind)
                {
                    case PieceKind.Dot:
                        using (var brush = new SolidBrush(p.Color)) g.FillEllipse(brush, r);
                        break;
                    case PieceKind.Line:
                    case PieceKind.QuoteBar:
                        using (var brush = new SolidBrush(p.Color)) g.FillRectangle(brush, r);
                        break;
                    case PieceKind.CodeBlock:
                        Theme.FillRounded(g, p.Color, r, Ui.S(8));
                        break;
                    case PieceKind.InlineCode:
                        Theme.FillRounded(g, Color.FromArgb(40, 33, 36), new RectangleF(r.X, r.Y + Ui.S(1), r.Width, r.Height - Ui.S(1)), Ui.S(4));
                        TextRenderer.DrawText(g, p.Text, p.Font, new Point(r.X + Ui.S(4), r.Y), p.Color, Ui.Line | TextFormatFlags.PreserveGraphicsClipping);
                        break;
                    default:
                        var color = p.LinkId >= 0 && p.LinkId == hoverLink ? Color.FromArgb(255, 160, 144) : p.Color;
                        TextRenderer.DrawText(g, p.Text, p.Font, new Point(r.X, r.Y), color, Ui.Line | TextFormatFlags.PreserveGraphicsClipping);
                        if (p.LinkId >= 0 && p.LinkId == hoverLink)
                            using (var pen = new Pen(color)) g.DrawLine(pen, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
                        break;
                }
            }
            g.Clip = oldClip;

            var fade = Ui.S(22);
            if (scroll > 1)
                using (var brush = new LinearGradientBrush(new Rectangle(0, Ui.S(1), Width, fade + 1), Theme.Surface, Theme.Alpha(Theme.Surface, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(brush, Ui.S(2), Ui.S(1), Width - Ui.S(4), fade);
            if (scroll < MaxScroll - 1)
                using (var brush = new LinearGradientBrush(new Rectangle(0, Height - fade - Ui.S(2), Width, fade + 1), Theme.Alpha(Theme.Surface, 0), Theme.Surface, LinearGradientMode.Vertical))
                    g.FillRectangle(brush, Ui.S(2), Height - fade - Ui.S(1), Width - Ui.S(4), fade);

            if (MaxScroll > 0)
            {
                var thumb = Thumb();
                var wide = thumbHover || dragging;
                var w = wide ? Ui.S(8) : Ui.S(5);
                var tr = new RectangleF(thumb.Right - w - Ui.S(1), thumb.Y, w, thumb.Height);
                Theme.FillRounded(g, Theme.Alpha(Color.White, wide ? 110 : 60), tr, w / 2f);
            }
        }

        Rectangle Track { get { return new Rectangle(Width - Ui.S(16), Ui.S(10), Ui.S(12), Height - Ui.S(20)); } }

        Rectangle Thumb()
        {
            var track = Track;
            var view = Inner.Height;
            var h = Math.Max(Ui.S(36), (int)((long)track.Height * view / Math.Max(1, contentHeight)));
            h = Math.Min(h, track.Height);
            var y = track.Y + (int)((track.Height - h) * (MaxScroll == 0 ? 0 : scroll / MaxScroll));
            return new Rectangle(track.X, y, track.Width, h);
        }

        void ScrollTo(float target, bool smooth = true)
        {
            scrollTarget = Math.Max(0, Math.Min(MaxScroll, target));
            if (smooth) scrollTimer.Start();
            else { scroll = scrollTarget; scrollTimer.Stop(); Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var step = Ui.S(22) * Math.Max(1, SystemInformation.MouseWheelScrollLines);
            ScrollTo(scrollTarget - e.Delta / 120f * step);
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left && MaxScroll > 0 && Track.Contains(e.Location))
            {
                var thumb = Thumb();
                if (thumb.Contains(e.Location))
                {
                    dragging = true;
                    dragStartY = e.Y;
                    dragStartScroll = scroll;
                    Capture = true;
                }
                else ScrollTo(scrollTarget + (e.Y < thumb.Y ? -1 : 1) * Inner.Height * 0.9f);
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                var track = Track;
                var thumb = Thumb();
                var range = Math.Max(1, track.Height - thumb.Height);
                ScrollTo(dragStartScroll + (e.Y - dragStartY) * (float)MaxScroll / range, false);
            }
            var overThumb = MaxScroll > 0 && Track.Contains(e.Location);
            if (overThumb != thumbHover) { thumbHover = overThumb; Invalidate(); }

            var link = overThumb ? null : LinkAt(e.Location);
            var id = link != null ? link.LinkId : -1;
            if (id != hoverLink) { hoverLink = id; Invalidate(); }
            Cursor = link != null ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging) { dragging = false; Capture = false; Invalidate(); }
            else if (e.Button == MouseButtons.Left)
            {
                var link = LinkAt(e.Location);
                if (link != null) Ui.OpenUrl(link.Url);
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverLink = -1;
            thumbHover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        Piece LinkAt(Point p)
        {
            var inner = Inner;
            if (!inner.Contains(p)) return null;
            var local = new Point(p.X - inner.X, p.Y - inner.Y + (int)Math.Round(scroll));
            foreach (var piece in pieces)
                if (piece.LinkId >= 0 && piece.Bounds.Contains(local)) return piece;
            return null;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            var line = Ui.S(40);
            switch (e.KeyCode)
            {
                case Keys.Up: ScrollTo(scrollTarget - line); break;
                case Keys.Down: ScrollTo(scrollTarget + line); break;
                case Keys.PageUp: ScrollTo(scrollTarget - Inner.Height * 0.9f); break;
                case Keys.PageDown: case Keys.Space: ScrollTo(scrollTarget + Inner.Height * 0.9f); break;
                case Keys.Home: ScrollTo(0); break;
                case Keys.End: ScrollTo(MaxScroll); break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnResize(EventArgs e)
        {
            laidOutWidth = -1;
            base.OnResize(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) scrollTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
