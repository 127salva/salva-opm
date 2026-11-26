// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva
// Portions adapted from Ohman (https://github.com/P4R1H/Ohman), GPL-3.0-or-later.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SalvaOPM
{
    /// <summary>Colours and fonts of the window, "Modern Tech Minimalist": lavender accent on near-black, surfaces with a thin
    /// border, Plus Jakarta Sans (embedded) for the interface and Oswald SemiBold (embedded) for values. Red, amber and green keep their
    /// meaning (danger, warning, all good). Coordinates are written for 96 DPI and scaled with D(); fonts are in pixels.</summary>
    public static class Th
    {
        // one colour per job: Bg also fills a card held down; Border also fills the pills, a hovered card and the skeleton;
        // TextHi is all text that is not secondary (Sub); Section is the dim grey of a disabled card; NoReading, a little darker,
        // is a history slot without a reading (it must not look like a level)
        public static readonly Color Accent = C("#B8A3F5"), Danger = C("#F0565E"),
            Bg = C("#111116"), Surface = C("#1C1B23"), Border = C("#302E39"),
            TextHi = C("#F1EEF7"), Sub = C("#AAA5B5"), Section = C("#6F6A7A"), NoReading = C("#5F5B6B"),
            Ok = C("#3FB868"), Warn = C("#F4BE4F"),
            Selected = C("#262238");   // a chosen card's fill

        private static Color C(string h) { return ColorTranslator.FromHtml(h); }

        public static float K = 1f;
        public static int D(double v) { return (int)Math.Round(v * K); }

        /// <summary>Interface: Title, Small, Button (the setup's buttons), Card (Plus Jakarta Sans). Values: Mono, Stack (Oswald SemiBold; Stack is
        /// the chip cards' readings, two lines of it fit the readings' block).</summary>
        public static Font Title, Small, Mono, Stack, Button, Card;

        /// <summary>Level colours of a temperature (°C): grey no reading, green up to 85, amber 86-95, red 96 and up.</summary>
        public static Color TempLevelColor(double t) { return double.IsNaN(t) ? NoReading : t >= 95.5 ? Danger : t >= 85.5 ? Warn : Ok; }

        /// <summary>Level colours of a fan speed (rpm): grey no reading, green up to 3500, amber 3501-4200, red 4201 and up.</summary>
        public static Color RpmLevelColor(double rpm) { return double.IsNaN(rpm) ? NoReading : rpm > 4200 ? Danger : rpm > 3500 ? Warn : Ok; }

        /// <summary>Layout and fonts for a screen at this scale (1 = 96 DPI). Called again when the window opens on, or moves
        /// to, a screen with another scale. Fonts are in pixels: a point size would follow the main screen, not this one.</summary>
        public static void Init(float scale)
        {
            if (Title != null && scale == K) return;   // the fonts for this scale exist already (a tray click, a window opened again)
            K = scale;
            LoadFonts();
            Title = Face("Plus Jakarta Sans ExtraBold", FontStyle.Regular, 15f);
            Small = Face("Plus Jakarta Sans SemiBold", FontStyle.Regular, 9.75f);
            Button = Face("Plus Jakarta Sans ExtraBold", FontStyle.Regular, 11f);
            Card = Face("Plus Jakarta Sans", FontStyle.Bold, 12.5f);
            Mono = Face("Oswald SemiBold", FontStyle.Regular, 10.5f);   // its own weight: Bold would be a synthetic heavier one
            Stack = Face("Oswald SemiBold", FontStyle.Regular, 12f);
        }

        private static float Px(float points) { return points * 96f / 72f * K; }

        /// <summary>An embedded face (Plus Jakarta Sans for the interface, Oswald SemiBold for values: a condensed face with a plain zero) in pixels; Segoe UI Semibold if the embedded fonts could not be loaded.</summary>
        private static Font Face(string family, FontStyle style, float points)
        {
            if (_jakarta != null)
                foreach (FontFamily f in _jakarta.Families) if (f.Name == family) return new Font(f, Px(points), style, GraphicsUnit.Pixel);
            return new Font("Segoe UI Semibold", Px(points), GraphicsUnit.Pixel);
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint size, IntPtr reserved, out uint count);

        private static System.Drawing.Text.PrivateFontCollection _jakarta;
        private static bool _fontsTried;

        /// <summary>The embedded fonts (third_party/fonts, OFL), for this process only: GDI (TextRenderer)
        /// finds them through AddFontMemResourceEx, a Font object through the private collection. Nothing is installed in
        /// Windows. False when they are not in this build or could not be loaded (the window then uses Segoe UI).</summary>
        public static bool LoadFonts()
        {
            if (_fontsTried) return _jakarta != null;
            _fontsTried = true;
            try
            {
                System.Drawing.Text.PrivateFontCollection pfc = new System.Drawing.Text.PrivateFontCollection();
                foreach (string file in new string[] { "PlusJakartaSans-SemiBold", "PlusJakartaSans-Bold", "PlusJakartaSans-ExtraBold", "Oswald-SemiBold" })
                    using (System.IO.Stream s = typeof(Th).Assembly.GetManifestResourceStream("SalvaOPM." + file + ".ttf"))
                    {
                        if (s == null) return false;
                        byte[] b = new byte[s.Length];
                        s.Read(b, 0, b.Length);
                        IntPtr p = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(b.Length);   // kept for the app's life
                        System.Runtime.InteropServices.Marshal.Copy(b, 0, p, b.Length);
                        uint n;
                        if (AddFontMemResourceEx(p, (uint)b.Length, IntPtr.Zero, out n) == IntPtr.Zero) return false;
                        pfc.AddMemoryFont(p, b.Length);
                    }
                _jakarta = pfc;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>A card as the chip cards, the mode cards and the setup's buttons draw it: its fill, the thin border, and when ring has any
        /// alpha the 2 px ring of a chosen card, on the card's outer whole pixel rows (a pen is centred on its path, and pixel
        /// rows run from n - 0.5 to n + 0.5).</summary>
        public static void CardFace(Graphics g, RectangleF r, Color fill, Color ring)
        {
            float rad = D(RSurface);
            Fill(g, fill, r, rad);
            using (Pen edge = new Pen(Border, 1)) using (GraphicsPath p = Round(r, rad)) g.DrawPath(edge, p);
            if (ring.A == 0) return;
            float pw = D(2), inset = (pw - 1) / 2;
            using (Pen b = new Pen(ring, pw)) using (GraphicsPath p = Round(new RectangleF(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset), rad))
                g.DrawPath(b, p);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [System.Runtime.InteropServices.DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        /// <summary>The scale of the screen under a point (a window centred there opens on that screen).</summary>
        public static float ScaleAt(Point p)
        {
            try
            {
                POINT pt; pt.X = p.X; pt.Y = p.Y;
                uint x, y;
                if (GetDpiForMonitor(MonitorFromPoint(pt, 2), 0, out x, out y) == 0 && x > 0) return x / 96f;   // nearest screen, effective DPI
            }
            catch (Exception) { }
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>True once Windows has rounded a window for us (Windows 11): it then draws the border too.</summary>
        public static bool RoundedByWindows;

        /// <summary>Rounded window corners on Windows 11 (DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND) with Windows' own
        /// border in the given colour (DWMWA_BORDER_COLOR); on Windows 10 both calls fail and nothing changes.</summary>
        public static void RoundCorners(IntPtr handle, Color border)
        {
            int round = 2, colour = border.R | (border.G << 8) | (border.B << 16);
            try
            {
                RoundedByWindows = DwmSetWindowAttribute(handle, 33, ref round, 4) == 0;
                if (RoundedByWindows) DwmSetWindowAttribute(handle, 34, ref colour, 4);
            }
            catch (Exception) { RoundedByWindows = false; }
        }

        /// <summary>A dark title bar (DWMWA_USE_IMMERSIVE_DARK_MODE: 20, or 19 on Windows 10 before 20H1).</summary>
        public static void DarkTitleBar(IntPtr handle)
        {
            int on = 1;
            try { if (DwmSetWindowAttribute(handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(handle, 19, ref on, 4); } catch (Exception) { }
        }

        /// <summary>The one radius scale (96 DPI units): small pills and inner focus rings, every surface.</summary>
        public const double RSmall = 6, RSurface = 14;
        /// <summary>Motion: the cards' cross-fade (mouse only; skipped when Windows animations are off).</summary>
        public const int FadeMs = 150;
        public static bool Motion
        {
            get
            {
                // Windows 11 "Animation effects" (Accessibility > Visual effects) is SPI_GETCLIENTAREAANIMATION, the same
                // setting browsers use for reduced motion; the older UIEffectsEnabled stays on when it is turned off
                bool on;
                try { return !SystemParametersInfo(0x1042, 0, out on, 0) || on; }
                catch (Exception) { return true; }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(int action, int param, out bool value, int winIni);

        /// <summary>A strong ease-out (quartic): fast at first, settles softly; t is clamped to 0..1.</summary>
        public static double EaseOut(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            double u = 1 - t;
            return 1 - u * u * u * u;
        }

        public static Color Mix(Color a, Color b, double k)
        {
            k = Math.Max(0, Math.Min(1, k));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * k), (int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
        }

        /// <summary>The logo's lavender gradient (window, taskbar, exe icon); the tray diamond shows the mode in its own colours.</summary>
        public static readonly Color LogoLight = C("#D4C7FA"), LogoDark = C("#8B73E0");

        /// <summary>The logo: a lavender diamond, centred on (cx, cy).</summary>
        public static void Diamond(Graphics g, float cx, float cy, float r) { Diamond(g, cx, cy, r, LogoLight, LogoDark); }

        /// <summary>The diamond in other colours (the tray shows the mode with it).</summary>
        public static void Diamond(Graphics g, float cx, float cy, float r, Color light, Color dark)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(cx - r, cy - r), new PointF(cx + r, cy + r), light, dark))
                g.FillPolygon(b, new PointF[] { new PointF(cx, cy - r), new PointF(cx + r, cy), new PointF(cx, cy + r), new PointF(cx - r, cy) });
        }

        /// <summary>The header of the window and the setup (96-DPI units): the logo, its left tip on the edge, and the name after
        /// it, both centred on the line mid.</summary>
        public static void Header(Graphics g, int edge, int mid)
        {
            Diamond(g, D(edge + 16), D(mid), D(16));
            TextMid(g, "Salva OPM", Title, TextHi, HeaderNameX(edge), D(mid));
        }

        /// <summary>Where the header's name starts: after the 32 px logo and a 10 px gap.</summary>
        public static int HeaderNameX(int edge) { return D(edge + 42); }

        /// <summary>The window's icons (res/icons, white with transparency; drawn for the app, scale and bolt from Phosphor): "leaf" (Eco), "scale"
        /// (Balanced), "bolt" (Performance), "thermo" (temperatures), "fan" (fan speeds), "cpu" and "gpu" (the chip cards). Drawn tinted to the colour asked for,
        /// fitted into the square box without stretching, the drawing itself centred in it (so it sits on the centre line of the
        /// text or element beside it). Each size and colour is made once and kept.</summary>
        public static void Icon(Graphics g, string kind, RectangleF box, Color c)
        {
            int n = (int)Math.Round(box.Width);
            Bitmap icon = Cached(kind, c, n);
            if (icon != null) g.DrawImage(icon, (int)Math.Round(box.X), (int)Math.Round(box.Y), n, n);   // null: not in this build
        }

        private static readonly System.Collections.Generic.Dictionary<string, Bitmap> _icons = new System.Collections.Generic.Dictionary<string, Bitmap>();
        private static readonly System.Collections.Generic.Dictionary<string, SizeF> _inkSize = new System.Collections.Generic.Dictionary<string, SizeF>();

        /// <summary>The size of an icon's drawing in a size x size box (the drawing is smaller than its picture).</summary>
        public static SizeF IconInk(string kind, float size)
        {
            SizeF k;
            if (!_inkSize.TryGetValue(kind, out k))
            {
                k = new SizeF(1, 1);   // not in this build: the box itself
                using (System.IO.Stream s = typeof(Th).Assembly.GetManifestResourceStream("SalvaOPM.icon-" + kind + ".png"))
                    if (s != null) using (Bitmap b = new Bitmap(s)) { RectangleF r = InkBox(b); float m = Math.Max(b.Width, b.Height); k = new SizeF(r.Width / m, r.Height / m); }
                _inkSize[kind] = k;
            }
            return new SizeF(k.Width * size, k.Height * size);
        }

        private static readonly System.Collections.Generic.Dictionary<string, RectangleF> _drawnInk = new System.Collections.Generic.Dictionary<string, RectangleF>();

        /// <summary>An icon of size x size whose drawing starts exactly at (left, top): a corner shared with text or a bar.
        /// Placed by the solid pixels of the icon as drawn at that size, so rounding cannot move it.</summary>
        public static RectangleF IconTopLeft(Graphics g, string kind, float left, float top, float size, Color c)
        {
            int n = (int)Math.Round(size);
            RectangleF at = IconPlace(kind, left, top, size);
            Bitmap icon = Cached(kind, c, n);
            if (icon == null || at.IsEmpty) return at;
            RectangleF ink = _drawnInk[kind + "|" + n];
            g.DrawImage(icon, (int)Math.Round(at.X - ink.X), (int)Math.Round(at.Y - ink.Y), n, n);
            return at;
        }

        /// <summary>Where IconTopLeft puts the drawing (whole pixels), without drawing it: the one place both the paint and
        /// the layout read it from.</summary>
        public static RectangleF IconPlace(string kind, float left, float top, float size)
        {
            int n = (int)Math.Round(size);
            string key = kind + "|" + n;
            RectangleF ink;
            if (!_drawnInk.TryGetValue(key, out ink))
            {
                Bitmap b = Cached(kind, Color.White, n);
                if (b == null) return RectangleF.Empty;
                ink = InkBox(b);
                _drawnInk[key] = ink;
            }
            int x = (int)Math.Round(left - ink.X), y = (int)Math.Round(top - ink.Y);
            return new RectangleF(x + ink.X, y + ink.Y, ink.Width, ink.Height);
        }

        private static Bitmap Cached(string kind, Color c, int n)
        {
            if (n < 1) return null;
            string key = kind + "|" + c.ToArgb() + "|" + n + "x" + n;
            Bitmap icon;
            if (!_icons.TryGetValue(key, out icon))
            {
                icon = MakeIcon(kind, c, n);
                if (icon == null) return null;
                _icons[key] = icon;
            }
            return icon;
        }

        /// <summary>The middle of a picture's drawing as the eye sees it: halfway between the centre of the box around its solid
        /// pixels and the centre of its weight.</summary>
        public static PointF InkCentre(Bitmap b)
        {
            // the eye reads a shape's middle between its outline's middle and its weight's middle (a thermometer's heavy
            // bulb pulls it down, a card's tab pulls it down): halfway between the two
            RectangleF r = InkBox(b);
            double sx = 0, sy = 0, sw = 0;
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++) { int a = b.GetPixel(x, y).A; sx += a * (x + 0.5); sy += a * (y + 0.5); sw += a; }
            if (sw <= 0) return new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
            return new PointF((float)((r.X + r.Width / 2 + sx / sw) / 2), (float)((r.Y + r.Height / 2 + sy / sw) / 2));
        }

        /// <summary>The box around a picture's solid pixels (alpha at least half); the whole picture when it has none.</summary>
        public static RectangleF InkBox(Bitmap b)
        {
            Rectangle r = Box(b, delegate(Color p) { return p.A >= 128; });
            return r.IsEmpty ? new RectangleF(0, 0, b.Width, b.Height) : r;
        }

        /// <summary>One icon at n x n in colour c: the picture scaled to fit (high-quality), its whiteness kept as coverage.</summary>
        private static Bitmap MakeIcon(string kind, Color c, int n)
        {
            using (System.IO.Stream s = typeof(Th).Assembly.GetManifestResourceStream("SalvaOPM.icon-" + kind + ".png"))
            {
                if (s == null) return null;
                using (Bitmap src = new Bitmap(s))
                {
                    Bitmap o = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    float k = Math.Min((float)n / src.Width, (float)n / src.Height);
                    float dw = src.Width * k, dh = src.Height * k;
                    // the drawing's own middle (not the picture's) on the box's middle: an icon lines up with its text
                    PointF ink = InkCentre(src);
                    float x = n / 2f - ink.X * k, y = n / 2f - ink.Y * k;
                    using (System.Drawing.Imaging.ImageAttributes edges = new System.Drawing.Imaging.ImageAttributes())
                    using (Graphics g = Graphics.FromImage(o))
                    {
                        edges.SetWrapMode(WrapMode.TileFlipXY);                 // no dark seam at the scaled edge
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(src, new Rectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(dw), (int)Math.Round(dh)),
                            0, 0, src.Width, src.Height, GraphicsUnit.Pixel, edges);
                    }
                    // the drawing gives only the coverage (alpha); every pixel takes c (a tint while scaling overshot at the edges,
                    // where colour read as pure white)
                    for (int py = 0; py < n; py++)
                        for (int px = 0; px < n; px++) { int a = o.GetPixel(px, py).A; if (a > 0) o.SetPixel(px, py, Color.FromArgb(a * c.A / 255, c.R, c.G, c.B)); }
                    return o;
                }
            }
        }

        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        /// <summary>Plus Jakarta Sans has a narrow space, which GDI rounds down to 2 px at small sizes ("All good" read as one
        /// word): each space gets a hair space after it (U+200A), about Segoe UI's width, and still a place to wrap.</summary>
        public static string Sp(string s, Font f)
        {
            return s != null && f.FontFamily.Name.StartsWith("Plus Jakarta", StringComparison.Ordinal) ? s.Replace(" ", "\u200A ") : s;
        }

        private static readonly Bitmap _measureBmp = new Bitmap(1, 1);
        private static Graphics _measure;

        /// <summary>The drawn size of a line of text. Measured on a 96-DPI surface: without one, TextRenderer takes a font sized
        /// in pixels for points and reports every width about 4/3 too wide (91 px for a 68 px "62°C"), which pushed
        /// right-aligned and centred text off its line.</summary>
        public static Size Measure(string s, Font f)
        {
            lock (_measureBmp)
            {
                if (_measure == null) _measure = Graphics.FromImage(_measureBmp);
                return TextRenderer.MeasureText(_measure, Sp(s, f), f, Size.Empty, Flags);
            }
        }
        public static void Text(Graphics g, string s, Font f, Color c, int x, int y) { TextRenderer.DrawText(g, Sp(s, f), f, new Point(x, y), c, Flags); }

        // ---- where drawn things are, measured from a drawing (white on black, or an icon's alpha), as the eye sees them

        /// <summary>Solid ink: the shape the eye reads.</summary>
        public static bool Solid(Color p) { return p.G > 96; }

        /// <summary>Everything that shows at all, ClearType's coloured edge included (it can reach a column past the solid ink).</summary>
        public static bool Faint(Color p) { return Math.Max(p.R, Math.Max(p.G, p.B)) > 48; }

        /// <summary>The box around a picture's pixels that are inked; empty when there are none.</summary>
        public static Rectangle Box(Bitmap b, Func<Color, bool> inked)
        {
            int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                    if (inked(b.GetPixel(x, y))) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
            return x1 < 0 ? Rectangle.Empty : new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        private static readonly System.Collections.Generic.Dictionary<string, Rectangle> _textInk = new System.Collections.Generic.Dictionary<string, Rectangle>();

        /// <summary>Where a line of text's ink lies relative to the point it is drawn at (Text), solid or faint; measured once per
        /// string and font. Empty when nothing is drawn.</summary>
        public static Rectangle TextInk(string s, Font f, bool faint)
        {
            string key = s + "|" + f.Name + "|" + f.Size + "|" + faint;
            Rectangle v;
            if (_textInk.TryGetValue(key, out v)) return v;
            Size sz = Measure(s, f);
            using (Bitmap b = new Bitmap(sz.Width + 8, sz.Height + 8))
            {
                using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.Black); Text(g, s, f, Color.White, 4, 4); }
                v = Box(b, faint ? (Func<Color, bool>)Faint : Solid);
                if (!v.IsEmpty) v.Offset(-4, -4);
            }
            _textInk[key] = v;
            return v;
        }

        /// <summary>How far below the top of a drawn line of text the middle of its capitals is, in pixels: from a drawn "H"
        /// (font metrics put the box centre elsewhere, and differently for each face).</summary>
        public static int InkMid(Font f) { Rectangle h = TextInk("H", f, false); return h.IsEmpty ? Measure("H", f).Height / 2 : (h.Top + h.Bottom) / 2; }

        /// <summary>The height of the font's capitals in pixels (from the same drawn "H").</summary>
        public static int CapHeight(Font f) { Rectangle h = TextInk("H", f, false); return h.IsEmpty ? Measure("H", f).Height / 2 : h.Height; }

        /// <summary>How far below the top of a drawn line of text its baseline is (the bottom of the drawn "H").</summary>
        public static int InkBase(Font f) { Rectangle h = TextInk("H", f, false); return h.IsEmpty ? Measure("H", f).Height : h.Bottom; }

        /// <summary>Text whose capitals start exactly at y (a top edge shared with an icon or a bar).</summary>
        public static void TextTop(Graphics g, string s, Font f, Color c, int x, int top) { Text(g, s, f, c, x, top - (InkBase(f) - CapHeight(f))); }

        /// <summary>Text whose ink ends on the column before right: placed by its ink, not its advance (an "8" reaches a pixel
        /// further than a "0"). Solid ink lines texts up with each other; faint (ClearType's coloured edge included) keeps
        /// everything inside an edge shared with a line.</summary>
        public static void TextTopRight(Graphics g, string s, Font f, Color c, int right, int top, bool faint)
        {
            Rectangle ink = TextInk(s, f, faint);
            TextTop(g, s, f, c, right - (ink.IsEmpty ? Measure(s, f).Width : ink.Right), top);
        }

        /// <summary>Text whose capitals are centred on the line mid (so a row of mixed fonts and icons shares one centre).</summary>
        public static void TextMid(Graphics g, string s, Font f, Color c, int x, int mid) { Text(g, s, f, c, x, mid - InkMid(f)); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillWith(Graphics g, Brush b, RectangleF r, float rad)
        {
            using (GraphicsPath p = Round(r, rad)) g.FillPath(b, p);
        }

        public static void Fill(Graphics g, Color c, RectangleF r, float rad)
        {
            using (GraphicsPath p = Round(r, rad)) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }
    }

    /// <summary>The tray menu: the window's dark background, a rounded highlight under the mouse, a thin separator.</summary>
    public sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Th.Bg); }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (Th.RoundedByWindows) return;   // Windows 11 draws the rounded border itself (a square one would be cut at the corners)
            using (Pen p = new Pen(Th.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        /// <summary>The highlight's even gap to the menu's edges, and Windows 11's corner radius for menus (DWMWCP_ROUND).</summary>
        public const int Inset = 4, OuterRadius = 8;

        /// <summary>The menu in the current scale: a drop-down ignores item padding (and its own Padding), so the items are
        /// sized here and the renderer places the text 12 px in. The menu takes the items' width: left to itself it measures
        /// its text again and ends up wider than them, and an item's highlight is clipped to the item.</summary>
        public static void Fit(ContextMenuStrip m)
        {
            m.Font = Th.Small;
            int textW = 0;
            foreach (ToolStripItem item in m.Items) if (item is ToolStripMenuItem) textW = Math.Max(textW, Th.Measure(item.Text, Th.Small).Width);
            int w = textW + Th.D(40);
            foreach (ToolStripItem item in m.Items) if (item is ToolStripMenuItem) { item.AutoSize = false; item.Size = new Size(w, Th.D(28)); }
            m.AutoSize = false;
            m.Size = new Size(w, m.GetPreferredSize(Size.Empty).Height);
        }

        /// <summary>An item's highlight, in the item's coordinates: Inset from the menu's sides (the items start at its left edge
        /// and stop short of its right one), and from its top and bottom on the first and last item.</summary>
        public static RectangleF Highlight(Rectangle item, Size menu)
        {
            float p = Th.D(Inset), top = Math.Max(item.Top, p), bottom = Math.Min(item.Bottom, menu.Height - p);
            return new RectangleF(p - item.X, top - item.Y, menu.Width - 2 * p, bottom - top);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;   // its whole-number edges fall between pixels: crisp
            // concentric with the menu's rounded corners: the outer radius minus the gap
            Th.Fill(e.Graphics, Th.Border, Highlight(e.Item.Bounds, e.ToolStrip.Size), Th.D(OuterRadius - Inset));
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Default;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Th.TextHi;
            e.TextRectangle = new Rectangle(Th.D(12), 0, e.Item.Width - Th.D(16), e.Item.Height);
            e.TextFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            e.Text = Th.Sp(e.Text, e.TextFont);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Th.Border)) e.Graphics.DrawLine(p, Th.D(8), y, e.Item.Width - Th.D(8), y);
        }
    }

    /// <summary>Base for the owner-drawn controls: double-buffered, painted on the window background.</summary>
    public class Ctl : Control
    {
        public Ctl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Th.Bg;
            ForeColor = Th.TextHi;
            TabStop = false;   // only the mode cards (Seg) take input, so only they are a Tab stop
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || k == Keys.Enter || base.IsInputKey(k);
        }

        /// <summary>The keyboard focus ring: drawn only after keyboard use (ShowFocusCues), never for mouse clicks.</summary>
        protected bool KeyboardFocus { get { return Focused && ShowFocusCues; } }

        internal static void FocusRing(Graphics g, RectangleF r, float radius)
        {
            using (Pen p = new Pen(Th.TextHi, Th.D(1.5)))
            using (GraphicsPath path = Th.Round(new RectangleF(r.X + Th.D(4), r.Y + Th.D(4), r.Width - Th.D(8), r.Height - Th.D(8)), radius))
                g.DrawPath(p, path);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>A mouse click hides the keyboard focus ring for the whole window (WM_CHANGEUISTATE, UIS_SET,
        /// UISF_HIDEFOCUS), as browsers do; WinForms shows it again on the next Tab or arrow key.</summary>
        public static void HideFocusCues(Control c)
        {
            Form f = c.FindForm();
            if (f != null && f.IsHandleCreated) SendMessage(f.Handle, 0x127, (IntPtr)0x10001, IntPtr.Zero);
        }

        protected override void OnMouseDown(MouseEventArgs e) { HideFocusCues(this); base.OnMouseDown(e); }
        protected override void OnChangeUICues(UICuesEventArgs e) { Invalidate(); base.OnChangeUICues(e); }

        /// <summary>WinForms shows the ring again on Tab only: an arrow key after a click must show it too, or the arrows
        /// would move a focus nobody can see (WM_CHANGEUISTATE, UIS_CLEAR, UISF_HIDEFOCUS).</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            Form f = FindForm();
            if (!ShowFocusCues && f != null && f.IsHandleCreated && IsInputKey(e.KeyCode)) SendMessage(f.Handle, 0x127, (IntPtr)0x10002, IntPtr.Zero);
            base.OnKeyDown(e);
        }
    }

    /// <summary>The mode cards: one card per item, its icon above its name; the chosen card is tinted with a lavender
    /// edge. Arrow keys move a focus ring, Enter or Space chooses.</summary>
    public sealed class Seg : Ctl
    {
        public string[] Items = new string[0];
        public string[] Icons = new string[0];
        public bool[] Disabled;
        public int Index;
        public event Action<int> Picked;
        private int _hover = -1, _focus = -1;   // _focus: the item the arrow keys are on

        public Seg()
        {
            TabStop = true;
            _anim.Interval = 15;
            // the tick stops itself too: a minimized window is never painted, so a stop in OnPaint alone would leave it running
            _anim.Tick += delegate { Invalidate(); if (_from < 0 || Environment.TickCount - _since >= Th.FadeMs) _anim.Stop(); };
        }

        protected override void OnGotFocus(EventArgs e) { _focus = Index; base.OnGotFocus(e); }

        // a click hid the ring: forget the item the arrows were on, so Enter cannot apply a choice nobody can see
        protected override void OnChangeUICues(UICuesEventArgs e) { if (e.ChangeFocus && !e.ShowFocus) _focus = -1; base.OnChangeUICues(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int d = e.KeyCode == Keys.Left || e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Right || e.KeyCode == Keys.Down ? 1 : 0;
            if (_focus < 0 || _focus >= Items.Length) _focus = Index;
            if (d != 0)
            {
                for (int i = _focus + d; i >= 0 && i < Items.Length; i += d) if (!IsDisabled(i)) { _focus = i; Invalidate(); break; }
                e.Handled = true;
            }
            else if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && _focus != Index && !IsDisabled(_focus))
            {
                _keyChose = true; _keyAt = Environment.TickCount;   // never animate keyboard actions
                Action<int> h = Picked;
                if (h != null) h(_focus);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        /// <summary>A mode card's icon above its name: as wide as the wider of the two.</summary>
        public static float GroupWidth(string name) { return Math.Max(Th.D(30), Th.Measure(name, Th.Card).Width); }

        /// <summary>The gap between cards (96-DPI units): the window's 16 px gap between blocks.</summary>
        public const int Gap = 16;

        /// <summary>Each of n cards' width in a row this wide (pixels).</summary>
        public static float CardWidth(float width, int n) { return (width - 1 - Th.D(Gap) * (n - 1)) / n; }

        /// <summary>The tallest of the cards' icon drawings in a size x size box (one value for all cards keeps the names on one line).</summary>
        private float TallestIcon(float size)
        {
            float h = 0;
            foreach (string ic in Icons) h = Math.Max(h, Th.IconInk(ic, size).Height);
            return h > 0 ? h : size;
        }

        private RectangleF Cell(int i)
        {
            float gap = Th.D(Gap), cw = CardWidth(Width, Math.Max(1, Items.Length));
            float x = (float)Math.Round(i * (cw + gap)), right = (float)Math.Round(i * (cw + gap) + cw);   // whole pixels: crisp sides
            return new RectangleF(x, 0, right - x, Height - 1);
        }

        private bool IsDisabled(int i) { return Disabled != null && i < Disabled.Length && Disabled[i]; }

        /// <summary>One colour for a card's icon and its name: dim when disabled, bright when chosen or hovered.</summary>
        public static Color ItemColor(bool chosen, bool disabled, bool hover)
        {
            return disabled ? Th.Section : chosen || hover ? Th.TextHi : Th.Sub;
        }

        // ---- motion: a mouse choice cross-fades the selected look from the old card to the new one
        private int _shown = -1, _shownCount;
        internal int _from = -1;   // the item the fade started from (-1: none); internal: the self-checks set it
        internal int _since;
        private int _keyAt;        // when the keyboard last chose (Environment.TickCount); its change is shown at once
        private bool _keyChose;
        internal readonly Timer _anim = new Timer();

        private double Progress() { return Th.EaseOut((Environment.TickCount - _since) / (double)Th.FadeMs); }

        private void Track()
        {
            if (Index == _shown && Items.Length == _shownCount) return;   // nothing changed (also the repaints WMI calls cause mid-apply)
            bool byKey = _keyChose && Environment.TickCount - _keyAt < 5000;
            if (!byKey && _shown >= 0 && Items.Length == _shownCount && Th.Motion)
            {
                _from = _shown;
                _since = Environment.TickCount;
                _anim.Start();
            }
            else { _from = -1; _anim.Stop(); }
            _shown = Index;
            _shownCount = Items.Length;
            _keyChose = false;
        }

        // ---- press: the pressed look on mouse-down; the choice applies on release over the same item
        private int _press = -1;
        private bool _pressInside;

        private bool IsPressed(int i) { return i == _press && _pressInside; }

        private static RectangleF Inset(RectangleF r, float d) { return new RectangleF(r.X + d, r.Y + d, r.Width - 2 * d, r.Height - 2 * d); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
            Track();
            double k = _from >= 0 ? Progress() : 1;
            for (int i = 0; i < Items.Length; i++)
            {
                bool on = i == Index, off = IsDisabled(i);
                double a = on ? k : i == _from ? 1 - k : 0;   // how selected it looks: fades in on the new card, out on the old
                RectangleF r = IsPressed(i) ? Inset(Cell(i), Th.D(2)) : Cell(i);
                Color baseFill = IsPressed(i) ? Th.Bg : i == _hover && !off && !on ? Th.Border : Th.Surface;
                Th.CardFace(g, r, Th.Mix(baseFill, Th.Selected, a), Color.FromArgb((int)(255 * a), off ? Th.Border : Th.Accent));
                Color c = ItemColor(on, off, i == _hover);
                // the icon above the name, centred in the card as one group as the eye sees it: from the tallest drawing (shorter
                // than its box, and centred in it) to the name's baseline
                float s = Th.D(30), gap = Th.D(8), cap = Th.CapHeight(Th.Card), cx = r.X + r.Width / 2;
                float top = r.Y + (r.Height - s - gap - cap) / 2 - (s - TallestIcon(s)) / 4;
                if (i < Icons.Length) Th.Icon(g, Icons[i], new RectangleF(cx - s / 2, top, s, s), c);
                Th.TextTop(g, Items[i], Th.Card, c, (int)Math.Round(cx - Th.Measure(Items[i], Th.Card).Width / 2f), (int)Math.Round(top + s + gap));
            }
            if (KeyboardFocus && _focus >= 0 && _focus < Items.Length) FocusRing(g, Cell(_focus), Th.D(Th.RSurface - 4));
            if (_from >= 0 && Environment.TickCount - _since >= Th.FadeMs) { _from = -1; _anim.Stop(); }
        }

        private int At(Point p)
        {
            for (int i = 0; i < Items.Length; i++) if (Cell(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = e.Button == MouseButtons.Left ? At(e.Location) : -1;
            if (i >= 0 && i != Index && !IsDisabled(i)) { _press = i; _pressInside = true; Capture = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = At(e.Location);
            if (_press >= 0 && (h == _press) != _pressInside) { _pressInside = h == _press; Invalidate(); }
            Cursor = h >= 0 && h != Index && !IsDisabled(h) ? Cursors.Hand : Cursors.Default;   // a hand only over something clickable
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = _press;
            bool commit = i >= 0 && At(e.Location) == i;
            _press = -1;
            Capture = false;
            Invalidate();
            if (commit)
            {
                Action<int> h = Picked;
                if (h != null) h(i);
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e) { if (_press >= 0) { _press = -1; Invalidate(); } base.OnMouseCaptureChanged(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _anim.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>The setup window's buttons, drawn like the mode cards: the main one (Install, Update, Close) as the chosen
    /// card (its fill and lavender ring), the other (Uninstall) as a plain card; the app's hover, pressed and dimmed colours.
    /// Still a Windows button, so Tab, Enter and Space work as usual.</summary>
    public sealed class CardButton : Button
    {
        public bool Primary;
        private bool _hover, _down;

        public CardButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Th.Bg;
            Font = Th.Button;
            Cursor = Cursors.Hand;
        }

        /// <summary>The fill for a state; the text takes the mode cards' colour (Seg.ItemColor), the ring shows while enabled.</summary>
        public static Color FillFor(bool primary, bool enabled, bool hover, bool down)
        {
            if (!enabled) return Th.Surface;
            if (down) return Th.Bg;
            return primary ? (hover ? Th.Mix(Th.Selected, Th.Accent, 0.12) : Th.Selected) : hover ? Th.Border : Th.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Th.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            Th.CardFace(g, r, FillFor(Primary, Enabled, _hover, _down), Primary && Enabled ? Th.Accent : Color.Empty);
            Color text = Seg.ItemColor(Primary, !Enabled, _hover || _down);
            Th.TextMid(g, Text, Th.Button, text, (int)Math.Round((Width - Th.Measure(Text, Th.Button).Width) / 2f), Height / 2);
            if (Focused && ShowFocusCues) Ctl.FocusRing(g, r, Th.D(Th.RSurface - 4));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { _hover = _down = false; Invalidate(); base.OnEnabledChanged(e); }
    }

    /// <summary>One chip's card (CPU or GPU) on a 16 px padding. The icon and name sit in the top left corner; in the top
    /// right the temperature over the fan speed, each line (the number, a small gap, its unit) ending on the right padding
    /// where the history lines end, the units as tall as the numbers, all starting on the top padding line. Under them are two history lines, temperature and fan
    /// speed: one slot per 30 s (its highest reading), the newest on the right, neighbours of one colour joined, in the level
    /// colours (grey no reading, green, amber, red). Before the first reading (start-up) a skeleton stands in for the numbers
    /// and the lines.</summary>
    public sealed class ChipCard : Ctl
    {
        public string Title = "";
        public string IconName = "cpu";
        public double Temp = double.NaN, Rpm = double.NaN;                 // now; NaN = no reading
        public bool Asleep;                                                // the GPU is asleep: "Asleep" instead of "--"
        public double[] TempDots = new double[0], RpmDots = new double[0]; // one per 30 s, oldest first; NaN = no reading

        /// <summary>Nothing read yet since start-up (the history has no slot at all, not even an empty one): a skeleton.</summary>
        public bool Loading { get { return TempDots.Length == 0; } }

        // the card's grid (96-DPI units): the name and the readings start on the top padding line; the readings' two lines are
        // ReadH tall together; the two history rows start at RowsTop, the lower one ending on the bottom padding
        private const int Pad = 16, ReadH = 32, RowsTop = 54, LineH = 6, RowGap = 28, SmallIcon = 22, ChipSize = 24;

        /// <summary>The readings' height in the layout: two lines of the value font's capitals and the gap between them.</summary>
        public static int ReadingsHeight { get { return Th.D(ReadH); } }

        /// <summary>The card's height (96-DPI units).</summary>
        public const int CardHeight = RowsTop + SmallIcon + RowGap + Pad;

        internal int Row1Mid { get { return Th.D(RowsTop + SmallIcon / 2); } }
        private int Row2Mid { get { return Row1Mid + Th.D(RowGap); } }

        /// <summary>Where the history lines run: from after the small icon to the right padding.</summary>
        public float LineLeft { get { return Th.D(Pad + SmallIcon + 10); } }

        /// <summary>The chip icon's box: sized so its square drawing is ChipSize px, the CPU's and the GPU's alike.</summary>
        private float ChipBox
        {
            get
            {
                // the drawing scaled into a box can come out a pixel off: take the box whose drawing is ChipSize square, or
                // else the first one that is ChipSize tall
                int want = Th.D(ChipSize), n = (int)Math.Round(want / Math.Max(0.01f, Th.IconInk(IconName, 1).Height)), tall = -1;
                for (int k = n - 2; k < n + 4; k++)
                {
                    RectangleF at = Th.IconPlace(IconName, 0, 0, k);
                    if (at.Height == want && at.Width == want) return k;
                    if (tall < 0 && at.Height >= want) tall = k;
                }
                return tall > 0 ? tall : n;
            }
        }

        /// <summary>Where the chip icon's drawing lands: its left and top edges on the padding, like a title. The name starts 10 px
        /// after it (NameLeft), centred on it (NameMid): one line across both cards.</summary>
        public RectangleF Chip { get { return Th.IconPlace(IconName, Th.D(Pad), Th.D(Pad), ChipBox); } }
        public int NameLeft { get { return (int)Math.Round(Chip.Right) + Th.D(10) - Th.TextInk(Title, Th.Card, false).Left; } }   // by its ink: 10 px of gap
        public int NameMid { get { RectangleF c = Chip; return (int)Math.Round(c.Y + c.Height / 2); } }

        /// <summary>The history icons' own column, not the title's: centred in a SmallIcon-wide column on the left padding.</summary>
        public static float HistoryIconMid { get { return Th.D(Pad + SmallIcon / 2f); } }

        /// <summary>The right padding: where the history lines end, and the readings (each line's number, gap and unit) too.</summary>
        public float LineRight { get { return Width - Th.D(Pad); } }
        public static float LineHeight { get { return Th.D(LineH); } }

        /// <summary>How many 30 s slots a history line shows: 10 minutes, whatever the card's width.</summary>
        public const int SlotCount = 20;

        /// <summary>Where line 0's or line 1's number ends: 4 px before its unit.</summary>
        public int NumberRight(int line) { return (int)UnitBox(line).X - Th.D(4); }

        /// <summary>The capitals' top of reading line 0 (temperature) or 1 (fan speed): line 0 on the top padding, line 1 ending
        /// the readings' block.</summary>
        public int LineTop(int line) { return line == 0 ? Th.D(Pad) : Th.D(Pad) + Th.D(ReadH) - Th.CapHeight(Th.Stack); }

        /// <summary>A line's unit, in the numbers' face and size (so as tall as them).</summary>
        private static string Unit(int line) { return line == 0 ? "°C" : "RPM"; }

        /// <summary>Where a line's unit is drawn (its solid ink): everything of it that shows, ClearType's coloured edge included,
        /// ends on the right padding, where the history lines end; it starts on its number's first ink row.</summary>
        public RectangleF UnitBox(int line)
        {
            Rectangle ink = Th.TextInk(Unit(line), Th.Stack, false);
            int shows = Th.TextInk(Unit(line), Th.Stack, true).Right;
            int inkTop = LineTop(line) + Th.CapHeight(Th.Stack) - Th.InkBase(Th.Stack) + Th.TextInk("0123456789", Th.Stack, false).Top;
            return new RectangleF(LineRight - shows + ink.X, inkTop, ink.Width, ink.Height);
        }

        /// <summary>Where the readings start (room for "8888" before "RPM", or "Asleep" to the right padding) and where the name
        /// ends: they must not meet.</summary>
        public float ReadingsLeft { get { return Math.Min(NumberRight(1) - Th.Measure("8888", Th.Stack).Width, LineRight - Th.Measure("Asleep", Th.Stack).Width); } }
        public float NameRight { get { return NameLeft + Th.Measure(Title, Th.Card).Width; } }

        /// <summary>The [start, end) runs of equal colour: drawn as one piece each, so a line has no seams.</summary>
        public static System.Collections.Generic.List<int[]> Stretches(Color[] c)
        {
            System.Collections.Generic.List<int[]> runs = new System.Collections.Generic.List<int[]>();
            for (int i = 0; i < c.Length; )
            {
                int j = i + 1;
                while (j < c.Length && c[j] == c[i]) j++;
                runs.Add(new int[] { i, j });
                i = j;
            }
            return runs;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
            Th.CardFace(g, new RectangleF(0, 0, Width - 1, Height - 1), Th.Surface, Color.Empty);
            // from here on a whole-number edge falls between pixels, not through them: the shapes below are crisp (the border
            // above is a 1 px pen on whole numbers, which the default mode keeps crisp)
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // the chip's icon in the top left padding corner, its name centred on it; the readings start on the same top line
            int top = Th.D(Pad);
            Th.IconTopLeft(g, IconName, Th.D(Pad), top, ChipBox, Th.Accent);
            Th.TextMid(g, Title, Th.Card, Th.TextHi, NameLeft, NameMid);

            // the readings: the temperature over the fan speed, each line (number, gap, unit) ending on the right padding where
            // the history lines end, each unit on its number's top row; a sleeping GPU's "Asleep" has no unit and runs to the padding
            if (Loading || !Asleep) DrawUnit(g, 0);
            DrawUnit(g, 1);
            if (Loading)
            {
                // nothing read yet (start-up): placeholders where the numbers will be; the units are known, so they stay
                int cap = Th.CapHeight(Th.Stack);
                float w1 = Th.Measure("88", Th.Stack).Width, w2 = Th.Measure("8888", Th.Stack).Width;
                using (Brush sk = SkeletonBrush())
                {
                    Th.FillWith(g, sk, new RectangleF(NumberRight(0) - w1, LineTop(0), w1, cap), Th.D(3));
                    Th.FillWith(g, sk, new RectangleF(NumberRight(1) - w2, LineTop(1), w2, cap), Th.D(3));
                }
            }
            else
            {
                if (Asleep) Th.TextTopRight(g, "Asleep", Th.Stack, Th.Sub, (int)LineRight, LineTop(0), true);   // not a failure: say so; nothing past the lines' end
                else Number(g, Temp, NumberRight(0), LineTop(0));
                Number(g, Rpm, NumberRight(1), LineTop(1));
            }
            if ((Loading && Th.Motion) || (Growing && !Loading)) _anim.Start(); else _anim.Stop();

            float si = Th.D(SmallIcon);
            Th.Icon(g, "thermo", new RectangleF(HistoryIconMid - si / 2, Row1Mid - si / 2, si, si), Th.Accent);
            Th.Icon(g, "fan", new RectangleF(HistoryIconMid - si / 2, Row2Mid - si / 2, si, si), Th.Accent);
            if (Loading) { SkeletonLine(g, Row1Mid); SkeletonLine(g, Row2Mid); }
            else
            {
                Line(g, 0, Row1Mid);
                Line(g, 1, Row2Mid);
            }
            if (_hoverRow >= 0 && !Loading) DrawHover(g);   // nothing read yet: no slot to tell about
        }

        private static void Number(Graphics g, double v, int right, int top)
        {
            Th.TextTopRight(g, double.IsNaN(v) ? "--" : ((int)Math.Round(v)).ToString(), Th.Stack, double.IsNaN(v) ? Th.Sub : Th.TextHi, right, top, false);   // the numbers line up with each other
        }

        // ---- motion: the skeleton's band of light while nothing is read yet (start-up), and a hovered slot coming out
        internal readonly Timer _anim = new Timer { Interval = 15 };   // internal: the self-checks tick it

        // the tick stops itself too: a minimized window is never painted, so a stop in OnPaint alone would leave it running
        public ChipCard() { _anim.Tick += delegate { Invalidate(); if (!((Loading && Th.Motion) || (Growing && !Loading))) _anim.Stop(); }; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _anim.Dispose();
            base.Dispose(disposing);
        }

        // ---- hover: the slot under the mouse on either history line shows its value and age
        internal int _hoverRow = -1, _hoverSlot = -1;   // internal: the self-checks set and read them

        /// <summary>"now", "30s ago", "2min 30s ago": the age of the slot that many slots before the newest.</summary>
        public static string AgeText(int slotsBack)
        {
            int s = slotsBack * 30;
            if (s <= 0) return "now";
            return (s < 60 ? s + "s" : s / 60 + "min" + (s % 60 == 0 ? "" : " " + s % 60 + "s")) + " ago";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int row = -1, slot = -1;
            float band = Th.D(11);
            if (e.X >= LineLeft && e.X < LineRight)
            {
                if (Math.Abs(e.Y - Row1Mid) <= band) row = 0; else if (Math.Abs(e.Y - Row2Mid) <= band) row = 1;
                if (row >= 0) { slot = 0; while (slot < SlotCount - 1 && e.X >= SlotX(slot + 1)) slot++; }   // the slot drawn under the pointer
            }
            if (row != _hoverRow || slot != _hoverSlot)
            {
                _hoverRow = row; _hoverSlot = slot; _hoverSince = Environment.TickCount;   // the new slot comes out of the line
                if (Growing) _anim.Start();
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { if (_hoverRow >= 0) { _hoverRow = -1; _hoverSlot = -1; Invalidate(); } base.OnMouseLeave(e); }

        /// <summary>The hover pill's two lines for a slot of a row (0 temperature, 1 fan speed).</summary>
        public string[] HoverText(int row, int slot)
        {
            double x = SlotValue(row, slot);
            string value = double.IsNaN(x) ? "No reading" : row == 0 ? ((int)Math.Round(x)) + "°C" : ((int)Math.Round(x)) + " RPM";   // the units as on the card
            return new string[] { value, AgeText(SlotCount - 1 - slot) };
        }

        /// <summary>The hovered slot comes out of the line: its stretch grows from the line's 6 px to this height.</summary>
        public const int HoverBarH = 8;

        /// <summary>How long it takes to come out (ease-out); at once when Windows animations are off.</summary>
        public const int HoverGrowMs = 120;

        /// <summary>The hover bar's height (pixels) this long after the hover moved to its slot: it comes out of the line by
        /// whole pixels on each side (HoverBarH tall at 100 % and 125 %), so its edges end on whole pixels.</summary>
        public static float HoverBarHeight(int elapsedMs, bool motion)
        {
            double k = motion ? Th.EaseOut(elapsedMs / (double)HoverGrowMs) : 1;
            return (float)(LineHeight + 2 * HoverGrow * k);
        }

        private static int HoverGrow { get { return (int)Math.Round((Th.D(HoverBarH) - LineHeight) / 2); } }

        internal int _hoverSince;   // Environment.TickCount when the hover moved to its slot

        private bool Growing { get { return _hoverRow >= 0 && Th.Motion && unchecked(Environment.TickCount - _hoverSince) < HoverGrowMs; } }

        /// <summary>The hovered slot's own stretch of the line, a little taller (no dot), and a compact two-line pill above it:
        /// the value, then its age. Square like the line, except on the line's two ends, where its outer side is round.</summary>
        private void DrawHover(Graphics g)
        {
            string[] text = HoverText(_hoverRow, _hoverSlot);
            RectangleF band = LineBand(_hoverRow == 0 ? Row1Mid : Row2Mid);
            float x0 = SlotX(_hoverSlot), x1 = SlotX(_hoverSlot + 1), cx = (x0 + x1) / 2;
            float sh = HoverBarHeight(unchecked(Environment.TickCount - _hoverSince), Th.Motion), ro = sh / 2;
            Color c = LevelColor(_hoverRow, SlotValue(_hoverRow, _hoverSlot));
            RectangleF q = new RectangleF(x0, band.Y - (sh - band.Height) / 2, x1 - x0, sh);
            using (GraphicsPath p = new GraphicsPath())
            using (SolidBrush b = new SolidBrush(c))
            {
                if (_hoverSlot == 0) p.AddArc(q.X, q.Y, 2 * ro, 2 * ro, 90, 180); else p.AddLine(q.X, q.Bottom, q.X, q.Y);
                if (_hoverSlot == SlotCount - 1) p.AddArc(q.Right - 2 * ro, q.Y, 2 * ro, 2 * ro, 270, 180); else p.AddLine(q.Right, q.Y, q.Right, q.Bottom);
                p.CloseFigure();
                g.FillPath(b, p);
            }
            int cap = Th.CapHeight(Th.Mono), pad = Th.D(8), gap = Th.D(6);
            float pw = Math.Max(Th.Measure(text[0], Th.Mono).Width, Th.Measure(text[1], Th.Mono).Width) + 2 * pad, ph = 2 * cap + gap + 2 * pad;
            float px = (float)Math.Round(Math.Max(Th.D(Pad), Math.Min(Width - Th.D(Pad) - pw, cx - pw / 2))), py = Math.Max(Th.D(4), band.Y - HoverGrow - Th.D(6) - ph);
            Th.Fill(g, Th.Border, new RectangleF(px, py, pw, ph), Th.D(Th.RSmall));
            Th.TextTop(g, text[0], Th.Mono, Th.TextHi, (int)(px + pad), (int)(py + pad));
            Th.TextTop(g, text[1], Th.Mono, Th.Sub, (int)(px + pad), (int)(py + pad) + cap + gap);
        }

        /// <summary>A line's unit, its drawing starting at UnitBox's top-left corner.</summary>
        private void DrawUnit(Graphics g, int line)
        {
            RectangleF box = UnitBox(line);
            Rectangle ink = Th.TextInk(Unit(line), Th.Stack, false);
            Th.Text(g, Unit(line), Th.Stack, Th.Sub, (int)Math.Round(box.X) - ink.X, (int)Math.Round(box.Y) - ink.Y);
        }

        /// <summary>The skeleton's paint: a soft band of light sweeping across the card every 1.4 s over the pills' colour, a little lighter
        /// than the card (still when Windows animations are off).</summary>
        public static readonly Color SkeletonShine = ColorTranslator.FromHtml("#3E3B4A");

        private Brush SkeletonBrush()
        {
            if (!Th.Motion) return new SolidBrush(Th.Border);
            float w = Math.Max(1, Width), hb = w * 0.25f, l = -w, span = 3 * w;
            float cx = -hb + (Environment.TickCount % 1400) / 1400f * (w + 2 * hb);   // the band's middle, from just left of the card to just right of it
            LinearGradientBrush b = new LinearGradientBrush(new PointF(l, 0), new PointF(l + span, 0), Th.Border, Th.Border);
            ColorBlend cb = new ColorBlend(5);
            cb.Colors = new Color[] { Th.Border, Th.Border, SkeletonShine, Th.Border, Th.Border };
            cb.Positions = new float[] { 0, (cx - hb - l) / span, (cx - l) / span, (cx + hb - l) / span, 1 };
            b.InterpolationColors = cb;
            return b;
        }

        private void SkeletonLine(Graphics g, int cy)
        {
            RectangleF all = LineBand(cy);
            using (Brush sk = SkeletonBrush()) Th.FillWith(g, sk, all, all.Height / 2);
        }

        /// <summary>A history line's band on the row centred at cy, on whole pixels (an odd height has its extra row below).</summary>
        private RectangleF LineBand(int cy)
        {
            int h = (int)LineHeight;
            return new RectangleF(LineLeft, cy - h / 2, LineRight - LineLeft, h);
        }

        /// <summary>Where slot i starts on a history line, on a whole pixel (SlotX(SlotCount) is the line's right end).</summary>
        internal float SlotX(int i) { return (float)Math.Round(LineLeft + (LineRight - LineLeft) * i / SlotCount); }

        /// <summary>The value shown at a slot of a row (0 temperature, 1 fan speed), the newest at the right; NaN when the slot
        /// has no reading or is not filled yet.</summary>
        private double SlotValue(int row, int slot)
        {
            double[] v = row == 0 ? TempDots : RpmDots;
            int k = v.Length - SlotCount + slot;
            return k >= 0 && k < v.Length ? v[k] : double.NaN;
        }

        private static Color LevelColor(int row, double x) { return row == 0 ? Th.TempLevelColor(x) : Th.RpmLevelColor(x); }

        /// <summary>One history line: the last SlotCount values across the row, the newest at the right, missing ones grey;
        /// round only at its two ends.</summary>
        private void Line(Graphics g, int row, int cy)
        {
            Color[] c = new Color[SlotCount];
            for (int i = 0; i < SlotCount; i++) c[i] = LevelColor(row, SlotValue(row, i));
            RectangleF all = LineBand(cy);
            // each run fills the whole rounded line, clipped to its own slots: the fill is anti-aliased, so the two ends stay
            // round (a clip to the rounded outline is not, and squared them), and straight clips meet with no seam
            using (GraphicsPath line = Th.Round(all, all.Height / 2))
                foreach (int[] run in Stretches(c))
                {
                    GraphicsState st = g.Save();
                    g.SetClip(new RectangleF(SlotX(run[0]), all.Y - 1, SlotX(run[1]) - SlotX(run[0]), all.Height + 2), CombineMode.Intersect);
                    using (SolidBrush b = new SolidBrush(c[run[0]])) g.FillPath(b, line);
                    g.Restore(st);
                }
        }
    }

    /// <summary>The header's status: a pill with a coloured dot and one line, plus an "i" when there is something to do
    /// (the warning and what to do are the control's tooltip).</summary>
    public sealed class StatusPill : Ctl
    {
        public Color Dot = Th.Ok;
        public bool Info;

        /// <summary>The pill's whole width for a line (pixels): the padding, the dot, a gap, the text and the "i" when shown.</summary>
        public static int WidthFor(string text, bool info)
        {
            return Th.Measure(text, Th.Small).Width + 2 * Th.D(14) + Th.D(9) + Th.D(8) + (info ? Th.D(26) : 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
            float pad = Th.D(14), dot = Th.D(9), info = Info ? Th.D(26) : 0;
            float w = Math.Min(Width - 1, WidthFor(Text, Info));
            RectangleF r = new RectangleF(Width - 1 - w, 0, w, Height - 1);
            // the pill on whole pixel rows (rows 0 to Height - 1 span -0.5 to Height - 0.5): crisp top and bottom, same middle
            Th.Fill(g, Th.Border, new RectangleF(r.X, -0.5f, w, Height), Height / 2f);
            using (SolidBrush b = new SolidBrush(Dot)) g.FillEllipse(b, r.X + pad, r.Y + (r.Height - dot) / 2, dot, dot);
            float tx = r.X + pad + dot + Th.D(8);
            // its capitals centred on the pill's middle, the header's centre line (the renderer's VerticalCenter sits lower)
            int top = (int)Math.Round(r.Y + r.Height / 2) - Th.InkMid(Th.Small);
            Rectangle tr = Rectangle.Round(new RectangleF(tx, top, r.Right - pad - info - tx, Th.Measure("Hg", Th.Small).Height));
            TextRenderer.DrawText(g, Th.Sp(Text, Th.Small), Th.Small, tr, Th.TextHi, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (!Info) return;
            float d = Th.D(16), ix = r.Right - pad - d + Th.D(4), iy = r.Y + (r.Height - d) / 2;
            InfoMark(g, ix, iy, d, Th.TextHi, Th.Border);
        }

        /// <summary>The "i" in the icon set's style: a filled light disc with the letter cut out as geometry (a round dot over a
        /// stem with round ends), not a font's letter in a thin ring.</summary>
        public static void InfoMark(Graphics g, float x, float y, float d, Color disc, Color letter)
        {
            using (SolidBrush b = new SolidBrush(disc)) g.FillEllipse(b, x, y, d, d);
            float cx = x + d / 2, sw = Math.Max(1.6f, d * 0.15f), dr = sw * 0.62f;
            using (SolidBrush m = new SolidBrush(letter)) g.FillEllipse(m, cx - dr, y + d * 0.30f - dr, 2 * dr, 2 * dr);
            Th.Fill(g, letter, new RectangleF(cx - sw / 2, y + d * 0.44f, sw, d * 0.33f), sw / 2);
        }
    }
}
