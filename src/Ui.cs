// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva
// Portions adapted from Ohman (https://github.com/P4R1H/Ohman), GPL-3.0-or-later.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace SalvaOPM
{
    /// <summary>One reading's history as slots of exactly 30 s of clock time: each slot keeps the highest reading taken in it
    /// (NaN when none, e.g. the GPU asleep). Time without readings (sleep, a busy moment) leaves its slots empty, so a slot's
    /// place always says how long ago it was. Keeps what the cards show. No I/O.</summary>
    public sealed class SlotHistory
    {
        public const long SlotMs = 30000;
        public const int Keep = ChipCard.SlotCount;
        private readonly System.Collections.Generic.List<double> _dots = new System.Collections.Generic.List<double>();
        private long _slot = long.MinValue;   // the slot number (clock ms / SlotMs) of the newest dot

        /// <summary>The last reading added (NaN = none).</summary>
        public double Last = double.NaN;

        /// <summary>A reading taken at clockMs (wall clock, milliseconds). A clock that went back counts as the newest slot.</summary>
        public void Add(long clockMs, double v)
        {
            long slot = clockMs / SlotMs;
            if (_dots.Count == 0 || slot > _slot)
            {
                long missed = _dots.Count == 0 ? 0 : Math.Min(slot - _slot - 1, Keep);   // whole slots without a reading
                for (long i = 0; i < missed; i++) _dots.Add(double.NaN);
                _dots.Add(double.NaN);
                if (_dots.Count > Keep) _dots.RemoveRange(0, _dots.Count - Keep);
                _slot = slot;
            }
            int n = _dots.Count - 1;
            if (!double.IsNaN(v) && (double.IsNaN(_dots[n]) || v > _dots[n])) _dots[n] = v;
            Last = v;
        }

        /// <summary>Oldest first; the last one is still filling.</summary>
        public double[] Values() { return _dots.ToArray(); }
    }

    /// <summary>What to do about each warning the window can show: the "What to do" column of docs/troubleshooting.md
    /// (change both together). Null when there is no warning.</summary>
    public static class Hints
    {
        private static readonly string[][] Table = {
            new string[] { "Performance needs the charger", "Use the original charger, or one strong enough for this laptop." },
            new string[] { "not applied yet", "The laptop didn't answer; it tries again within a minute. If this stays, restart the PC." },
            new string[] { "Performance didn't start", "Back to Balanced. Try Performance again. If it keeps happening, close other tuning apps and send log.txt." },
            new string[] { "Couldn't check the power", "Performance keeps running. Switch mode once; send log.txt if it repeats." },
            new string[] { "Another app took over", "Back to Balanced. Close other tuning apps. If OMEN Gaming Hub came back, run the setup → Update." },
            new string[] { "GPU setting not accepted", "Switch mode once. Send log.txt if it repeats." },
            new string[] { "not tested yet", "Salva OPM was tested on BIOS F.13 only. If a mode misbehaves, choose Balanced or uninstall." },
            new string[] { "Stopped after an error", "The laptop's own settings are in charge. Exit from the tray, open Salva OPM from the Start menu, and send log.txt if it happens again." } };

        public static string For(string warning)
        {
            if (warning == null) return null;
            foreach (string[] row in Table) if (warning.IndexOf(row[0], StringComparison.Ordinal) >= 0) return row[1];
            return null;
        }
    }

    /// <summary>The whole window, one page on a 20 px edge and 16 px gaps (24 px under the header): a header (logo, name, a status pill with
    /// any warning); the three modes as cards; the CPU card over the GPU card (temperature and fan speed now, and their 10-minute history lines of 30 s slots).</summary>
    public sealed class MainForm : Form
    {
        /// <summary>The header row's centre line (96-DPI units): logo, name and status pill sit on it.</summary>
        public const int HeaderMid = 35;

        /// <summary>The window's width (96-DPI units): as narrow as the header allows (the longest warning must fit the pill
        /// beside the name); the rows and both cards follow it.</summary>
        public const int W = 490;

        /// <summary>Where the mode cards start: 24 px under the header row (it ends at HeaderMid + 15), more room than the 16 px
        /// between the other blocks.</summary>
        public const int ModesTop = HeaderMid + 15 + 24;

        /// <summary>Where the CPU card starts: 16 px under the mode cards (84 tall).</summary>
        public const int CardsTop = ModesTop + 84 + 16;

        /// <summary>The window's height (96-DPI units): header and mode cards, then the CPU card over the GPU card, 20 px under them.</summary>
        public const int H = CardsTop + 2 * ChipCard.CardHeight + 16 + 20;

        /// <summary>Where the status pill's room starts: just after the name (the pill itself sits at the right of it).</summary>
        public const int PillLeft = 186;

        /// <summary>Each chip card's width: the full row inside the 20 px edges (the CPU card over the GPU card).</summary>
        public static int CardW { get { return W - 40; } }

        private readonly App _app;
        private readonly StatusPill _status = new StatusPill();
        private readonly Seg _modes = new Seg();
        private readonly ChipCard _cpu = new ChipCard(), _gpu = new ChipCard();
        private readonly ToolTip _tip = new ToolTip();
        private string _tipText;

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
            ClientSize = new Size(Th.D(W), Th.D(H));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            // rows: header 20-50, modes 74-158 (24 px under the header), the CPU card from 174, the GPU card 16 px under it, 20 px under that
            _status.SetBounds(Th.D(PillLeft), Th.D(HeaderMid - 15), Th.D(W - PillLeft - 20), Th.D(30));
            _modes.Items = new string[] { "Eco", "Balanced", "Performance" };
            _modes.Icons = new string[] { "leaf", "scale", "bolt" };
            _modes.Picked += delegate(int i) { _app.Choose((Mode)i); };
            _modes.SetBounds(Th.D(20), Th.D(ModesTop), Th.D(W - 40), Th.D(84));
            _cpu.Title = "CPU";
            _cpu.IconName = "cpu";
            _gpu.Title = "GPU";
            _gpu.IconName = "gpu";
            _cpu.SetBounds(Th.D(20), Th.D(CardsTop), Th.D(CardW), Th.D(ChipCard.CardHeight));
            _gpu.SetBounds(Th.D(20), Th.D(CardsTop + ChipCard.CardHeight + 16), Th.D(CardW), Th.D(ChipCard.CardHeight));
            Controls.Add(_status);
            Controls.Add(_modes);
            Controls.Add(_cpu);
            Controls.Add(_gpu);

            Sync();
        }

        /// <summary>Brings every control in line with the engine (on each engine change). Never waits: while a hardware job
        /// holds the engine it only shows the clicked choice, and that job's Changed brings the next sync.</summary>
        public void Sync()
        {
            if (!System.Threading.Monitor.TryEnter(E)) { ShowPending(); return; }
            try { SyncLocked(); }
            finally { System.Threading.Monitor.Exit(E); }
        }

        /// <summary>While a job holds the engine: show the clicked mode at once, without reading the engine. The rest follows
        /// when the job is done.</summary>
        private void ShowPending()
        {
            Mode? pm = _app.PendingMode;
            if (!pm.HasValue) return;
            _modes.Index = (int)pm.Value;
            _modes.Invalidate();
        }

        private void SyncLocked()
        {
            // a clicked choice shows at once, before the hardware thread has applied it
            Mode shown = _app.PendingMode ?? E.Effective;
            string fault = _app.Booted ? E.Fault : null, hint = Hints.For(fault);
            _status.Text = StatusText(_app.Booted, fault, _app.CpuTemps.Values().Length > 0);
            bool starting = _status.Text == Starting;
            _status.Dot = starting ? Th.Sub : fault == null ? Th.Ok : E.Halted ? Th.Danger : Th.Warn;
            _status.Info = hint != null;
            _status.Cursor = hint != null ? Cursors.Help : Cursors.Default;
            string tip = fault == null ? "" : hint == null ? fault : fault + "\r\n\r\nWhat to do: " + hint;
            if (tip != _tipText) { _tipText = tip; _tip.SetToolTip(_status, tip); }   // re-setting it every 2 s would hide an open tooltip
            _modes.Items[0] = E.Controller.AutoEco ? "Eco (auto)" : "Eco";
            _modes.Index = (int)shown;
            _modes.Disabled = new bool[] { false, false, !E.Controller.PerformanceAllowed };

            Fill(_cpu, _app.CpuTemps, _app.CpuFans);
            Fill(_gpu, _app.GpuTemps, _app.GpuFans);
            _gpu.Asleep = _app.Booted && E.Sensors.GpuAsleep;
            foreach (Control c in Controls) c.Invalidate();
        }

        public const string Starting = "Starting…";

        /// <summary>The pill's line: "Starting…" until start-up is done and the first reading is in (as long as the cards'
        /// skeleton), unless a warning comes first; then the warning, or "All good".</summary>
        public static string StatusText(bool booted, string fault, bool read)
        {
            return !booted || (fault == null && !read) ? Starting : fault ?? "All good";
        }

        private static void Fill(ChipCard c, SlotHistory temps, SlotHistory fans)
        {
            c.Temp = temps.Last;
            c.Rpm = fans.Last;
            c.TempDots = temps.Values();
            c.RpmDots = fans.Values();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Th.Bg);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Th.Header(g, 20, HeaderMid);   // the logo's left tip on the 20 px content edge; the status pill on the same centre line
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Th.DarkTitleBar(Handle);
        }

        // moved to a screen with another scale: build the window again at that scale once the move ends (WM_DPICHANGED,
        // WM_ENTERSIZEMOVE, WM_EXITSIZEMOVE); the app declares per-screen scaling, so Windows no longer stretches it
        private bool _moving;
        private float _pendingScale;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0231) _moving = true;
            else if (m.Msg == 0x0232) { _moving = false; if (_pendingScale > 0) RescaleSoon(); }
            else if (m.Msg == 0x02E0)
            {
                _pendingScale = (m.WParam.ToInt32() & 0xFFFF) / 96f;
                if (!_moving) RescaleSoon();
                return;
            }
            base.WndProc(ref m);
        }

        private void RescaleSoon()
        {
            float scale = _pendingScale;
            _pendingScale = 0;
            Point at = Location;
            BeginInvoke((MethodInvoker)delegate { _app.Rescale(scale, at); });
        }

        /// <summary>A click on the window background hides the keyboard focus ring too.</summary>
        protected override void OnMouseDown(MouseEventArgs e) { Ctl.HideFocusCues(this); base.OnMouseDown(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
