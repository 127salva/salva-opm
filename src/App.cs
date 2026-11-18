// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.Collections.Generic;
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
    /// The one thread that does all hardware work (every WMI call takes 30–100 ms; on the window's thread a mode change froze
    /// the window for about a second). Jobs run one at a time, in order, inside lock (gate); a job posted with the key of a
    /// job still waiting replaces it, so fast clicks collapse into the last choice and timer ticks never pile up.
    /// </summary>
    public sealed class HardwareThread : IDisposable
    {
        private readonly object _gate, _q = new object();
        private readonly Action<Exception> _onError;
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, Action> _jobs = new Dictionary<string, Action>();
        private readonly Thread _thread;
        private bool _stop;
        private int _waitKeys;

        /// <summary>onError runs on this thread, inside the gate (the fail safe).</summary>
        public HardwareThread(object gate, Action<Exception> onError)
        {
            _gate = gate;
            _onError = onError;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "SalvaOPM hardware";
            _thread.Start();
        }

        public void Post(string key, Action job)
        {
            lock (_q)
            {
                if (_stop) return;
                if (!_jobs.ContainsKey(key)) _order.Add(key);
                _jobs[key] = job;
                Monitor.Pulse(_q);
            }
        }

        /// <summary>Runs job after the waiting ones and blocks until it is done or timeoutMs passed (exit, log-off).</summary>
        public bool RunAndWait(Action job, int timeoutMs)
        {
            using (ManualResetEvent done = new ManualResetEvent(false))
            {
                Post("wait" + Interlocked.Increment(ref _waitKeys), delegate { try { job(); } finally { done.Set(); } });
                return done.WaitOne(timeoutMs);
            }
        }

        private void Loop()
        {
            while (true)
            {
                Action job;
                lock (_q)
                {
                    while (_order.Count == 0 && !_stop) Monitor.Wait(_q);
                    if (_order.Count == 0) return;
                    string key = _order[0];
                    _order.RemoveAt(0);
                    job = _jobs[key];
                    _jobs.Remove(key);
                }
                lock (_gate)
                {
                    try { job(); }
                    catch (Exception e) { try { if (_onError != null) _onError(e); } catch (Exception) { } }
                }
            }
        }

        public void Dispose()
        {
            lock (_q) { _stop = true; Monitor.Pulse(_q); }
            _thread.Join(3000);
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
        /// <summary>The window's graphs: 5 minutes of 5 s readings, kept while the window is closed.</summary>
        public readonly TempHistory CpuHistory = new TempHistory(60), GpuHistory = new TempHistory(60);
        /// <summary>All hardware work runs here; the window thread only posts jobs and reads what it can without waiting.</summary>
        public readonly HardwareThread Hw;
        /// <summary>A choice clicked but not applied yet: the window shows it at once (null: none waiting).</summary>
        public Mode? PendingMode;
        public FanSource? PendingFan;
        public Mode PendingFanMode;   // the mode PendingFan was clicked for
        private readonly IPowerSource _power;
        private readonly bool _demo;
        private readonly Logger _log;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
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
        private DateTime _lastTickWall = DateTime.MinValue;
        private volatile bool _booted;
        private bool _exiting, _idDone, _haltShown;

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
            // the engine changes on the hardware thread; the window and the tray follow on their own thread
            Engine.Changed += delegate { OnUi(Refresh); };
            Hw = new HardwareThread(Engine, delegate(Exception e)
            {
                _log.Write("unhandled on the hardware thread: " + e);
                Engine.FailSafe(e.GetType().Name + ": " + e.Message);
                OnUi(delegate { PendingMode = null; PendingFan = null; Refresh(); });   // the failed job never cleared its clicked choice
            });

            _tray.Icon = AppIcon;
            _tray.Text = "Salva OPM";
            _tray.Visible = true;
            _tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleWindow(); };
            // a small dark menu in the window's colours (the old Win32 menu was light grey with an empty icon column)
            _menu.Renderer = new TrayMenuRenderer();
            _menu.ShowImageMargin = false;
            _menu.ShowCheckMargin = false;
            _menu.Font = Th.Small;
            _menu.Padding = new Padding(Th.D(4));
            _menu.Items.Add(new ToolStripMenuItem("Open Salva OPM", null, delegate { ShowWindow(); }));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { Exit(); }));
            // a drop-down ignores item padding: size the items here, the renderer places the text 12 px in
            int textW = 0;
            foreach (ToolStripItem item in _menu.Items) if (item is ToolStripMenuItem) textW = Math.Max(textW, Th.Measure(item.Text, Th.Small).Width);
            foreach (ToolStripItem item in _menu.Items) if (item is ToolStripMenuItem) { item.AutoSize = false; item.Size = new Size(textW + Th.D(40), Th.D(28)); }
            _menu.Opening += delegate { Th.RoundCorners(_menu.Handle); };
            _tray.ContextMenuStrip = _menu;

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
                if (_booted)
                    Hw.Post("tick", delegate
                    {
                        Engine.Tick(Now());
                        double cpu = Engine.Sensors.CpuTemp, gpu = Engine.Sensors.GpuAsleep ? double.NaN : Engine.Sensors.GpuTemp;
                        OnUi(delegate { CpuHistory.Add(cpu); GpuHistory.Add(gpu); });   // only the window thread touches the graphs
                    });
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

        private void OnUi(Action a) { _ui.Post(delegate { a(); }, null); }

        /// <summary>Tray and window follow the engine (on the window thread).</summary>
        private void Refresh()
        {
            if (_exiting) return;   // a late update from the exit job must not touch the disposed tray icon
            UpdateTray();
            if (_form != null && !_form.IsDisposed) _form.Sync();
        }
        /// <summary>False until start-up is done (the identity read and the first apply); the window shows "Starting…".</summary>
        public bool Booted { get { return _booted; } }

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

        /// <summary>Start-up on the hardware thread; a failure is retried from the window thread with a growing delay.</summary>
        private void Boot()
        {
            bool last = _bootDelay > 80000;   // after this try, give up retrying
            Hw.Post("boot", delegate
            {
                if (!_demo && !_idDone) _idDone = CheckIdentity();
                bool ok = _demo || _idDone;
                if (ok)
                {
                    PowerSnapshot p = _power.Read();
                    Engine.SetPowerInputs(p.OnAc, p.BatteryDrain, p.BatteryPercent);
                    ok = Engine.Boot(p.PowerOk, Now());
                }
                if (!ok && last && !_demo && !_idDone) { Engine.MonitorOnly = true; Engine.MonitorReason = "the laptop's identity could not be read"; }
                if (ok || last) _booted = true;
                OnUi(delegate
                {
                    Refresh();
                    if (_booted) return;
                    _retry.Interval = _bootDelay;
                    _bootDelay *= 2;
                    _retry.Start();
                });
            });
        }

        private void OnPower(bool resumed)
        {
            if (!_booted) return;
            long now = Now();
            if (!resumed && !_flap.Allow(now)) { _retry.Interval = 30000; _retry.Start(); return; }
            Hw.Post(resumed ? "resume" : "power", delegate
            {
                PowerSnapshot p = _power.Read();   // the charger is read through WMI too
                Engine.SetPowerInputs(p.OnAc, p.BatteryDrain, p.BatteryPercent);
                if (resumed) Engine.Resume(p.PowerOk, Now()); else Engine.PowerChanged(p.PowerOk, Now());
                OnUi(Refresh);
            });
        }

        /// <summary>False (with a balloon) until start-up is done: the identity is not known yet, so nothing may be written.</summary>
        private bool ReadyForChoices()
        {
            if (_booted) return true;
            _tray.ShowBalloonTip(3000, "Salva OPM", "Starting up; try again in a moment.", ToolTipIcon.Info);
            return false;
        }

        public void Choose(Mode m)
        {
            if (!ReadyForChoices()) return;
            // shown at once; applied on the hardware thread, where fast clicks collapse into the last one
            PendingMode = m;
            Refresh();
            Hw.Post("mode", delegate
            {
                string error = Engine.Select(m, Now()) ? null : Engine.LastError;
                OnUi(delegate
                {
                    if (PendingMode == m) PendingMode = null;
                    if (error != null) _tray.ShowBalloonTip(4000, "Salva OPM", error, ToolTipIcon.Warning);
                    Refresh();
                });
            });
        }

        /// <summary>The fan choice for the mode on screen (m), shown at once and applied on the hardware thread for that
        /// mode, even when a mode change is still waiting in front of it.</summary>
        public void ChooseFan(Mode m, FanSource f)
        {
            if (!ReadyForChoices()) return;
            PendingFan = f;
            PendingFanMode = m;
            Refresh();
            Hw.Post("fan", delegate
            {
                Engine.SetFanMode(m, f, Now());
                OnUi(delegate { if (PendingFan == f && PendingFanMode == m) PendingFan = null; Refresh(); });
            });
        }

        public void SetCurve(int[] levels)
        {
            if (!ReadyForChoices()) return;
            Hw.Post("curve", delegate { Engine.SetCurve(levels, Now()); });
        }

        /// <summary>The open window reads the fan speeds every 2 s.</summary>
        public void ReadFans() { Hw.Post("fans", delegate { Engine.ReadFans(Now()); OnUi(Refresh); }); }

        private void UpdateTray()
        {
            if (_exiting || !Monitor.TryEnter(Engine)) return;   // a job is running: its Changed brings the next update
            try { UpdateTrayLocked(); }
            finally { Monitor.Exit(Engine); }
        }

        private void UpdateTrayLocked()
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
            _retry.Stop();
            Hw.RunAndWait(delegate { try { Engine.Exit(true); } catch (Exception) { } }, 10000);
            CleanExitFlag();
        }

        public void Exit()
        {
            if (!_exiting)
            {
                _exiting = true;
                _tick.Stop();
                _retry.Stop();
                Hw.RunAndWait(delegate { try { Engine.Exit(false); } catch (Exception) { } }, 10000);
            }
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
            _menu.Dispose();
            _watcher.Dispose();
            Hw.Dispose();   // after the exit job; nothing touches the hardware from here on
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
