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

    /// <summary>Base for the owner-drawn controls: double-buffered, painted on the window background.</summary>
    public class Ctl : Control
    {
        public Ctl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Th.Bg;
            ForeColor = Th.TextB;
        }
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
        private int _hover = -1;

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

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
            if (Icons != null) { PaintCards(g); return; }
            Th.Fill(g, Th.Sunken, new RectangleF(0, 0, Width - 1, Height - 1), Th.D(12));
            for (int i = 0; i < Items.Length; i++)
            {
                RectangleF r = Cell(i);
                if (i == Index) Th.Fill(g, IsDisabled(i) ? Th.Off : Th.Accent, r, Th.D(9));
                else if (i == _hover && !IsDisabled(i)) Th.Fill(g, Th.Hover, r, Th.D(9));
                Color c = i == Index ? Color.White : IsDisabled(i) ? Th.Section : i == _hover ? Th.TextB : Th.SegText;
                Th.TextCenter(g, Items[i], i == Index ? Th.SegBold : Th.Seg, c, Rectangle.Round(r));
            }
        }

        private void PaintCards(Graphics g)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                RectangleF r = Cell(i);
                bool on = i == Index, off = IsDisabled(i);
                Th.Fill(g, on ? Color.FromArgb(0x2A, 0x19, 0x18) : i == _hover && !off ? Th.Hover : Th.Sunken, r, Th.D(12));
                if (on)
                    using (Pen b = new Pen(off ? Th.Off : Th.Accent, Th.D(2)))
                    using (GraphicsPath p = Th.Round(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), Th.D(12)))
                        g.DrawPath(b, p);
                Color c = off ? Th.Section : on ? Th.TextHi : i == _hover ? Th.TextB : Th.SegText;
                float s = Th.D(30);
                Th.Icon(g, Icons[i], new RectangleF(r.X + Th.D(22), r.Y + (r.Height - s) / 2, s, s), off ? Th.Section : on ? Th.Accent : Th.SegText);
                Size ts = Th.Measure(Items[i], Th.Card);
                Th.Text(g, Items[i], Th.Card, c, (int)(r.X + Th.D(66)), (int)(r.Y + (r.Height - ts.Height) / 2));
            }
        }

        private int At(Point p)
        {
            for (int i = 0; i < Items.Length; i++) if (Cell(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = At(e.Location);
            Cursor = h >= 0 && h != Index && !IsDisabled(h) ? Cursors.Hand : Cursors.Default;   // a hand only over something clickable
            if (h != _hover) { _hover = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = At(e.Location);
            if (i < 0 || i == Index || IsDisabled(i)) return;
            Action<int> h = Picked;
            if (h != null) h(i);
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
        public string HistoryLabel = "";
        private const double GraphMin = 30, GraphMax = 100;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
            Th.Fill(g, Th.Sunken, new RectangleF(0, 0, Width - 1, Height - 1), Th.D(14));
            Th.Icon(g, "chip", new RectangleF(Th.D(16), Th.D(14), Th.D(26), Th.D(26)), Th.Accent);
            Th.Text(g, Title, Th.Card, Th.TextHi, Th.D(50), Th.D(15));
            string t = Asleep ? "asleep" : double.IsNaN(Temp) ? "--" : ((int)Math.Round(Temp)) + " °C";
            Th.TextRight(g, t, Asleep ? Th.Seg : Th.Big, Asleep ? Th.Sub : Th.TempColor(Temp), Width - Th.D(16), Asleep ? Th.D(16) : Th.D(6));

            // ring gauge: fan speed against the top of the fan table
            float cx = Th.D(72), cy = Th.D(124), rad = Th.D(46), w = Th.D(9);
            RectangleF ring = new RectangleF(cx - rad, cy - rad, 2 * rad, 2 * rad);
            using (Pen bg = new Pen(Th.GridH, w)) g.DrawEllipse(bg, ring);
            if (Rpm > 0)
                using (Pen fg = new Pen(Th.Accent, w))
                {
                    fg.StartCap = fg.EndCap = LineCap.Round;
                    g.DrawArc(fg, ring, -90, 360f * Math.Min(Rpm, MaxRpm) / MaxRpm);
                }
            // centred by the renderer itself: measured widths of the semibold font leave it off-centre
            int box = (int)(2 * rad);
            Th.TextCenter(g, Rpm >= 0 ? Rpm.ToString() : "--", Th.Title, Th.TextHi, new Rectangle((int)(cx - rad), (int)(cy - Th.D(26)), box, Th.D(30)));
            Th.TextCenter(g, "rpm", Th.Small, Th.Sub, new Rectangle((int)(cx - rad), (int)(cy + Th.D(4)), box, Th.D(18)));

            // temperature graph, newest point at the right edge
            float x0 = Th.D(140), x1 = Width - Th.D(16), y0 = Th.D(66), y1 = Height - Th.D(32);
            using (Pen gv = new Pen(Th.GridV, 1))
                foreach (double v in new double[] { 40, 60, 80, 100 })
                {
                    float gy = Gy(v, y0, y1);
                    g.DrawLine(gv, x0, gy, x1, gy);
                }
            Th.Text(g, HistoryLabel, Th.Small, Th.Axis, (int)x0, (int)y1 + Th.D(6));
            Th.TextRight(g, "now", Th.Small, Th.Axis, (int)x1, (int)y1 + Th.D(6));
            float step = (x1 - x0) / Math.Max(1, HistoryCapacity - 1);
            int n = History.Length, start = 0;
            while (start < n)
            {
                while (start < n && double.IsNaN(History[start])) start++;
                int end = start;
                while (end < n && !double.IsNaN(History[end])) end++;
                if (end - start >= 2) DrawRun(g, start, end, n, step, x1, y0, y1);
                start = end;
            }
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

    /// <summary>The header's status: a pill with a coloured dot and one line (the full text is the control's tooltip).</summary>
    public sealed class StatusPill : Ctl
    {
        public Color Dot = Th.Ok;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
            float pad = Th.D(14), dot = Th.D(9);
            float w = Math.Min(Width - 1, Th.Measure(Text, Th.Small).Width + 2 * pad + dot + Th.D(8));
            RectangleF r = new RectangleF(Width - 1 - w, 0, w, Height - 1);
            Th.Fill(g, Th.PillBg, r, r.Height / 2);
            using (SolidBrush b = new SolidBrush(Dot)) g.FillEllipse(b, r.X + pad, r.Y + (r.Height - dot) / 2, dot, dot);
            Rectangle tr = Rectangle.Round(new RectangleF(r.X + pad + dot + Th.D(8), r.Y, r.Right - pad - (r.X + pad + dot + Th.D(8)), r.Height));
            TextRenderer.DrawText(g, Text, Th.Small, tr, Th.TextB, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
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
        public event Action<int[]> Committed;
        private int _drag = -1, _hover = -1;

        public bool Dragging { get { return _drag >= 0; } }

        private float Top0 { get { return Th.D(24); } }
        private float PlotH { get { return Height - Top0 - Th.D(30); } }
        private float X0 { get { return Th.D(34); } }
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
            Th.Fill(g, Th.Sunken, new RectangleF(0, 0, Width - 1, Height - 1), Th.D(14));
            using (Pen gh = new Pen(Th.GridH, 1))
                foreach (int pct in new int[] { 100, 75, 50, 25, 0 })
                {
                    float y = (float)Math.Round(Y(Ceiling * pct / 100.0)) + 0.5f;
                    g.DrawLine(gh, X0, y, X1, y);
                    Th.TextRight(g, pct.ToString(), Th.Small, Th.Axis, (int)X0 - Th.D(6), (int)y - Th.D(8));
                }
            using (Pen gv = new Pen(Th.GridV, 1))
                for (int i = 0; i < Temps.Length; i++)
                {
                    float x = (float)Math.Round(X(Temps[i])) + 0.5f;
                    g.DrawLine(gv, x, Top0, x, Top0 + PlotH);
                    string s = Temps[i] + "°";
                    int w = Th.Measure(s, Th.Small).Width;
                    int tx = i == 0 ? (int)X0 : i == Temps.Length - 1 ? (int)X1 - w : (int)x - w / 2;
                    Th.Text(g, s, Th.Small, Th.Axis, tx, (int)(Top0 + PlotH + Th.D(8)));
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
                    float r = Th.D(i == _drag || i == _hover ? 6 : 4.5);
                    g.FillEllipse(bg, pts[i].X - r, pts[i].Y - r, 2 * r, 2 * r);
                    g.DrawEllipse(ring, pts[i].X - r, pts[i].Y - r, 2 * r, 2 * r);
                }
            int p = _drag >= 0 ? _drag : _hover;
            if (p < 0) return;
            string label = (int)Math.Round(100.0 * Levels[p] / Ceiling) + "%";
            Size ls = Th.Measure(label, Th.Mono);
            float pw = ls.Width + Th.D(14), ph = Th.D(18);
            float px = Math.Max(X0, Math.Min(X1 - pw, pts[p].X - pw / 2)), py = Math.Max(0, pts[p].Y - Th.D(12) - ph);
            Th.Fill(g, Th.PillBg, new RectangleF(px, py, pw, ph), Th.D(5));
            Th.Text(g, label, Th.Mono, Th.TextB, (int)(px + Th.D(7)), (int)(py + (ph - ls.Height) / 2f));
        }

        private int HandleAt(Point p)
        {
            if (ReadOnly) return -1;
            for (int i = 0; i < Temps.Length; i++) if (Math.Abs(p.X - X(Temps[i])) <= Th.D(13) && Math.Abs(p.Y - Y(Levels[i])) <= Th.D(40)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag >= 0 && e.Button == MouseButtons.Left)
            {
                int lvl = Legal(_drag, LevelAt(e.Y));
                if (lvl == Levels[_drag]) return;
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    int d = lvl - Levels[_drag];
                    for (int i = 0; i < Levels.Length; i++) Levels[i] = Legal(i, Levels[i] + d);
                }
                else
                {
                    Levels[_drag] = lvl;
                    for (int i = _drag + 1; i < Levels.Length; i++) if (Levels[i] < lvl) Levels[i] = lvl;   // never falls as it gets hotter
                    for (int i = _drag - 1; i >= 0; i--) if (Levels[i] > lvl) Levels[i] = Legal(i, lvl);
                }
                for (int i = 1; i < Levels.Length; i++) if (Levels[i] < Levels[i - 1]) Levels[i] = Levels[i - 1];   // floors differ per point: keep it rising
                Invalidate();
                return;
            }
            int h = HandleAt(e.Location);
            if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
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
