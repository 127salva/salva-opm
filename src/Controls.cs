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
    /// <summary>Colours and fonts of the window: Ohman's dark palette (Theme.cs) with this app's accent #ED4549.
    /// Coordinates are written for 96 DPI and scaled with D(); fonts are in points and scale by themselves.</summary>
    public static class Th
    {
        public static readonly Color Accent = C("#ED4549"), Bg = C("#161311"), Sunken = C("#0F0D0B"),
            TextHi = C("#F4F1EF"), TextB = C("#EDEAE8"), SegText = C("#A8A3A0"), Sub = C("#96918D"),
            Axis = C("#847F7B"), Section = C("#787370"), GridH = C("#292623"), GridV = C("#211D1A"),
            PillBg = C("#312D2A"), Ok = C("#4AC06C"), Warn = C("#F3821D"), Off = C("#3A3532"), Hover = C("#201C19");

        private static Color C(string h) { return ColorTranslator.FromHtml(h); }

        public static float K = 1f;
        public static int D(double v) { return (int)Math.Round(v * K); }

        public static Font Title, Small, Mono, Seg, SegBold, Card, Big;

        public static void Init(float scale)
        {
            K = scale;
            string mono = "Consolas";
            using (InstalledFontCollection fc = new InstalledFontCollection())
                foreach (FontFamily f in fc.Families) if (f.Name == "Cascadia Mono") { mono = f.Name; break; }
            Title = new Font("Segoe UI Semibold", 14.5f);
            Small = new Font("Segoe UI", 9.75f);
            Mono = new Font(mono, 9.75f);
            Seg = new Font("Segoe UI", 11f);
            SegBold = new Font("Segoe UI Semibold", 11f);
            Card = new Font("Segoe UI Semibold", 12.5f);
            Big = new Font("Segoe UI Semibold", 22f);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Rounded window corners on Windows 11 (DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND); ignored before.</summary>
        public static void RoundCorners(IntPtr handle)
        {
            int round = 2;
            try { DwmSetWindowAttribute(handle, 33, ref round, 4); } catch (Exception) { }
        }

        /// <summary>The one radius scale (96 DPI units): small pills and inner focus rings, switch cells, every surface.</summary>
        public const double RSmall = 6, RCell = 10, RSurface = 14;
        /// <summary>Motion: the switch pill's slide and the cards' cross-fade (mouse only; skipped when Windows animations are off).</summary>
        public const int SlideMs = 180, FadeMs = 150;
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

        /// <summary>An ease-in-out (cubic) for something moving on screen: slow start, slow stop; t is clamped to 0..1.</summary>
        public static double EaseInOut(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        public static Color Mix(Color a, Color b, double k)
        {
            k = Math.Max(0, Math.Min(1, k));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * k), (int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
        }

        public static SizeF PillSize(string s) { return new SizeF(Measure(s, Mono).Width + D(14), D(18)); }

        /// <summary>A small readout pill (graph hover, curve point) with its top-left corner at (x, y).</summary>
        public static void Pill(Graphics g, string s, float x, float y)
        {
            SizeF p = PillSize(s);
            RectangleF r = new RectangleF(x, y, p.Width, p.Height);
            Fill(g, PillBg, r, D(RSmall));
            TextCenter(g, s, Mono, TextB, Rectangle.Round(r));   // centred by the renderer, as on every DPI
        }

        /// <summary>A chip temperature's colour: normal, amber from 85 C (the 85 C rule), red from 95 C (the thermal guard).</summary>
        public static Color TempColor(double t) { return double.IsNaN(t) ? Sub : t >= 95 ? Accent : t >= 85 ? Warn : TextHi; }

        /// <summary>The logo: the red diamond of the tray icon, centred on (cx, cy).</summary>
        public static void Diamond(Graphics g, float cx, float cy, float r)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(cx - r, cy - r), new PointF(cx + r, cy + r), Color.FromArgb(0xF6, 0x7A, 0x7D), Color.FromArgb(0xB8, 0x2A, 0x2E)))
                g.FillPolygon(b, new PointF[] { new PointF(cx, cy - r), new PointF(cx + r, cy), new PointF(cx, cy + r), new PointF(cx - r, cy) });
        }

        /// <summary>Line icons drawn in a square box: "leaf" (Eco), "scale" (Balanced), "bolt" (Performance), "chip".</summary>
        public static void Icon(Graphics g, string kind, RectangleF box, Color c)
        {
            float x = box.X, y = box.Y, s = Math.Min(box.Width, box.Height);
            Func<double, double, PointF> P = delegate(double u, double v) { return new PointF(x + (float)(u * s), y + (float)(v * s)); };
            using (Pen p = new Pen(c, Math.Max(1.5f, s / 14f)))
            {
                p.LineJoin = LineJoin.Round; p.StartCap = p.EndCap = LineCap.Round;
                if (kind == "leaf")
                {
                    using (GraphicsPath leaf = new GraphicsPath())
                    {
                        leaf.AddBezier(P(0.18, 0.82), P(0.08, 0.35), P(0.45, 0.10), P(0.90, 0.10));
                        leaf.AddBezier(P(0.90, 0.10), P(0.88, 0.58), P(0.55, 0.88), P(0.18, 0.82));
                        g.DrawPath(p, leaf);
                    }
                    g.DrawLine(p, P(0.10, 0.92), P(0.62, 0.38));
                }
                else if (kind == "scale")
                {
                    g.DrawLine(p, P(0.50, 0.12), P(0.50, 0.86));
                    g.DrawLine(p, P(0.30, 0.88), P(0.70, 0.88));
                    g.DrawLine(p, P(0.14, 0.26), P(0.86, 0.26));
                    foreach (double cxp in new double[] { 0.16, 0.84 })
                    {
                        g.DrawLine(p, P(cxp, 0.26), P(cxp - 0.12, 0.56));
                        g.DrawLine(p, P(cxp, 0.26), P(cxp + 0.12, 0.56));
                        PointF a = P(cxp - 0.14, 0.46);
                        g.DrawArc(p, a.X, a.Y, 0.28f * s, 0.20f * s, 0, 180);
                    }
                }
                else if (kind == "bolt")
                    g.DrawPolygon(p, new PointF[] { P(0.58, 0.06), P(0.18, 0.56), P(0.46, 0.56), P(0.38, 0.94), P(0.82, 0.40), P(0.54, 0.40), P(0.64, 0.06) });
                else if (kind == "chip")
                {
                    PointF o = P(0.24, 0.24), i = P(0.40, 0.40);
                    g.DrawRectangle(p, o.X, o.Y, 0.52f * s, 0.52f * s);
                    g.DrawRectangle(p, i.X, i.Y, 0.20f * s, 0.20f * s);
                    foreach (double k in new double[] { 0.38, 0.50, 0.62 })
                    {
                        g.DrawLine(p, P(k, 0.08), P(k, 0.24)); g.DrawLine(p, P(k, 0.76), P(k, 0.92));
                        g.DrawLine(p, P(0.08, k), P(0.24, k)); g.DrawLine(p, P(0.76, k), P(0.92, k));
                    }
                }
            }
        }

        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        public static Size Measure(string s, Font f) { return TextRenderer.MeasureText(s, f, Size.Empty, Flags); }
        public static void Text(Graphics g, string s, Font f, Color c, int x, int y) { TextRenderer.DrawText(g, s, f, new Point(x, y), c, Flags); }
        public static void TextRight(Graphics g, string s, Font f, Color c, int right, int y) { Text(g, s, f, c, right - Measure(s, f).Width, y); }
        public static void TextCenter(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            TextRenderer.DrawText(g, s, f, r, c, Flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

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
            using (Pen p = new Pen(Th.GridH)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Th.Fill(e.Graphics, Th.PillBg, new RectangleF(Th.D(2), 1, e.Item.Width - Th.D(4), e.Item.Height - 2), Th.D(Th.RSmall));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Th.TextHi : Th.TextB;
            e.TextRectangle = new Rectangle(Th.D(12), 0, e.Item.Width - Th.D(16), e.Item.Height);
            e.TextFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Th.GridH)) e.Graphics.DrawLine(p, Th.D(8), y, e.Item.Width - Th.D(8), y);
        }
    }

    /// <summary>Base for the owner-drawn controls: double-buffered, painted on the window background.</summary>
    public class Ctl : Control
    {
        public Ctl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Th.Bg;
            ForeColor = Th.TextB;
            TabStop = false;   // only the controls that take input are Tab stops (Seg, an editable CurveView)
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || k == Keys.Enter || base.IsInputKey(k);
        }

        /// <summary>The keyboard focus ring: drawn only after keyboard use (ShowFocusCues), never for mouse clicks.</summary>
        protected bool KeyboardFocus { get { return Focused && ShowFocusCues; } }

        protected static void FocusRing(Graphics g, RectangleF r, float radius)
        {
            using (Pen p = new Pen(Th.TextHi, Th.D(1.5)))
            using (GraphicsPath path = Th.Round(new RectangleF(r.X + Th.D(4), r.Y + Th.D(4), r.Width - Th.D(8), r.Height - Th.D(8)), radius))
                g.DrawPath(p, path);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>Ohman's segmented control: a sunken pill with the chosen item filled in the accent colour.</summary>
    public sealed class Seg : Ctl
    {
        public string[] Items = new string[0];
        public bool[] Disabled;
        public int Index;
        public event Action<int> Picked;
        /// <summary>Card style (the mode switch): one card per item, the icon named in Icons left of the name.</summary>
        public string[] Icons;
        private int _hover = -1, _focus = -1;   // _focus: the item the arrow keys are on

        public Seg()
        {
            TabStop = true;
            _anim.Interval = 15;
            _anim.Tick += delegate { Invalidate(); };
        }

        protected override void OnGotFocus(EventArgs e) { _focus = Index; base.OnGotFocus(e); }

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

        private void PaintFocus(Graphics g, float radius)
        {
            if (KeyboardFocus && _focus >= 0 && _focus < Items.Length) FocusRing(g, Cell(_focus), radius);
        }

        private RectangleF Cell(int i)
        {
            int n = Math.Max(1, Items.Length);
            if (Icons != null)
            {
                float gap = Th.D(12), cw = (Width - 1 - gap * (n - 1)) / n;
                return new RectangleF(i * (cw + gap), 0, cw, Height - 1);
            }
            float pad = Th.D(6), w = (Width - 2 * pad) / n;
            return new RectangleF(pad + i * w, pad, w, Height - 2 * pad);
        }

        private bool IsDisabled(int i) { return Disabled != null && i < Disabled.Length && Disabled[i]; }

        // ---- motion: the selection on screen follows Index; a mouse choice slides (switch) or fades (cards)
        private int _shown = -1, _shownCount, _from = -1;   // _from: the item the motion started from (-1: none)
        private RectangleF _fromRect;                     // the switch pill where it was when the motion started
        private int _since;
        private int _keyAt;        // when the keyboard last chose (Environment.TickCount); its change is shown at once
        private bool _keyChose;
        private readonly Timer _anim = new Timer();

        private int MotionMs { get { return Icons != null ? Th.FadeMs : Th.SlideMs; } }
        private double Progress() { return Th.EaseOut((Environment.TickCount - _since) / (double)MotionMs); }
        private static float Lerp(float a, float b, double k) { return (float)(a + (b - a) * k); }

        /// <summary>The switch pill as drawn now: between where it was and the chosen cell while it slides.</summary>
        private RectangleF Presented()
        {
            RectangleF to = Cell(_shown < 0 ? Index : _shown);
            if (_from < 0) return to;
            double k = Progress();
            return new RectangleF(Lerp(_fromRect.X, to.X, k), to.Y, Lerp(_fromRect.Width, to.Width, k), to.Height);
        }

        private void Track()
        {
            if (Index == _shown && Items.Length == _shownCount) return;   // nothing changed (also the repaints WMI calls cause mid-apply)
            bool byKey = _keyChose && Environment.TickCount - _keyAt < 5000;
            if (!byKey && _shown >= 0 && Items.Length == _shownCount && Th.Motion)
            {
                _fromRect = Presented();   // from the current on-screen place, so a second click mid-way does not jump
                _from = _shown;
                _since = Environment.TickCount;
                _anim.Start();
            }
            else { _from = -1; _anim.Stop(); }
            _shown = Index;
            _shownCount = Items.Length;
            _keyChose = false;
        }

        private void EndMotionIfDone()
        {
            if (_from >= 0 && Environment.TickCount - _since >= MotionMs) { _from = -1; _anim.Stop(); }
        }

        // ---- press: the pressed look on mouse-down; the choice applies on release over the same item
        private int _press = -1;
        private bool _pressInside;
        private static readonly Color Pressed = Color.FromArgb(0x0A, 0x08, 0x07);

        private bool IsPressed(int i) { return i == _press && _pressInside; }

        private static RectangleF Inset(RectangleF r, float d) { return new RectangleF(r.X + d, r.Y + d, r.Width - 2 * d, r.Height - 2 * d); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
            Track();
            if (Icons != null) PaintCards(g); else PaintSwitch(g);
            EndMotionIfDone();
        }

        private void PaintSwitch(Graphics g)
        {
            RectangleF frame = new RectangleF(0, 0, Width - 1, Height - 1);
            Th.Fill(g, Th.Sunken, frame, Th.D(Th.RSurface));
            for (int i = 0; i < Items.Length; i++)
            {
                if (i == Index || IsDisabled(i)) continue;
                if (IsPressed(i)) Th.Fill(g, Pressed, Inset(Cell(i), Th.D(2)), Th.D(Th.RCell));
                else if (i == _hover) Th.Fill(g, Th.Hover, Cell(i), Th.D(Th.RCell));
            }
            if (Index >= 0 && Index < Items.Length) Th.Fill(g, IsDisabled(Index) ? Th.Off : Th.Accent, Presented(), Th.D(Th.RCell));
            for (int i = 0; i < Items.Length; i++)
            {
                Color c = i == Index ? Color.White : IsDisabled(i) ? Th.Section : i == _hover ? Th.TextB : Th.SegText;
                Th.TextCenter(g, Items[i], i == Index ? Th.SegBold : Th.Seg, c, Rectangle.Round(Cell(i)));
            }
            PaintFocus(g, Th.D(Th.RSmall));
        }

        private static readonly Color Selected = Color.FromArgb(0x2A, 0x19, 0x18);

        private void PaintCards(Graphics g)
        {
            double k = _from >= 0 ? Progress() : 1;
            for (int i = 0; i < Items.Length; i++)
            {
                bool on = i == Index, off = IsDisabled(i);
                double a = on ? k : i == _from ? 1 - k : 0;   // how selected it looks: fades in on the new card, out on the old
                RectangleF r = IsPressed(i) ? Inset(Cell(i), Th.D(2)) : Cell(i);
                Color baseFill = IsPressed(i) ? Pressed : i == _hover && !off && !on ? Th.Hover : Th.Sunken;
                Th.Fill(g, Th.Mix(baseFill, Selected, a), r, Th.D(Th.RSurface));
                if (a > 0)
                    using (Pen b = new Pen(Color.FromArgb((int)(255 * a), off ? Th.Off : Th.Accent), Th.D(2)))
                    using (GraphicsPath p = Th.Round(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), Th.D(Th.RSurface)))
                        g.DrawPath(b, p);
                Color c = off ? Th.Section : on ? Th.TextHi : i == _hover ? Th.TextB : Th.SegText;
                float s = Th.D(30);
                Th.Icon(g, Icons[i], new RectangleF(r.X + Th.D(22), r.Y + (r.Height - s) / 2, s, s), off ? Th.Section : on ? Th.Accent : Th.SegText);
                Size ts = Th.Measure(Items[i], Th.Card);
                Th.Text(g, Items[i], Th.Card, c, (int)(r.X + Th.D(66)), (int)(r.Y + (r.Height - ts.Height) / 2));
            }
            PaintFocus(g, Th.D(Th.RSurface - 4));
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
    /// <summary>One chip's panel: name, temperature, a ring gauge of its fan speed and a graph of the last minutes.</summary>
    public sealed class ChipPanel : Ctl
    {
        public string Title = "";
        public double Temp = double.NaN;
        public bool Asleep;
        public int Rpm = -1;                   // -1 = no recent reading
        public int MaxRpm = HpBios.FanMax * 100;
        public double[] History = new double[0];
        public int HistoryCapacity = 60;
        public int SecondsPerPoint = 5;
        public string GapText = "--";          // shown when hovering a gap ("asleep" for the GPU)
        private const double GraphMin = 30, GraphMax = 100;
        private int _hover = -1;               // history index under the mouse

        private static string Duration(int s) { return s < 60 ? s + " s" : s / 60 + " min" + (s % 60 == 0 ? "" : " " + s % 60 + " s"); }

        /// <summary>"now", "35 s ago", "3 min 20 s ago": the age of point index of count.</summary>
        public static string AgeText(int index, int count, int secondsPerPoint)
        {
            int s = (count - 1 - index) * secondsPerPoint;
            return s <= 0 ? "now" : Duration(s) + " ago";
        }

        /// <summary>The time a graph spans: "40 s" while it fills up, the full "5 min" once it holds capacity points.</summary>
        public static string SpanText(int count, int capacity, int secondsPerPoint)
        {
            return Duration((count >= capacity ? capacity : count - 1) * secondsPerPoint);
        }

        // the ring glides to each new reading (the number shows it at once)
        private const int RingMs = 600;
        private double _ringFrom, _ringTo = -1;
        private int _ringSince;
        private bool _gliding;
        private readonly Timer _ringTimer = new Timer();

        public ChipPanel()
        {
            _ringTimer.Interval = 15;
            _ringTimer.Tick += delegate { Invalidate(); };
        }

        private double RingValue()
        {
            if (_ringTo < 0) return 0;
            if (!_gliding) return _ringTo;
            return _ringFrom + (_ringTo - _ringFrom) * Th.EaseInOut((Environment.TickCount - _ringSince) / (double)RingMs);
        }

        private void GlideTo(double target)
        {
            if (target == _ringTo)
            {
                if (_gliding && Environment.TickCount - _ringSince >= RingMs) { _gliding = false; _ringTimer.Stop(); }
                return;
            }
            if (_ringTo < 0 || !Th.Motion) { _ringTo = target; _gliding = false; _ringTimer.Stop(); return; }   // first reading, or animations off
            _ringFrom = RingValue();   // from where it is drawn now, so a reading mid-glide does not jump
            _ringTo = target;
            _ringSince = Environment.TickCount;
            _gliding = true;
            _ringTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _ringTimer.Dispose();
            base.Dispose(disposing);
        }

        private void Graph(out float x0, out float x1, out float y0, out float y1)
        {
            x0 = Th.D(140); x1 = Width - Th.D(16); y0 = Th.D(66); y1 = Height - Th.D(32);
        }

        /// <summary>Point spacing: the readings so far spread over the full width until it holds capacity points.</summary>
        private float Step(float x0, float x1) { return (x1 - x0) / Math.Max(1, Math.Min(History.Length, HistoryCapacity) - 1); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            float x0, x1, y0, y1;
            Graph(out x0, out x1, out y0, out y1);
            int n = History.Length, h = -1;
            if (n > 0 && e.X >= x0 - Th.D(6) && e.X <= x1 + Th.D(6) && e.Y >= y0 - Th.D(20) && e.Y <= y1 + Th.D(6))
                h = Math.Max(0, Math.Min(n - 1, n - 1 - (int)Math.Round((x1 - e.X) / Step(x0, x1))));
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { if (_hover >= 0) { _hover = -1; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
            Th.Fill(g, Th.Sunken, new RectangleF(0, 0, Width - 1, Height - 1), Th.D(Th.RSurface));
            Th.Icon(g, "chip", new RectangleF(Th.D(16), Th.D(14), Th.D(26), Th.D(26)), Th.Accent);
            Th.Text(g, Title, Th.Card, Th.TextHi, Th.D(50), Th.D(15));
            string t = Asleep ? "asleep" : double.IsNaN(Temp) ? "--" : ((int)Math.Round(Temp)) + " °C";
            Th.TextRight(g, t, Asleep ? Th.Seg : Th.Big, Asleep ? Th.Sub : Th.TempColor(Temp), Width - Th.D(16), Asleep ? Th.D(16) : Th.D(6));

            // ring gauge: fan speed against the top of the fan table
            float cx = Th.D(72), cy = Th.D(124), rad = Th.D(46), w = Th.D(9);
            RectangleF ring = new RectangleF(cx - rad, cy - rad, 2 * rad, 2 * rad);
            using (Pen bg = new Pen(Th.GridH, w)) g.DrawEllipse(bg, ring);
            GlideTo(Rpm > 0 ? Math.Min(Rpm, MaxRpm) : 0);
            double shown = RingValue();
            if (shown > 0)
                using (Pen fg = new Pen(Th.Accent, w))
                {
                    fg.StartCap = fg.EndCap = LineCap.Round;
                    g.DrawArc(fg, ring, -90, (float)(360 * shown / MaxRpm));
                }
            // centred by the renderer itself: measured widths of the semibold font leave it off-centre
            int box = (int)(2 * rad);
            Th.TextCenter(g, Rpm >= 0 ? Rpm.ToString() : "--", Th.Title, Th.TextHi, new Rectangle((int)(cx - rad), (int)(cy - Th.D(26)), box, Th.D(30)));
            Th.TextCenter(g, "rpm", Th.Small, Th.Sub, new Rectangle((int)(cx - rad), (int)(cy + Th.D(4)), box, Th.D(18)));

            // temperature graph, newest point at the right edge
            float x0, x1, y0, y1;
            Graph(out x0, out x1, out y0, out y1);
            using (Pen gv = new Pen(Th.GridV, 1))
                foreach (double v in new double[] { 40, 60, 80, 100 })
                {
                    float gy = Gy(v, y0, y1);
                    g.DrawLine(gv, x0, gy, x1, gy);
                }
            if (History.Length >= 2) Th.Text(g, SpanText(History.Length, HistoryCapacity, SecondsPerPoint), Th.Small, Th.Axis, (int)x0, (int)y1 + Th.D(6));
            Th.TextRight(g, "now", Th.Small, Th.Axis, (int)x1, (int)y1 + Th.D(6));
            float step = Step(x0, x1);
            int n = History.Length, start = 0;
            while (start < n)
            {
                while (start < n && double.IsNaN(History[start])) start++;
                int end = start;
                while (end < n && !double.IsNaN(History[end])) end++;
                if (end - start >= 2) DrawRun(g, start, end, n, step, x1, y0, y1);
                start = end;
            }
            if (n < 2) Th.TextCenter(g, "Collecting…", Th.Small, Th.Sub, Rectangle.Round(new RectangleF(x0, y0, x1 - x0, y1 - y0)));
            if (_hover >= 0 && _hover < n) DrawHover(g, n, step, x0, x1, y0, y1);
        }

        /// <summary>The point under the mouse: a vertical line, a dot on the curve and a pill with its temperature and age.</summary>
        private void DrawHover(Graphics g, int n, float step, float x0, float x1, float y0, float y1)
        {
            double t = History[_hover];
            float x = x1 - (n - 1 - _hover) * step, dy = double.IsNaN(t) ? y1 : Gy(t, y0, y1);
            using (Pen v = new Pen(Th.Axis, 1)) g.DrawLine(v, x, y0, x, y1);
            if (!double.IsNaN(t))
            {
                float r = Th.D(4.5);
                using (SolidBrush bg = new SolidBrush(Th.Sunken)) g.FillEllipse(bg, x - r, dy - r, 2 * r, 2 * r);
                using (Pen ring = new Pen(Th.Accent, Th.D(2))) g.DrawEllipse(ring, x - r, dy - r, 2 * r, 2 * r);
            }
            string label = (double.IsNaN(t) ? GapText : ((int)Math.Round(t)) + " °C") + " · " + AgeText(_hover, n, SecondsPerPoint);
            SizeF ps = Th.PillSize(label);
            float px = Math.Max(x0, Math.Min(x1 - ps.Width, x - ps.Width / 2)), py = dy - Th.D(12) - ps.Height;
            if (py < y0 - Th.D(2)) py = dy + Th.D(12);   // no room above (a hot point): put it under the dot
            Th.Pill(g, label, px, py);
        }

        private static float Gy(double t, float y0, float y1)
        {
            double k = (Math.Max(GraphMin, Math.Min(GraphMax, t)) - GraphMin) / (GraphMax - GraphMin);
            return (float)(y1 - k * (y1 - y0));
        }

        /// <summary>One unbroken stretch of readings (a sleeping GPU leaves a gap).</summary>
        private void DrawRun(Graphics g, int start, int end, int n, float step, float x1, float y0, float y1)
        {
            PointF[] pts = new PointF[end - start];
            for (int i = start; i < end; i++) pts[i - start] = new PointF(x1 - (n - 1 - i) * step, Gy(History[i], y0, y1));
            using (GraphicsPath fill = new GraphicsPath())
            {
                fill.AddLine(pts[0].X, y1, pts[0].X, pts[0].Y);
                fill.AddLines(pts);
                fill.AddLine(pts[pts.Length - 1].X, pts[pts.Length - 1].Y, pts[pts.Length - 1].X, y1);
                fill.CloseFigure();
                using (SolidBrush b = new SolidBrush(Color.FromArgb(0x26, Th.Accent))) g.FillPath(b, fill);
            }
            using (Pen line = new Pen(Th.Accent, Th.D(2))) { line.LineJoin = LineJoin.Round; g.DrawLines(line, pts); }
        }
    }

    /// <summary>The header's status: a pill with a coloured dot and one line, plus an "i" when there is something to do
    /// (the warning and what to do are the control's tooltip).</summary>
    public sealed class StatusPill : Ctl
    {
        public Color Dot = Th.Ok;
        public bool Info;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
            float pad = Th.D(14), dot = Th.D(9), info = Info ? Th.D(26) : 0;
            float w = Math.Min(Width - 1, Th.Measure(Text, Th.Small).Width + 2 * pad + dot + Th.D(8) + info);
            RectangleF r = new RectangleF(Width - 1 - w, 0, w, Height - 1);
            Th.Fill(g, Th.PillBg, r, r.Height / 2);
            using (SolidBrush b = new SolidBrush(Dot)) g.FillEllipse(b, r.X + pad, r.Y + (r.Height - dot) / 2, dot, dot);
            float tx = r.X + pad + dot + Th.D(8);
            Rectangle tr = Rectangle.Round(new RectangleF(tx, r.Y, r.Right - pad - info - tx, r.Height));
            TextRenderer.DrawText(g, Text, Th.Small, tr, Th.TextB, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            if (!Info) return;
            float d = Th.D(16), ix = r.Right - pad - d + Th.D(4), iy = r.Y + (r.Height - d) / 2;
            using (Pen p = new Pen(Th.TextB, Th.D(1.5))) g.DrawEllipse(p, ix, iy, d, d);
            Th.TextCenter(g, "i", Th.SegBold, Th.TextB, Rectangle.Round(new RectangleF(ix, iy - Th.D(1), d, d)));
        }
    }

    /// <summary>The fan-curve graph, in Ohman's style (Curve.cs): points at fixed temperatures, dragged up and down only.
    /// The y axis is percent of the fan's top speed; the point under the mouse shows its percentage. Shift-drag moves the
    /// whole curve; a curve never falls as it gets hotter.</summary>
    public sealed class CurveView : Ctl
    {
        public int[] Temps = FanCurve.CustomTemps;
        public int[] Levels = (int[])FanCurve.DefaultCustom.Clone();
        public int Floor = HpBios.FanMin, Ceiling = HpBios.FanMax;
        public bool ReadOnly;
        /// <summary>Shown instead of the curve (the firmware's own curve cannot be read); null draws the curve.</summary>
        public string Message;
        public event Action<int[]> Committed;
        private int _drag = -1, _hover = -1;

        public bool Dragging { get { return _drag >= 0 || _keyDirty; } }

        private float Top0 { get { return Th.D(24); } }
        private float PlotH { get { return Height - Top0 - Th.D(30); } }
        private float X0 { get { return Th.D(40); } }
        private float X1 { get { return Width - Th.D(10); } }
        private float X(double t) { return X0 + (float)((t - Temps[0]) / (Temps[Temps.Length - 1] - Temps[0])) * (X1 - X0); }
        private float Y(double lvl) { return Top0 + PlotH * (float)(1 - lvl / Ceiling); }
        private int LevelAt(float y) { return (int)Math.Round((1 - (y - Top0) / PlotH) * Ceiling); }
        /// <summary>Lowest level each point may take (OGH's lower bound per temperature); null = Floor everywhere.</summary>
        public int[] Floors;
        private int Legal(int i, int lvl) { return Math.Max(Floors != null ? Floors[i] : Floor, Math.Min(Ceiling, lvl)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
            Th.Fill(g, Th.Sunken, new RectangleF(0, 0, Width - 1, Height - 1), Th.D(Th.RSurface));
            if (Message != null) { Th.TextCenter(g, Message, Th.Seg, Th.Sub, new Rectangle(0, 0, Width, Height)); return; }
            using (Pen gh = new Pen(Th.GridH, 1))
                foreach (int pct in new int[] { 100, 75, 50, 25, 0 })
                {
                    float y = (float)Math.Round(Y(Ceiling * pct / 100.0)) + 0.5f;
                    g.DrawLine(gh, X0, y, X1, y);
                    // centred on its gridline by the renderer (a fixed offset drifts with the DPI)
                    TextRenderer.DrawText(g, pct.ToString(), Th.Small, new Rectangle(0, (int)y - Th.D(10), (int)X0 - Th.D(8), Th.D(20)), Th.Axis,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
            using (Pen gv = new Pen(Th.GridV, 1))
                for (int i = 0; i < Temps.Length; i++)
                {
                    float x = (float)Math.Round(X(Temps[i])) + 0.5f;
                    g.DrawLine(gv, x, Top0, x, Top0 + PlotH);
                    string s = Temps[i] + "°";
                    int w = Th.Measure(s, Th.Small).Width;
                    int tx = i == 0 ? (int)X0 : i == Temps.Length - 1 ? (int)X1 - w : (int)x - w / 2;
                    Th.Text(g, s, Th.Small, Th.Axis, tx, (int)(Top0 + PlotH + Th.D(10)));
                }
            PointF[] pts = new PointF[Temps.Length];
            for (int i = 0; i < Temps.Length; i++) pts[i] = new PointF(X(Temps[i]), Y(Levels[i]));
            using (GraphicsPath fill = new GraphicsPath())
            {
                fill.AddLine(X0, Top0 + PlotH, pts[0].X, pts[0].Y);
                fill.AddLines(pts);
                fill.AddLine(pts[pts.Length - 1].X, pts[pts.Length - 1].Y, X1, Top0 + PlotH);
                fill.CloseFigure();
                using (SolidBrush b = new SolidBrush(Color.FromArgb(0x1F, Th.Accent))) g.FillPath(b, fill);
            }
            using (Pen line = new Pen(Th.Accent, Th.D(2.5)))
            {
                line.LineJoin = LineJoin.Round;
                g.DrawLines(line, pts);
            }
            if (ReadOnly) return;
            using (Pen ring = new Pen(Th.Accent, Th.D(2.5)))
            using (SolidBrush bg = new SolidBrush(Th.Sunken))
                for (int i = 0; i < pts.Length; i++)
                {
                    float r = Th.D(i == _drag || i == _hover || (KeyboardFocus && i == _key) ? 6 : 4.5);
                    g.FillEllipse(bg, pts[i].X - r, pts[i].Y - r, 2 * r, 2 * r);
                    g.DrawEllipse(ring, pts[i].X - r, pts[i].Y - r, 2 * r, 2 * r);
                }
            if (KeyboardFocus) FocusRing(g, new RectangleF(0, 0, Width - 1, Height - 1), Th.D(Th.RSurface - 4));
            int p = _drag >= 0 ? _drag : _hover >= 0 ? _hover : KeyboardFocus ? _key : -1;
            if (p < 0) return;
            string label = (int)Math.Round(100.0 * Levels[p] / Ceiling) + "%";
            SizeF ps = Th.PillSize(label);
            Th.Pill(g, label, Math.Max(X0, Math.Min(X1 - ps.Width, pts[p].X - ps.Width / 2)), Math.Max(0, pts[p].Y - Th.D(12) - ps.Height));
        }

        private int HandleAt(Point p)
        {
            if (ReadOnly || Message != null) return -1;
            for (int i = 0; i < Temps.Length; i++) if (Math.Abs(p.X - X(Temps[i])) <= Th.D(13) && Math.Abs(p.Y - Y(Levels[i])) <= Th.D(40)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag >= 0 && e.Button == MouseButtons.Left)
            {
                if (MovePoint(_drag, LevelAt(e.Y), (ModifierKeys & Keys.Shift) != 0)) Invalidate();
                return;
            }
            int h = HandleAt(e.Location);
            if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        /// <summary>Point i to level lvl (Shift: the whole curve by the same step); the curve never falls as it gets hotter.</summary>
        private bool MovePoint(int i, int lvl, bool shift)
        {
            lvl = Legal(i, lvl);
            if (lvl == Levels[i]) return false;
            if (shift)
            {
                int d = lvl - Levels[i];
                for (int k = 0; k < Levels.Length; k++) Levels[k] = Legal(k, Levels[k] + d);
            }
            else
            {
                Levels[i] = lvl;
                for (int k = i + 1; k < Levels.Length; k++) if (Levels[k] < lvl) Levels[k] = lvl;
                for (int k = i - 1; k >= 0; k--) if (Levels[k] > lvl) Levels[k] = Legal(k, lvl);
            }
            for (int k = 1; k < Levels.Length; k++) if (Levels[k] < Levels[k - 1]) Levels[k] = Levels[k - 1];   // floors differ per point: keep it rising
            return true;
        }

        // keyboard: Left/Right pick a point, Up/Down move it one level (Shift: the whole curve); saved when the key is released
        private int _key;
        private bool _keyDirty;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!ReadOnly && Message == null)
            {
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                {
                    _key = Math.Max(0, Math.Min(Levels.Length - 1, _key + (e.KeyCode == Keys.Left ? -1 : 1)));
                    Invalidate(); e.Handled = true;
                }
                else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    if (MovePoint(_key, Levels[_key] + (e.KeyCode == Keys.Up ? 1 : -1), e.Shift)) { _keyDirty = true; Invalidate(); }
                    e.Handled = true;
                }
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (_keyDirty) { _keyDirty = false; Action<int[]> h = Committed; if (h != null) h((int[])Levels.Clone()); }
            base.OnKeyUp(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) { _drag = HandleAt(e.Location); if (_drag >= 0) { Capture = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { EndDrag(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { EndDrag(); base.OnMouseCaptureChanged(e); }
        protected override void OnMouseLeave(EventArgs e) { if (_hover != -1 && _drag < 0) { _hover = -1; Invalidate(); } }

        private void EndDrag()
        {
            if (_drag < 0) return;
            _drag = -1;
            Capture = false;
            Invalidate();
            Action<int[]> h = Committed;
            if (h != null) h((int[])Levels.Clone());
        }
    }
}
