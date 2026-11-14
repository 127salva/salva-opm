// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva
// Portions adapted from Ohman (https://github.com/P4R1H/Ohman), GPL-3.0-or-later.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SalvaOPM
{
    /// <summary>The whole window, one page: the three modes; the fan switch under them (Auto alone in Eco and Balanced,
    /// Auto | Custom in Performance); the curve; and one small line of temperatures and fan speeds. A warning line appears
    /// above the curve only when something is wrong.</summary>
    public sealed class MainForm : Form
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private static readonly string[] AutoOnly = { "Auto" }, AutoCustom = { "Auto", "Custom" };

        private readonly App _app;
        private readonly Seg _modes = new Seg();
        private readonly Seg _fan = new Seg();
        private readonly CurveView _curve = new CurveView();
        private readonly Timer _refresh = new Timer();

        private Engine E { get { return _app.Engine; } }

        public MainForm(App app)
        {
            _app = app;
            Text = "Salva OPM";
            Icon = app.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Th.Bg;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Th.D(524), Th.D(430));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            _modes.Items = new string[] { "Eco", "Balanced", "Performance" };
            _modes.Picked += delegate(int i) { _app.Choose((Mode)i); };
            _modes.SetBounds(Th.D(20), Th.D(18), Th.D(484), Th.D(58));
            _fan.Picked += delegate(int i) { E.SetFanMode(i == 1, _app.Now()); };
            _fan.SetBounds(Th.D(20), Th.D(86), Th.D(484), Th.D(48));   // same left edge and width as the mode switch
            _curve.Committed += delegate(int[] levels) { E.SetCurve(levels, _app.Now()); };
            _curve.SetBounds(Th.D(20), Th.D(162), Th.D(484), Th.D(226));
            Controls.Add(_modes);
            Controls.Add(_fan);
            Controls.Add(_curve);

            Sync();
            _refresh.Interval = 2000;
            _refresh.Tick += delegate { Sync(); };
            _refresh.Start();
        }

        /// <summary>Brings every control in line with the engine. Cheap; called on engine changes and every 2 s.</summary>
        public void Sync()
        {
            bool mon = E.MonitorOnly, perf = E.Effective == Mode.Performance;
            _modes.Items[0] = E.Controller.AutoEco ? "Eco (auto)" : "Eco";
            _modes.Index = (int)E.Effective;
            _modes.Disabled = new bool[] { mon, mon, mon || !E.Controller.PerformanceAllowed };
            _fan.Items = perf ? AutoCustom : AutoOnly;
            _fan.Index = E.CustomFans ? 1 : 0;
            _fan.Disabled = new bool[] { mon, mon };
            if (!_curve.Dragging)
            {
                if (E.CustomFans)
                {
                    _curve.Temps = FanCurve.CustomTemps;
                    _curve.Levels = (int[])E.Settings.Curve.Clone();
                    _curve.Floors = FanCurve.CustomFloor;
                    _curve.ReadOnly = mon;
                }
                else
                {
                    FanCurve a = E.AutoCurve;
                    _curve.Temps = a.CpuTemps;
                    _curve.Levels = Array.ConvertAll(a.CpuLevels, delegate(int l) { return a.Clamp(l); });
                    _curve.Floors = null;
                    _curve.ReadOnly = true;
                }
            }
            _modes.Invalidate();
            _fan.Invalidate();
            _curve.Invalidate();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Th.Bg);
            string fault = E.Fault;
            if (fault != null)
            {
                Rectangle box = new Rectangle(Th.D(22), Th.D(138), Th.D(480), Th.D(22));
                TextRenderer.DrawText(g, fault, Th.Small, box, Th.Warn,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }
            Th.Text(g, E.InfoLine(_app.Now()), Th.Small, Th.Sub, Th.D(22), Th.D(398));
            Th.TextRight(g, "v" + Setup.Version, Th.Small, Th.Sub, Th.D(502), Th.D(398));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            try { if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); } catch (Exception) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _refresh.Dispose();
            base.Dispose(disposing);
        }
    }
}
