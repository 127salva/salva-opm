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
    /// <summary>Which laptop the firmware writes are validated for. On anything else the setup refuses to install and the
    /// app refuses to run.</summary>
    public static class Identity
    {
        public const string Board = "8E35", ModelPart = "16-ap0", ValidatedBios = "F.13";

        public static bool WritesAllowed(string model, string board)
        {
            return board == Board && model != null && model.IndexOf(ModelPart, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>This PC's model and board as Windows reports them (trimmed; null when unread). False when either cannot be
        /// read (WMI not ready at early logon). The app, --check and uninstall all ask this way.</summary>
        public static bool Read(out string model, out string board)
        {
            model = Preflight.Query(@"root\cimv2", "SELECT Model FROM Win32_ComputerSystem", "Model");
            board = Preflight.Query(@"root\cimv2", "SELECT Product FROM Win32_BaseBoard", "Product");
            if (model != null) model = model.Trim();
            if (board != null) board = board.Trim();
            return model != null && board != null;
        }

        public static string BiosWarning(string bios)
        {
            return bios == ValidatedBios ? null : string.IsNullOrEmpty(bios) ? "Unknown BIOS, not tested yet" : "New BIOS, not tested yet";   // the version is in the log ("identity: …, BIOS F.xx")
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
                // after a timeout the event is gone when the job ends: that is not an error (it would start the fail safe)
                Post("wait" + Interlocked.Increment(ref _waitKeys), delegate { try { job(); } finally { try { done.Set(); } catch (ObjectDisposedException) { } } });
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
        /// <summary>The cards' history lines: 10 minutes in 30 s slots (temperatures in °C, fan speeds in rpm), kept while the
        /// window is closed.</summary>
        public readonly SlotHistory CpuTemps = new SlotHistory(), GpuTemps = new SlotHistory(), CpuFans = new SlotHistory(), GpuFans = new SlotHistory();
        /// <summary>All hardware work runs here; the window thread only posts jobs and reads what it can without waiting.</summary>
        public readonly HardwareThread Hw;
        /// <summary>A choice clicked but not applied yet: the window shows it at once (null: none waiting).</summary>
        public Mode? PendingMode;
        private readonly IPowerSource _power;
        private readonly bool _demo;
        private readonly Logger _log;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private int _menuClosedAt = Environment.TickCount - 60000;
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _retry = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _goOn = new System.Windows.Forms.Timer { Interval = 60000 };
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
        private bool _exiting, _idDone, _haltShown, _windowErrorShown;
        private string _refused;   // set when this is not the validated laptop: the app shows why and exits

        public App(Engine engine, IPowerSource power, bool demo, Logger log, EventWaitHandle showSignal)
        {
            Engine = engine;
            _power = power;
            _demo = demo;
            _log = log;
            _ui = new WindowsFormsSynchronizationContext();
            AppIcon = MakeIcon();
            _modeIcons = new Icon[ModeColours.Length];
            for (int i = 0; i < ModeColours.Length; i++) _modeIcons[i] = MakeIcon(ModeColours[i][0], ModeColours[i][1]);
            if (!_demo) Engine.KillOgh = delegate { Ogh.KillBackground(_log.Write); };
            // the engine changes on the hardware thread; the window and the tray follow on their own thread
            Engine.Changed += delegate { OnUi(Refresh); };
            Hw = new HardwareThread(Engine, delegate(Exception e)
            {
                _log.Write("unhandled on the hardware thread: " + e);
                Engine.FailSafe(e.GetType().Name + ": " + e.Message);
                OnUi(delegate { PendingMode = null; Refresh(); });   // the failed job never cleared its clicked choice
            });

            _tray.Icon = AppIcon;
            _tray.Text = "Salva OPM";
            _tray.Visible = true;
            _tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleWindow(); };
            // a small dark menu in the window's colours (the old Win32 menu was light grey with an empty icon column)
            _menu.Renderer = new TrayMenuRenderer();
            _menu.ShowImageMargin = false;
            _menu.ShowCheckMargin = false;
            _menu.Items.Add(new ToolStripMenuItem("Open Salva OPM", null, delegate { ShowWindow(); }));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { Exit(); }));
            // a right-click on the icon while the menu is open closes it (the click itself closes it; do not reopen it at once)
            _menu.Closed += delegate { _menuClosedAt = Environment.TickCount; };
            _menu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (unchecked(Environment.TickCount - _menuClosedAt) < 400) { e.Cancel = true; return; }
                if (_form == null || _form.IsDisposed) Th.Init(Th.ScaleAt(Cursor.Position));   // the open window keeps its own screen's scale
                TrayMenuRenderer.Fit(_menu);
                Th.RoundCorners(_menu.Handle, Th.Border);
            };
            _tray.ContextMenuStrip = _menu;

            _watcher = new PowerWatcher();
            _watcher.StatusChanged += delegate { OnPower(false); };
            _watcher.Resumed += delegate { OnPower(true); };
            SystemEvents.SessionEnding += OnSessionEnding;
            _goOn.Tick += delegate { _goOn.Stop(); GoOn(); };

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
                        long now = Now();
                        Engine.Tick(now);
                        double cpu = Engine.Sensors.CpuTemp, gpu = Engine.Sensors.GpuAsleep ? double.NaN : Engine.Sensors.GpuTemp;
                        int[] f = Engine.FansFresh(now) ? Engine.Fans : null;
                        double cf = f == null ? double.NaN : f[0] * 100, gf = f == null ? double.NaN : f[1] * 100;
                        long at = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;   // wall clock: it runs on through sleep
                        OnUi(delegate { CpuTemps.Add(at, cpu); GpuTemps.Add(at, gpu); CpuFans.Add(at, cf); GpuFans.Add(at, gf); });   // only the window thread touches the histories
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

        /// <summary>False when the identity could not be read yet (WMI not ready at early logon). Another laptop sets _refused.</summary>
        private bool CheckIdentity()
        {
            string model, board;
            if (!Identity.Read(out model, out board)) return false;
            string bios = Preflight.Query(@"root\cimv2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion");
            bios = bios == null ? "" : bios.Trim();
            _log.Write("identity: " + model + ", board " + board + ", BIOS " + bios);
            if (!Identity.WritesAllowed(model, board)) _refused = "This is not the HP OMEN 16-ap0 with board 8E35 (" + model + ", " + board + ").";
            Engine.BiosWarning = Identity.BiosWarning(bios);
            return true;
        }

        /// <summary>Start-up on the hardware thread; a failure is retried from the window thread with a growing delay. The
        /// identity comes first: until it is known to be this laptop, no firmware call is made.</summary>
        private void Boot()
        {
            bool last = _bootDelay > 80000;   // after this try, give up retrying
            Hw.Post("boot", delegate
            {
                if (!_demo && !_idDone) _idDone = CheckIdentity();
                if (!_demo && _refused == null && !_idDone && last) _refused = Unreadable;
                if (_refused != null) { OnUi(Refuse); return; }
                bool ok = MayWrite(_refused, _demo, _idDone);
                if (ok)
                {
                    if (!_demo) Ogh.KillBackground(_log.Write);   // only on this laptop, like every change the app makes
                    PowerSnapshot p = _power.Read();
                    Engine.SetPowerInputs(p.OnAc, p.BatteryDrain, p.BatteryPercent);
                    ok = Engine.Boot(p.PowerOk, Now());
                }
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

        /// <summary>Not the validated laptop: say why and end, without one firmware call (the setup refuses to install there).</summary>
        private void Refuse()
        {
            if (_exiting) return;
            _exiting = true;
            _tick.Stop();
            _retry.Stop();
            _log.Write("refused to run: " + _refused);
            MessageBox.Show(RefusalText(_refused), "Salva OPM", MessageBoxButtons.OK, MessageBoxIcon.Stop);
            ExitThread();
        }

        public const string Unreadable = "The laptop's identity could not be read.";

        /// <summary>The refusal box's text. "That laptop" only when the first line named it (another laptop).</summary>
        public static string RefusalText(string why)
        {
            return why + "\r\n\r\nSalva OPM only runs on " + (why == Unreadable ? "the HP OMEN 16-ap0 with board 8E35" : "that laptop")
                + " and will close now.";
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
            _tray.Icon = Engine.Halted ? SystemIcons.Error : _modeIcons[(int)Engine.Effective];   // the tooltip stays "Salva OPM"
        }

        // ---------------- window ----------------

        public void ShowWindow()
        {
            if (_form == null || _form.IsDisposed) OpenForm(Th.ScaleAt(Cursor.Position), null);   // centred on the mouse's screen
            _form.Show();
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
        }

        private void OpenForm(float scale, Point? at)
        {
            Th.Init(scale);
            MainForm f = new MainForm(this);
            if (at.HasValue) { f.StartPosition = FormStartPosition.Manual; f.Location = at.Value; }
            f.FormClosed += delegate { f.Dispose(); if (_form == f) { _form = null; Trim(); } };
            _form = f;
        }

        /// <summary>An error on the window thread (e.g. while drawing): the window closes, so no broken control stays on screen,
        /// and the app goes on; the tray says so once. Posted, because the error arrives in the middle of a window message.</summary>
        public void CloseWindowAfterError()
        {
            OnUi(delegate
            {
                if (_exiting) return;
                if (_form != null && !_form.IsDisposed) { MainForm f = _form; _form = null; f.Dispose(); Trim(); }
                if (_windowErrorShown) return;
                _windowErrorShown = true;
                _tray.ShowBalloonTip(8000, "Salva OPM", "The window closed after an error. Salva OPM keeps running; open it again from the tray.", ToolTipIcon.Warning);
            });
        }

        /// <summary>The window moved to a screen with another scale (or the scale changed): it is built again at that scale,
        /// where it is, and the old one closes. Same scale: nothing to do.</summary>
        public void Rescale(float scale, Point at)
        {
            if (_exiting || _form == null || _form.IsDisposed || Math.Abs(scale - Th.K) < 0.01f) return;
            MainForm old = _form;
            OpenForm(scale, at);
            _form.Show();
            _form.Activate();
            old.Close();
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

        /// <summary>Tray Exit and the session's end: the exit writes, once. Like every firmware call, only on a laptop known to be
        /// this one (an Exit during start-up, before the identity is read, writes nothing).</summary>
        private void Leave()
        {
            if (_exiting) return;
            _exiting = true;
            _tick.Stop();   // after the exit writes, no heartbeat may send anything
            _retry.Stop();
            Hw.RunAndWait(delegate { if (MayWrite(_refused, _demo, _idDone)) { try { Engine.Exit(); } catch (Exception) { } } }, 10000);
        }

        /// <summary>The one gate for the firmware (start-up and exit alike): only once the laptop is known to be this one,
        /// never on another (refused) or while its identity is unknown. The demo has no real firmware.</summary>
        public static bool MayWrite(string refused, bool demo, bool identityKnown)
        {
            return refused == null && (demo || identityKnown);
        }

        // On Windows' question whether the session may end (SessionEnding), not on SessionEnded: at a plain sign-out Windows
        // ends a tray app before its SessionEnded handler can do anything (seen 2026-11-24 21:18), while the exit on the
        // question ran at every shutdown tried. If the session is still there a minute later, it was cancelled: GoOn.
        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            if (_exiting) return;   // already leaving (tray Exit, another laptop): nothing to go back to
            Leave();
            CleanExitFlag();
            _goOn.Start();
        }

        /// <summary>The sign-out or shutdown was cancelled after the exit writes: back to work, the mode applied again.</summary>
        private void GoOn()
        {
            if (!_exiting) return;
            _log.Write("the session went on (sign-out or shutdown cancelled): back to work");
            _exiting = false;
            try { if (_flag != null) File.WriteAllText(_flag, ""); } catch (Exception) { }
            _tick.Start();
            if (_booted) OnPower(true); else Boot();   // the resume path applies the mode again
            Refresh();
        }

        public void Exit()
        {
            Leave();
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            _goOn.Stop();
            _goOn.Dispose();
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

        /// <summary>The logo: a lavender diamond, drawn at run time for the window and the tray (res\app.ico is only the exe's icon).</summary>
        public static Icon MakeIcon() { return MakeIcon(Th.LogoLight, Th.LogoDark); }

        /// <summary>The tray diamond's {light, dark} colours per mode: green Eco, the theme's grey Balanced, and Performance in the
        /// app's own lavender (red means danger in the window).</summary>
        public static readonly Color[][] ModeColours = {
            new Color[] { Color.FromArgb(0x6F, 0xD6, 0x8E), Color.FromArgb(0x2E, 0x9B, 0x55) },
            new Color[] { Color.FromArgb(0xCF, 0xCA, 0xD8), Th.Section },
            new Color[] { Th.LogoLight, Th.LogoDark } };

        public static Icon MakeIcon(Color light, Color dark)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    Th.Diamond(g, 16, 16, 14, light, dark);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
