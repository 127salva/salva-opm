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

        public static Font Title, Small, Mono, Seg, SegBold;

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
        private int _hover = -1;


        private RectangleF Cell(int i)
        {
            float pad = Th.D(6), w = (Width - 2 * pad) / Math.Max(1, Items.Length);
            return new RectangleF(pad + i * w, pad, w, Height - 2 * pad);
        }

        private bool IsDisabled(int i) { return Disabled != null && i < Disabled.Length && Disabled[i]; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.Bg);
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
            using (SolidBrush bg = new SolidBrush(Th.Bg))
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
