// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace SalvaOPM
{
    /// <summary>
    /// SalvaOPM.exe is the installer, the app and its tools in one file.
    ///   (no arguments)        from Program Files: the app (started at logon by the "SalvaOPM" task, elevated);
    ///                         from anywhere else: the setup window
    ///   --show                open the window of the running app (Start-menu shortcut; starts the task when needed)
    ///   --install / --uninstall   install or remove everything (asks for admin once)
    ///   --check               what this Windows has / lacks (no writes)
    ///   --read                every allow-listed read (admin; writes refused)
    ///   --dry-run [minutes]   live readings and what the app WOULD do; writes refused (admin)
    ///   --selftest            built-in checks (pure logic + simulator scenarios)
    ///   --demo                the app against the simulator: no hardware, no admin, no Windows setting touched
    /// </summary>
    public static class Program
    {
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
        [DllImport("kernel32.dll")] private static extern bool SetDefaultDllDirectories(uint flags);
        private const uint LoadLibrarySearchSystem32 = 0x800;

        public const string ShowEventName = @"Local\SalvaOPM.Show";

        public static bool IsAdmin() { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }

        private static void Console_() { AttachConsole(-1); Console.WriteLine(); }

        [STAThread]
        public static int Main(string[] args)
        {
            // the setup runs elevated from Downloads: DLLs loaded from here on come from System32, never from beside the exe
            try { SetDefaultDllDirectories(LoadLibrarySearchSystem32); } catch (Exception) { }
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            switch (cmd)
            {
                case "--selftest": Console_(); return SelfCheck.Run();
                case "--check": Console_(); return Preflight.Run(Console.Out, true);
                case "--read": Console_(); return Tools.ReadAll(Console.Out);
                case "--dry-run":
                    {
                        Console_();
                        int minutes, n;
                        minutes = args.Length > 1 && int.TryParse(args[1], out n) && n > 0 ? n : 5;   // a bad value keeps the default
                        return Tools.DryRun(Console.Out, minutes);
                    }
                case "--install": return Setup.Install(args.Length > 1 && args[1] == "--quiet");
                case "--uninstall": return Setup.Uninstall(args.Length > 1 && args[1] == "--quiet");
                case "--show": return Show();
                case "--demo": return RunApp(true);
                case "":
                    if (Setup.RunningInstalled()) return IsAdmin() ? RunApp(false) : Show();
                    UiInit();
                    Application.Run(new SetupForm());
                    return 0;
                default:
                    Console_();
                    Console.WriteLine("usage: SalvaOPM.exe [--show | --install | --uninstall | --check | --read | --dry-run [minutes] | --selftest | --demo]");
                    return 2;
            }
        }

        public static void UiInit()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) Th.Init(g.DpiX / 96f);
        }

        /// <summary>Ask the running app to show its window; start it through its task first when it is not running.</summary>
        private static int Show()
        {
            if (Signal()) return 0;
            if (IsAdmin() && Setup.RunningInstalled()) return RunApp(false);
            Tasks.RunAsUser(Setup.TaskName);
            for (int i = 0; i < 40; i++) { Thread.Sleep(250); if (Signal()) return 0; }
            return 1;
        }

        private static bool Signal()
        {
            try { using (EventWaitHandle h = EventWaitHandle.OpenExisting(ShowEventName, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize)) { h.Set(); return true; } }
            catch (Exception) { return false; }
        }

        private static EventWaitHandle CreateShowEvent()
        {
            EventWaitHandleSecurity sec = new EventWaitHandleSecurity();
            sec.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, AccessControlType.Allow));
            bool created;
            return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out created, sec);
        }

        private static int RunApp(bool demo)
        {
            bool created;
            Mutex single;
            // one copy per PC: two copies in two Windows sessions would both write to the firmware
            try { single = new Mutex(true, demo ? @"Local\SalvaOPM.Demo" : @"Global\SalvaOPM", out created); }
            catch (UnauthorizedAccessException) { single = null; created = false; }
            using (single)
            {
                if (!created)
                {
                    if (!demo && !Signal())
                        MessageBox.Show("Salva OPM is already running for another Windows user. Only one copy can control the fans.", "Salva OPM");
                    return 0;
                }
                if (!demo && !IsAdmin())
                {
                    MessageBox.Show("Salva OPM needs administrator rights to talk to the BIOS. Install it with SalvaOPM-Setup.exe; it then starts by itself at logon.", "Salva OPM");
                    return 1;
                }
                UiInit();
                string dir = demo ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "demo-state") : Settings.DefaultDirectory();
                if (!demo) Setup.SecureDataDir();
                Logger log = new Logger(dir);
                IFirmware fw = demo ? (IFirmware)new SimFirmware() : new WmiFirmware(false, null);
                HpBios bios = new HpBios(fw);
                Settings settings = Settings.Load(dir);
                IPowerModeSetter win = demo ? (IPowerModeSetter)new NullPowerMode() : new WindowsPowerMode();
                IPowerSource power = demo ? (IPowerSource)new SimPowerSource() : new SystemPowerSource(bios);
                ISensors sensors = demo ? (ISensors)new SimSensors() : new SystemSensors();
                Engine engine = new Engine(bios, settings, win, sensors, new GovernorConfig(), log.Write);
                // fail safe: an unexpected error stops all writes for this session and hands everything to the firmware
                int errors = 0;
                App app = null;
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    if (++errors <= 5) log.Write("unhandled: " + e.Exception);
                    string why = e.Exception.GetType().Name + ": " + e.Exception.Message;
                    if (app != null) app.Hw.Post("failsafe", delegate { engine.FailSafe(why); });   // on the hardware thread, like every engine call
                    else lock (engine) engine.FailSafe(why);
                };
                // other threads: the process ends; the firmware drops the host state by itself within ~2 minutes
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { log.Write("fatal: " + e.ExceptionObject); };
                EventWaitHandle show = demo ? null : CreateShowEvent();
                app = new App(engine, power, demo, log, show);
                if (demo) app.ShowWindow();
                Application.Run(app);
                if (show != null) show.Dispose();
                return 0;
            }
        }
    }

    /// <summary>Append-only log next to state.txt (events only, never per tick); rotated at 256 KB.</summary>
    public sealed class Logger
    {
        public readonly string Path;
        private readonly object _sync = new object();
        public Logger(string dir) { Path = System.IO.Path.Combine(dir, "log.txt"); }
        public void Write(string line)
        {
            lock (_sync)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    FileInfo fi = new FileInfo(Path);
                    if (fi.Exists && fi.Length > 256 * 1024) { string old = Path + ".old"; if (File.Exists(old)) File.Delete(old); File.Move(Path, old); }
                    File.AppendAllText(Path, DateTime.Now.ToString("s") + " " + line + "\r\n");
                }
                catch (Exception) { }
            }
        }
    }

    /// <summary>--check: names exactly what is missing; changes nothing. Its BIOS probe is a type-42 read (never type 16).</summary>
    public static class Preflight
    {
        private static int _fail;

        private static void Item(TextWriter o, bool ok, string what)
        {
            if (!ok) _fail++;
            o.WriteLine((ok ? "OK       " : "MISSING  ") + what);
        }

        /// <summary>First row's property as text ("" when empty); null when the query failed or found nothing.</summary>
        public static string Query(string ns, string wql, string prop)
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery(wql), WmiFirmware.Bounded()))
                    foreach (ManagementObject m in s.Get()) { object v = m[prop]; return v == null ? "" : v.ToString(); }
            }
            catch (Exception) { }
            return null;
        }

        public static int Run(TextWriter o, bool verbose)
        {
            _fail = 0;
            Item(o, Environment.Is64BitOperatingSystem, "64-bit Windows");
            string wmi = Query(@"root\cimv2", "SELECT State FROM Win32_Service WHERE Name='Winmgmt'", "State");
            Item(o, wmi == "Running", "WMI service (Winmgmt) running" + (wmi == null ? " - WMI not reachable" : ""));
            string sch = Query(@"root\cimv2", "SELECT State FROM Win32_Service WHERE Name='Schedule'", "State");
            Item(o, sch == "Running", "Task Scheduler service (Schedule) running");
            // the same rule the app uses at start: the setup must not remove OMEN Gaming Hub where the app would only monitor
            string model = Query(@"root\cimv2", "SELECT Model FROM Win32_ComputerSystem", "Model");
            string board = Query(@"root\cimv2", "SELECT Product FROM Win32_BaseBoard", "Product");
            Item(o, model != null && board != null && Identity.WritesAllowed(model.Trim(), board.Trim()),
                "OMEN 16-ap0 with board 8E35 (found: " + (model == null ? "unknown" : model.Trim()) + ", " + (board == null ? "unknown" : board.Trim()) + ")");
            string mapper = Query(@"root\cimv2", "SELECT Status FROM Win32_PnPEntity WHERE DeviceID LIKE 'ACPI\\\\PNP0C14%'", "Status");
            Item(o, mapper == "OK", "Windows ACPI-WMI mapper device (ACPI\\PNP0C14)");
            bool cls = false;
            try { using (ManagementClass c = new ManagementClass(@"root\wmi:hpqBIntM")) { c.Get(); cls = true; } } catch (Exception) { }
            Item(o, cls, "HP BIOS WMI interface (root\\wmi hpqBIntM)");
            if (Program.IsAdmin())
                Item(o, cls && new HpBios(new WmiFirmware(true, null)).ReadPower() != null, "BIOS answers a read-only call (type 42)");
            else if (verbose) o.WriteLine("INFO     not elevated: the BIOS read test is skipped (the installed app runs elevated)");
            if (verbose)
            {
                Version pv = PawnIoModule.InstalledVersion();
                o.WriteLine((pv != null ? "OK       PawnIO " + pv : "INFO     PawnIO not installed (the setup installs it; without it the firmware drives the fans)"));
                o.WriteLine((File.Exists(Path.Combine(Environment.SystemDirectory, "nvml.dll")) ? "OK       NVIDIA nvml.dll (GPU temperature)" : "INFO     no nvml.dll: GPU temperature unavailable"));
                o.WriteLine(_fail == 0 ? "All prerequisites present." : _fail + " prerequisite(s) missing.");
            }
            return _fail;
        }
    }

    /// <summary>--read and --dry-run: firmware access is read-only (WmiFirmware refuses every write type before sending).</summary>
    public static class Tools
    {
        public static int ReadAll(TextWriter o)
        {
            if (!Program.IsAdmin()) { o.WriteLine("Run from an elevated PowerShell (administrator)."); return 1; }
            HpBios b = new HpBios(new WmiFirmware(true, null));
            int bad = 0;
            PowerData p = b.ReadPower(); if (p == null) bad++;
            o.WriteLine("power data (type 42)     : " + (p == null ? "FAILED" : string.Format("PL1 {0}  PL2 {1}  PL4 {2}  concurrent TDP {3}", p.Pl1, p.Pl2, p.Pl4, p.Tdp)));
            byte[] g = b.ReadGpu(); if (g == null) bad++;
            o.WriteLine("GPU record (type 33)     : " + (g == null ? "FAILED" : string.Format("cTGP {0}  PPAB {1}  dState {2}  GPS {3}  ({4})", g[0], g[1], g[2], g[3], Engine.GpuName(g))));
            for (int i = 0; i < 4; i++) { int s = b.ReadSensor(i); if (s < 0) bad++; o.WriteLine("sensor " + i + " (type 35)      : " + (s < 0 ? "FAILED" : s + " C")); }
            int[] f = b.ReadFanLevels(); if (f == null) bad++;
            o.WriteLine("fan levels (type 45)     : " + (f == null ? "FAILED" : "CPU " + f[0] + "  GPU " + f[1] + "  (x100 rpm)"));
            int t = b.ReadThrottling(); if (t < 0) bad++;
            o.WriteLine("throttling (type 53)     : " + (t < 0 ? "FAILED" : t == 1 ? "yes" : "no"));
            byte[] c = b.ReadCharger(); if (c == null) bad++;
            o.WriteLine("charger (cmd 1 type 15)  : " + (c == null ? "FAILED" : BitConverter.ToString(c) + (ChargerRule.IsWeak(c) ? "  -> weak (would count as unplugged)" : "  -> normal")));
            using (SystemSensors s = new SystemSensors())
            {
                s.Poll(); s.Poll(); s.Poll();
                o.WriteLine("CPU temperature          : " + (double.IsNaN(s.CpuTemp) ? "-- (" + s.CpuStatus + ")" : s.CpuTemp.ToString("0.0") + " C (" + s.CpuStatus + ")"));
                o.WriteLine("GPU temperature          : " + (double.IsNaN(s.GpuTemp) ? "-- (" + s.GpuStatus + ")" : s.GpuTemp.ToString("0") + " C, load " + s.GpuLoad + " %"));
            }
            o.WriteLine(bad == 0 ? "All reads OK. Nothing was written." : bad + " read(s) failed. Nothing was written.");
            return bad;
        }

        public static int DryRun(TextWriter o, int minutes)
        {
            if (!Program.IsAdmin()) { o.WriteLine("Run from an elevated PowerShell (administrator)."); return 1; }
            HpBios b = new HpBios(new WmiFirmware(true, null));
            SystemPowerSource src = new SystemPowerSource(b);
            GovernorConfig cfg = new GovernorConfig();
            GovernorState st = Governor.NewState(cfg, HpBios.GpuMax[3], true);
            FanCurve auto = FanCurve.Auto();
            ThermalGuard guard = new ThermalGuard();
            int ir = 35;
            o.WriteLine("Dry run for " + minutes + " min: live readings, the Auto curve, the thermal guard and the Performance governor; nothing is written. Ctrl+C to stop.");
            using (SystemSensors s = new SystemSensors())
            {
                DateTime end = DateTime.Now.AddMinutes(minutes);
                long t0 = Environment.TickCount;
                int loops = 0;
                while (DateTime.Now < end)
                {
                    s.Poll();
                    PowerSnapshot ps = src.Read();
                    if (st.Tick % cfg.IrCycleTicks == 0) { int v = b.ReadSensor(0); if (v >= 0) ir = v; }
                    int amb = b.ReadSensor(Engine.ChassisSensor);
                    int[] f = b.ReadFanLevels();
                    PowerData p = b.ReadPower();
                    GovernorInput i = new GovernorInput();
                    i.OnAc = ps.OnAc; i.BatteryDrain = ps.BatteryDrain; i.BatteryPercent = ps.BatteryPercent < 0 ? 1.0 : ps.BatteryPercent / 100.0;
                    i.GpuUsage = double.IsNaN(s.GpuLoad) ? 100 : (int)s.GpuLoad; i.IrTemp = ir;
                    i.Throttling = b.ReadThrottling() == 1 || (!double.IsNaN(s.CpuTemp) && s.CpuTemp >= 90);
                    GovernorOutput r = Governor.Step(cfg, st, i);
                    if (loops++ % 2 == 0) guard.Tick(s.CpuTemp, amb, f, Environment.TickCount - t0);   // every 10 s, as the app (Engine.GuardMs)
                    int[] want = auto.Target(s.CpuTemp, s.GpuTemp);
                    o.WriteLine(string.Format("{0:HH:mm:ss}  cpu {1} gpu {2} amb {3}  fans {4}  live PL {5} TDP {6}  ->  curve {7}/{8}  guard {9}  Performance PL {10} GPS {11}",
                        DateTime.Now, Fmt(s.CpuTemp), s.GpuAsleep ? "zz" : Fmt(s.GpuTemp), amb, f == null ? "?" : f[0] + "/" + f[1],
                        p == null ? "?" : p.Pl1 + "/" + p.Pl2, p == null ? "?" : p.Tdp.ToString(), want[0], want[1], guard.Active ? "MAX" : "off", r.Pl1, r.Gps));
                    Thread.Sleep((int)Engine.TickMs);
                }
            }
            return 0;
        }

        private static string Fmt(double v) { return double.IsNaN(v) ? "--" : v.ToString("0"); }
    }
}
