// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SalvaOPM
{
    /// <summary>Which laptop the firmware writes are validated for. Anything else gets monitoring only.</summary>
    public static class Identity
    {
        public const string Board = "8E35", ModelPart = "16-ap0", ValidatedBios = "F.13";

        public static bool WritesAllowed(string model, string board)
        {
            return board == Board && model != null && model.IndexOf(ModelPart, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string BiosWarning(string bios)
        {
            return bios == ValidatedBios ? null : "BIOS " + (string.IsNullOrEmpty(bios) ? "unknown" : bios) + " is not the validated " + ValidatedBios + ": repeat the hardware tests";
        }
    }

    /// <summary>
    /// The running app: tray icon, the 5 s engine timer, power events, the OMEN key and the window.
    /// The window is created when shown and disposed when closed, so the idle app holds no UI. --demo runs all of it
    /// against the simulator (no hardware, no Windows setting, OGH left alone).
    /// </summary>
    public sealed class App : ApplicationContext
    {
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

        public readonly Engine Engine;
        public readonly Icon AppIcon;
        private readonly IPowerSource _power;
        private readonly bool _demo;
        private readonly Logger _log;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _retry = new System.Windows.Forms.Timer();
        private readonly PowerWatcher _watcher;
        private readonly FlapGuard _flap = new FlapGuard();
        private readonly SynchronizationContext _ui;
        private readonly RegisteredWaitHandle _showWait;
        private readonly string _flag;
        private readonly Icon[] _modeIcons;
        private ManagementEventWatcher _key;
        private MainForm _form;
        private int _bootDelay = 5000, _ticks;
        private long _lastChoose = -1000;
        private DateTime _lastTickWall = DateTime.MinValue;
        private bool _booted, _exiting, _idDone, _haltShown;

        public App(Engine engine, IPowerSource power, bool demo, Logger log, EventWaitHandle showSignal)
        {
            Engine = engine;
            _power = power;
            _demo = demo;
            _log = log;
            _ui = new WindowsFormsSynchronizationContext();
            AppIcon = MakeIcon();
            // the tray diamond shows the mode: green Eco, grey Balanced, red Performance
            _modeIcons = new Icon[] {
                MakeIcon(Color.FromArgb(0x6F, 0xD6, 0x8E), Color.FromArgb(0x2E, 0x9B, 0x55)),
                MakeIcon(Color.FromArgb(0xD0, 0xCB, 0xC7), Color.FromArgb(0x7A, 0x75, 0x71)),
                AppIcon };
            if (!_demo) Engine.KillOgh = delegate { Ogh.KillBackground(_log.Write); };
            Engine.Changed += delegate { UpdateTray(); if (_form != null && !_form.IsDisposed) _form.Sync(); };

            _tray.Icon = AppIcon;
            _tray.Text = "Salva OPM";
            _tray.Visible = true;
            _tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleWindow(); };
            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("Open Salva OPM", delegate { ShowWindow(); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Eco", delegate { Choose(Mode.Eco); });
            menu.MenuItems.Add("Balanced", delegate { Choose(Mode.Balanced); });
            menu.MenuItems.Add("Performance", delegate { Choose(Mode.Performance); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Exit", delegate { Exit(); });
            _tray.ContextMenu = menu;

            _watcher = new PowerWatcher();
            _watcher.StatusChanged += delegate { OnPower(false); };
            _watcher.Resumed += delegate { OnPower(true); };
            SystemEvents.SessionEnding += OnSessionEnding;

            _tick.Interval = (int)Engine.TickMs;
            _tick.Tick += delegate
            {
                // Modern Standby sends no resume event to the app: a long gap between ticks means the PC was asleep
                DateTime wall = DateTime.UtcNow;
                if (_booted && _lastTickWall != DateTime.MinValue && (wall - _lastTickWall).TotalSeconds > 30)
                {
                    _log.Write("woke from standby (" + (int)(wall - _lastTickWall).TotalSeconds + " s without a tick)");
                    OnPower(true);
                }
                _lastTickWall = wall;
                if (_booted) Engine.Tick(Now());
                // WMI leaves a few handles per BIOS call until .NET cleans up; with so little allocation that can take long
                if (++_ticks % 60 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            };
            _retry.Tick += delegate { _retry.Stop(); if (!_booted) Boot(); else OnPower(false); };

            if (showSignal != null)
                _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, delegate { _ui.Post(delegate { ShowWindow(); }, null); }, null, -1, false);

            if (!_demo)
            {
                // a flag file that only a clean exit removes: finding it means the last session was killed or crashed
                _flag = Path.Combine(Settings.DefaultDirectory(), "running.flag");
                try { if (File.Exists(_flag)) _log.Write("previous session ended unexpectedly"); else File.WriteAllText(_flag, ""); } catch (Exception) { }
                Ogh.KillBackground(_log.Write);
                StartKeyWatcher();
            }
            Boot();
            _tick.Start();
        }

        public long Now() { return _clock.ElapsedMilliseconds; }

        /// <summary>False when the identity could not be read yet (WMI not ready at early logon).</summary>
        private bool CheckIdentity()
        {
            string model = Preflight.Query(@"root\cimv2", "SELECT Model FROM Win32_ComputerSystem", "Model");
            string board = Preflight.Query(@"root\cimv2", "SELECT Product FROM Win32_BaseBoard", "Product");
            string bios = Preflight.Query(@"root\cimv2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion");
            if (model == null || board == null) return false;
            model = model.Trim(); board = board.Trim(); bios = bios == null ? "" : bios.Trim();
            _log.Write("identity: " + model + ", board " + board + ", BIOS " + bios);
            if (!Identity.WritesAllowed(model, board))
            {
                Engine.MonitorOnly = true;
                Engine.MonitorReason = "not the OMEN 16-ap0 with board 8E35 (" + model + ", " + board + ")";
            }
            Engine.BiosWarning = Identity.BiosWarning(bios);
            return true;
        }

        private void Boot()
        {
            if (!_demo && !_idDone) _idDone = CheckIdentity();
            bool ok = _demo || _idDone;
            if (ok)
            {
                PowerSnapshot p = _power.Read();
                Engine.SetPowerInputs(p.OnAc, p.BatteryDrain, p.BatteryPercent);
                ok = Engine.Boot(p.PowerOk, Now());
            }
            UpdateTray();
            if (ok) { _booted = true; return; }
            if (_bootDelay > 80000)   // gave up retrying
            {
                if (!_demo && !_idDone) { Engine.MonitorOnly = true; Engine.MonitorReason = "the laptop's identity could not be read"; }
                _booted = true;
                return;
            }
            _retry.Interval = _bootDelay;
            _bootDelay *= 2;
            _retry.Start();
        }

        private void OnPower(bool resumed)
        {
            if (!_booted) return;
            long now = Now();
            if (!resumed && !_flap.Allow(now)) { _retry.Interval = 30000; _retry.Start(); return; }
            PowerSnapshot p = _power.Read();
            Engine.SetPowerInputs(p.OnAc, p.BatteryDrain, p.BatteryPercent);
            if (resumed) Engine.Resume(p.PowerOk, now); else Engine.PowerChanged(p.PowerOk, now);
            UpdateTray();
        }

        public void Choose(Mode m)
        {
            if (!_booted)   // the identity is not known yet: nothing may be written
            {
                _tray.ShowBalloonTip(3000, "Salva OPM", "Starting up; try again in a moment.", ToolTipIcon.Info);
                return;
            }
            long now = Now();
            if (now - _lastChoose < 1000) return;   // no rapid profile toggling
            _lastChoose = now;
            if (!Engine.Select(m, now) && Engine.LastError != null)
                _tray.ShowBalloonTip(4000, "Salva OPM", Engine.LastError, ToolTipIcon.Warning);
            UpdateTray();
        }

        private void UpdateTray()
        {
            if (Engine.Halted && !_haltShown)
            {
                _haltShown = true;
                _tray.ShowBalloonTip(8000, "Salva OPM stopped", Engine.LastError, ToolTipIcon.Error);
            }
            _tray.Icon = Engine.Halted ? SystemIcons.Error : _modeIcons[(int)Engine.Effective];
            string t = "Salva OPM · " + Engine.Name(Engine.Effective) + (Engine.Controller.AutoEco ? " (auto)" : "");
            t += "\n" + (Engine.Fault ?? Engine.InfoLine(Now()).Replace("   ·   ", " · ").Replace("  ", " "));
            _tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;   // Windows' limit for a tray tooltip
        }

        // ---------------- window ----------------

        public void ShowWindow()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new MainForm(this);
                _form.FormClosed += delegate { _form.Dispose(); _form = null; Trim(); };
            }
            _form.Show();
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
        }

        public void ToggleWindow()
        {
            if (_form != null && !_form.IsDisposed && _form.Visible && Form.ActiveForm == _form) _form.Close();
            else ShowWindow();
        }

        /// <summary>Gives back the pages the window used; the idle app is a tray icon and two timers.</summary>
        private static void Trim()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1)); } catch (Exception) { }
        }

        // ---------------- OMEN key (Ohman: hpqBEvnt id 29, data 8613) ----------------

        private void StartKeyWatcher()
        {
            try
            {
                _key = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new WqlEventQuery("SELECT * FROM hpqBEvnt"));
                _key.EventArrived += delegate(object s, EventArrivedEventArgs e)
                {
                    uint id = 0, data = 0;
                    try { id = Convert.ToUInt32(e.NewEvent["EventID"]); data = Convert.ToUInt32(e.NewEvent["EventData"]); } catch (Exception) { return; }
                    if (id == 29 && data == 8613) _ui.Post(delegate { ToggleWindow(); }, null);
                };
                _key.Start();
            }
            catch (Exception e) { _log.Write("OMEN key watcher: " + e.Message); _key = null; }
        }

        // ---------------- leaving ----------------

        private void CleanExitFlag()
        {
            try { if (_flag != null && File.Exists(_flag)) File.Delete(_flag); } catch (Exception) { }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            if (_exiting) return;
            _exiting = true;
            _tick.Stop();   // after parking, nothing may take the fans back
            try { Engine.Exit(true); } catch (Exception) { }
            CleanExitFlag();
        }

        public void Exit()
        {
            if (!_exiting) { _exiting = true; _tick.Stop(); try { Engine.Exit(false); } catch (Exception) { } }
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            _tick.Stop();
            _retry.Stop();
            if (_key != null) { try { _key.Stop(); _key.Dispose(); } catch (Exception) { } }
            if (_showWait != null) _showWait.Unregister(null);
            if (_form != null && !_form.IsDisposed) _form.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _watcher.Dispose();
            Engine.Sensors.Dispose();
            CleanExitFlag();
            base.ExitThreadCore();
        }

        /// <summary>The logo: a red diamond, drawn at run time for the window and the tray (res\app.ico is only the exe's icon).</summary>
        public static Icon MakeIcon() { return MakeIcon(Color.FromArgb(0xF6, 0x7A, 0x7D), Color.FromArgb(0xB8, 0x2A, 0x2E)); }

        public static Icon MakeIcon(Color light, Color dark)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    PointF[] d = { new PointF(16, 2), new PointF(30, 16), new PointF(16, 30), new PointF(2, 16) };
                    using (LinearGradientBrush b = new LinearGradientBrush(new PointF(2, 2), new PointF(30, 30), light, dark))
                        g.FillPolygon(b, d);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
