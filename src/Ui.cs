// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva
// Portions adapted from Ohman (https://github.com/P4R1H/Ohman), GPL-3.0-or-later.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SalvaOPM
{
    /// <summary>The last N readings of one temperature (NaN = a gap, e.g. the GPU asleep), for the window's graphs. No I/O.</summary>
    public sealed class TempHistory
    {
        private readonly double[] _v;
        private int _next, _count;

        public TempHistory(int size) { _v = new double[size]; }
        public int Capacity { get { return _v.Length; } }

        public void Add(double t)
        {
            _v[_next] = t;
            _next = (_next + 1) % _v.Length;
            if (_count < _v.Length) _count++;
        }

        /// <summary>Oldest first.</summary>
        public double[] Values()
        {
            double[] r = new double[_count];
            for (int i = 0; i < _count; i++) r[i] = _v[(_next - _count + i + _v.Length) % _v.Length];
            return r;
        }
    }

    /// <summary>The whole window, one page: a header (logo, name, version, a status pill with any warning); the three
    /// modes as cards; a CPU and a GPU panel (temperature, fan speed, the last 5 minutes); the fan switch (Auto alone in Eco
    /// and Balanced, Auto | Custom in Performance); and the curve.</summary>
    public sealed class MainForm : Form
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private static readonly string[] AutoOnly = { "Auto" }, AutoCustom = { "Auto", "Custom" };

        private readonly App _app;
        private readonly StatusPill _status = new StatusPill();
        private readonly Seg _modes = new Seg();
        private readonly ChipPanel _cpu = new ChipPanel(), _gpu = new ChipPanel();
        private readonly Seg _fan = new Seg();
        private readonly CurveView _curve = new CurveView();
        private readonly ToolTip _tip = new ToolTip();
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
            ClientSize = new Size(Th.D(760), Th.D(676));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            _status.SetBounds(Th.D(300), Th.D(22), Th.D(440), Th.D(30));
            _modes.Items = new string[] { "Eco", "Balanced", "Performance" };
            _modes.Icons = new string[] { "leaf", "scale", "bolt" };
            _modes.Picked += delegate(int i) { _app.Choose((Mode)i); };
            _modes.SetBounds(Th.D(20), Th.D(76), Th.D(720), Th.D(84));
            _cpu.Title = "CPU";
            _gpu.Title = "GPU";
            _cpu.HistoryLabel = _gpu.HistoryLabel = (app.CpuHistory.Capacity * Engine.TickMs / 60000) + " min";
            _cpu.HistoryCapacity = app.CpuHistory.Capacity;
            _gpu.HistoryCapacity = app.GpuHistory.Capacity;
            _cpu.SetBounds(Th.D(20), Th.D(176), Th.D(352), Th.D(200));
            _gpu.SetBounds(Th.D(388), Th.D(176), Th.D(352), Th.D(200));
            _fan.Picked += delegate(int i) { E.SetFanMode(i == 1, _app.Now()); };
            _fan.SetBounds(Th.D(20), Th.D(392), Th.D(720), Th.D(44));
            _curve.Committed += delegate(int[] levels) { E.SetCurve(levels, _app.Now()); };
            _curve.SetBounds(Th.D(20), Th.D(452), Th.D(720), Th.D(204));
            Controls.Add(_status);
            Controls.Add(_modes);
            Controls.Add(_cpu);
            Controls.Add(_gpu);
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
            string fault = E.Fault;
            _status.Text = fault ?? "All good";
            _status.Dot = fault == null ? Th.Ok : E.Halted || E.Guard.Active ? Th.Accent : Th.Warn;
            _tip.SetToolTip(_status, fault ?? "");
            _modes.Items[0] = E.Controller.AutoEco ? "Eco (auto)" : "Eco";
            _modes.Index = (int)E.Effective;
            _modes.Disabled = new bool[] { mon, mon, mon || !E.Controller.PerformanceAllowed };

            long now = _app.Now();
            int[] f = E.FansFresh(now) ? E.Fans : null;
            _cpu.Temp = E.Sensors.CpuTemp;
            _cpu.Rpm = f == null || f[0] < 0 ? -1 : f[0] * 100;
            _cpu.History = _app.CpuHistory.Values();
            _gpu.Asleep = E.Sensors.GpuAsleep;
            _gpu.Temp = E.Sensors.GpuTemp;
            _gpu.Rpm = f == null || f[1] < 0 ? -1 : f[1] * 100;
            _gpu.History = _app.GpuHistory.Values();

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
            foreach (Control c in Controls) c.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Th.Bg);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Th.Diamond(g, Th.D(38), Th.D(37), Th.D(16));
            Th.Text(g, "Salva OPM", Th.Title, Th.TextHi, Th.D(64), Th.D(22));
            Th.Text(g, "v" + Setup.Version, Th.Mono, Th.Sub, Th.D(64) + Th.Measure("Salva OPM", Th.Title).Width + Th.D(10), Th.D(29));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            try { if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); } catch (Exception) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _refresh.Dispose(); _tip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
