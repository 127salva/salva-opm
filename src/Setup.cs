// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SalvaOPM
{
    /// <summary>
    /// Install and uninstall, all from this one exe (SalvaOPM-Setup.exe). Install: copies itself to
    /// Program Files\Salva OPM, downloads the official PawnIO installer when PawnIO is missing or older than 2.2.0 (pinned release, SHA-256
    /// checked, run from the administrators-only data folder), registers the logon task, a Start-menu shortcut and an Apps
    /// entry, removes OMEN Gaming Hub and starts the app. Uninstall puts the firmware back (profile 0x30 with fans by BIOS,
    /// concurrent TDP baseline, the GPU record read at first start), re-enables the HP service and removes every file, key
    /// and task, and PawnIO when this setup installed it (through PawnIO's own uninstaller; no download).
    /// </summary>
    public static class Setup
    {
        public const string TaskName = "SalvaOPM";
        public const string Version = "2.5.2";
        public const string PawnIoUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
        public const string PawnIoSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SalvaOPM";

        public static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Salva OPM"); } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "SalvaOPM.exe"); } }
        private static string Shortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Salva OPM.lnk"); } }

        public static bool RunningInstalled()
        {
            return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The Apps entry goes during uninstall; the program folder only ~6 s after it (the running exe cannot delete
        /// itself), so the file alone said "installed" if the setup was reopened at once.</summary>
        public static bool IsInstalled()
        {
            return File.Exists(InstalledExe) && UninstallEntryExists();
        }

        /// <summary>The Apps entry (written at install, removed by uninstall); true when it cannot be read.</summary>
        private static bool UninstallEntryExists()
        {
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(UninstallKey))
                    return k != null;
            }
            catch (Exception) { return true; }
        }

        private static int Elevate(string arg)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, arg);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (Process p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
            }
            catch (Win32Exception) { return 1223; }   // the admin prompt was declined
        }

        /// <summary>The settings folder: administrators and SYSTEM write, ordinary users only read. Keeps an unprivileged
        /// process from editing what the elevated app loads (values are validated on load as well). A folder planted before
        /// install (a link, or one owned by a user, who could change its permissions back) is replaced.</summary>
        public static bool SecureDataDir()
        {
            try
            {
                string dir = Settings.DefaultDirectory();
                SecurityIdentifier admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                if (Directory.Exists(dir))
                {
                    DirectoryInfo di = new DirectoryInfo(dir);
                    IdentityReference owner = di.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
                    bool link = (di.Attributes & FileAttributes.ReparsePoint) != 0;
                    if (link) Directory.Delete(dir);   // removes the link itself, never its target
                    else if (!admins.Equals(owner) && !system.Equals(owner)) Directory.Delete(dir, true);
                }
                DirectorySecurity sec = new DirectorySecurity();
                sec.SetOwner(admins);
                sec.SetAccessRuleProtection(true, false);
                InheritanceFlags inh = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inh, PropagationFlags.None, AccessControlType.Allow));
                // created with its permissions in one call; a folder or link planted since the check above is refused, and
                // never given new permissions (on a link they would land on whatever it points to)
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir, sec);
                DirectoryInfo d = new DirectoryInfo(dir);
                IdentityReference o = d.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0 || (!admins.Equals(o) && !system.Equals(o))) return false;
                Directory.SetAccessControl(dir, sec);
                return true;
            }
            catch (Exception) { return false; }
        }

        public const int InsecureDataDir = 8;

        // ---------------- install ----------------

        /// <summary>Returns 0, 3010 (installed; restart to finish the driver) or an error code. quiet: no message boxes (the setup window reports).</summary>
        public static int Install(bool quiet)
        {
            if (!Program.IsAdmin()) return Elevate(quiet ? "--install --quiet" : "--install");
            // the elevated app and the PawnIO installer use this folder: never go on with one a user could still write to
            if (!SecureDataDir())
            {
                if (!quiet) MessageBox.Show("Not installed: C:\\ProgramData\\SalvaOPM could not be made administrators-only. Delete that folder and run the setup again.", "Salva OPM");
                return InsecureDataDir;
            }
            Logger log = new Logger(Settings.DefaultDirectory());
            StringWriter check = new StringWriter();
            if (Preflight.Run(check, false) != 0)
            {
                if (!quiet) MessageBox.Show("Not installed. Missing:\r\n" + check, "Salva OPM");
                File.WriteAllText(ResultFile(), "Not installed. Missing:\r\n" + check);
                return 2;
            }
            try
            {
                StopRunning();
                // an uninstall that just ran deletes this folder ~6 s after it ended: let it finish, or it removes the new copy
                if (Directory.Exists(InstallDir) && !UninstallEntryExists() && !RunningInstalled())
                {
                    int waited = 0;
                    while (Directory.Exists(InstallDir) && waited < 10000) { Thread.Sleep(500); waited += 500; }
                    log.Write("install: waited " + waited / 1000.0 + " s for the previous uninstall to remove the old folder");
                }
                Directory.CreateDirectory(InstallDir);
                if (!RunningInstalled()) CopyWithRetry(Application.ExecutablePath, InstalledExe);
                bool ours = PawnIoIsOurs();
                bool hadDriver = PawnIoModule.InstalledVersion() != null;
                int driver = InstallPawnIo(log);
                if (!hadDriver && (driver == 0 || driver == 3010)) ours = true;
                RegisterTask();
                CreateShortcut();
                WriteUninstallEntry(ours);
                Settings.Load(Settings.DefaultDirectory()).Save();
                bool oghGone = Ogh.Remove(log.Write);
                Tasks.Run(TaskName);
                log.Write("installed " + Version + (driver == 3010 ? " (restart to finish the PawnIO install)" : ""));
                string msg = driver == 3010 ? "Installed. Restart Windows once to finish the PawnIO driver; until then the firmware drives the fans."
                    : driver == NoDownload ? "Installed, but PawnIO could not be downloaded (no internet?). The firmware drives the fans; run the setup → Update once online."
                    : driver != 0 ? "Installed, but the PawnIO driver did not install (code " + driver + "). The firmware drives the fans until it is installed."
                    : "Installed and running. Find it in the tray or the Start menu.";
                msg += oghGone ? " OMEN Gaming Hub is removed." : " OMEN Gaming Hub could not be removed completely: uninstall it in Settings > Apps.";
                File.WriteAllText(ResultFile(), msg);
                if (!quiet) MessageBox.Show(msg, "Salva OPM");
                return driver == 3010 ? 3010 : 0;
            }
            catch (Exception e)
            {
                log.Write("install failed: " + e);
                File.WriteAllText(ResultFile(), "Install failed: " + e.Message);
                if (!quiet) MessageBox.Show("Install failed: " + e.Message, "Salva OPM");
                return 1;
            }
        }

        /// <summary>Where an elevated install leaves its one-line result for the setup window: the administrators-only data
        /// folder (users read it), never the user's TEMP, where a planted link could redirect an elevated write.</summary>
        public static string ResultFile() { return Path.Combine(Settings.DefaultDirectory(), "setup-result.txt"); }

        /// <summary>Ends the running app (its task, then any other SalvaOPM process but this one). Ended on purpose, so its
        /// "still running" flag goes too.</summary>
        private static void StopRunning()
        {
            Tasks.Stop(TaskName);
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("SalvaOPM"))
                using (p) { if (p.Id != me) { try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { } } }
            try { File.Delete(Path.Combine(Settings.DefaultDirectory(), "running.flag")); } catch (Exception) { }
        }

        private static void CopyWithRetry(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(from, to, true); return; }
                catch (IOException) { if (i >= 10) throw; Thread.Sleep(500); }
            }
        }

        /// <summary>Did a Salva OPM setup install PawnIO? The Apps entry records it (since 2.3). PawnIO that was there before
        /// us is never removed by our uninstall.</summary>
        private static bool PawnIoIsOurs()
        {
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(UninstallKey))
                    return k != null && Convert.ToInt32(k.GetValue("PawnIoByUs", 0)) == 1;
            }
            catch (Exception) { return false; }
        }

        public const int NoDownload = 6;

        /// <summary>0 = present or installed, 3010 = installed and Windows must restart, NoDownload = no internet, other =
        /// failed (the app still works without it: the firmware drives the fans).</summary>
        private static int InstallPawnIo(Logger log)
        {
            Version have = PawnIoModule.InstalledVersion();
            if (have != null && have >= new Version(2, 2, 0)) return 0;
            byte[] exe = DownloadPawnIo(log);
            if (exe == null) return NoDownload;
            if (Sha256(exe) != PawnIoSha256) { log.Write("PawnIO installer hash mismatch: " + Sha256(exe)); return 3; }
            // run from the administrators-only data folder: a user-level process cannot swap the file after the check
            string dir = Path.Combine(Settings.DefaultDirectory(), "pawnio");
            string file = Path.Combine(dir, "PawnIO_setup.exe");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(file, exe);
                return Run(file, "-install -silent", log);
            }
            catch (Exception e) { log.Write("PawnIO install: " + e.Message); return 5; }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        /// <summary>The pinned PawnIO release over HTTPS, in memory (at most 20 MB); null when it cannot be fetched.</summary>
        private static byte[] DownloadPawnIo(Logger log)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;   // the build targets no framework version, so TLS 1.2 is not on by default
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(PawnIoUrl);
                req.Timeout = 60000;
                req.ReadWriteTimeout = 60000;
                req.UserAgent = "SalvaOPM-Setup";
                using (WebResponse resp = req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (MemoryStream m = new MemoryStream())
                {
                    byte[] buf = new byte[81920];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        m.Write(buf, 0, n);
                        if (m.Length > 20 * 1024 * 1024) { log.Write("PawnIO download: larger than expected, stopped"); return null; }
                    }
                    log.Write("PawnIO installer downloaded (" + m.Length + " bytes)");
                    return m.ToArray();
                }
            }
            catch (Exception e) { log.Write("PawnIO download failed: " + e.Message); return null; }
        }

        public static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        }

        /// <summary>PawnIO's own uninstaller, from its Program Files folder (administrators-only); nothing is downloaded.</summary>
        private static int UninstallPawnIo(Logger log)
        {
            string exe = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO"), "uninstall.exe");
            if (!File.Exists(exe)) { log.Write("PawnIO uninstaller not found: " + exe); return 2; }
            return Run(exe, "-uninstall -silent", log);
        }

        private static int Run(string exe, string args, Logger log)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                if (!p.WaitForExit(180000)) { try { p.Kill(); } catch (Exception) { } log.Write("PawnIO " + args + " timed out"); return 4; }
                log.Write("PawnIO " + args + ": exit code " + p.ExitCode);
                return p.ExitCode;   // DOS codes since PawnIO 2.2.0: 0 ok, 3010 restart needed
            }
        }

        private static void RegisterTask()
        {
            string user = WindowsIdentity.GetCurrent().Name;
            object svc = Tasks.Service();
            object folder = Tasks.Root(svc);
            object def = Tasks.Call(svc, "NewTask", 0);
            object reg = Tasks.Get(def, "RegistrationInfo");
            Tasks.Set(reg, "Description", "Salva OPM: Eco / Balanced / Performance and fans for HP OMEN board 8E35.");
            Tasks.Set(reg, "Author", "Salva OPM");
            object principal = Tasks.Get(def, "Principal");
            Tasks.Set(principal, "UserId", user);
            Tasks.Set(principal, "LogonType", 3);   // interactive token
            Tasks.Set(principal, "RunLevel", 1);    // highest: the BIOS WMI calls and the driver need it
            object settings = Tasks.Get(def, "Settings");
            Tasks.Set(settings, "DisallowStartIfOnBatteries", false);
            Tasks.Set(settings, "StopIfGoingOnBatteries", false);
            Tasks.Set(settings, "ExecutionTimeLimit", "PT0S");
            Tasks.Set(settings, "MultipleInstances", 0);   // parallel: a second start only asks the running one to show its window
            Tasks.Set(settings, "Enabled", true);
            object trigger = Tasks.Call(Tasks.Get(def, "Triggers"), "Create", 9);   // at logon
            Tasks.Set(trigger, "UserId", user);
            object action = Tasks.Call(Tasks.Get(def, "Actions"), "Create", 0);     // start a program
            Tasks.Set(action, "Path", InstalledExe);
            Tasks.Call(folder, "RegisterTaskDefinition", TaskName, def, 6, user, null, 3, null);   // create or update
        }

        private static void CreateShortcut()
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object lnk = Tasks.Call(shell, "CreateShortcut", Shortcut);
            Tasks.Set(lnk, "TargetPath", InstalledExe);
            Tasks.Set(lnk, "Arguments", "--show");
            Tasks.Set(lnk, "WorkingDirectory", InstallDir);
            Tasks.Set(lnk, "IconLocation", InstalledExe + ",0");
            Tasks.Set(lnk, "Description", "Salva OPM");
            Tasks.Call(lnk, "Save");
        }

        private static void WriteUninstallEntry(bool pawnIoByUs)
        {
            using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(UninstallKey))
            {
                k.SetValue("PawnIoByUs", pawnIoByUs ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("DisplayName", "Salva OPM");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "127salva");
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + InstalledExe + "\" --uninstall --quiet");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        // ---------------- uninstall ----------------

        public const string UninstallQuestion = "Uninstall Salva OPM?\r\n\r\nThe fans, the GPU and the Windows power mode go back to their defaults, and the app's files are removed. The PawnIO driver is removed too if this setup installed it.";
        public const string RemovedMessage = "Salva OPM was removed and everything it changed was put back. OMEN Gaming Hub is the one exception: install it from the Microsoft Store if you want it.";

        public static int Uninstall(bool quiet)
        {
            if (!Program.IsAdmin()) return Elevate(quiet ? "--uninstall --quiet" : "--uninstall");
            if (!quiet && MessageBox.Show(UninstallQuestion, "Salva OPM", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 1223;
            StopRunning();
            Settings saved = Settings.Load(Settings.DefaultDirectory());   // read before the folder may be replaced below
            // the log goes to the folder only when it is administrators-only; RemoveAll deletes the folder at the end
            Logger log = new Logger(SecureDataDir() ? Settings.DefaultDirectory() : InstallDir);
            bool ours = PawnIoIsOurs();
            RestoreFirmware(saved, log);
            // Windows power mode back to Balanced on both power sources
            WindowsPowerMode win = new WindowsPowerMode();
            win.Set(Mode.Balanced, true);
            win.Set(Mode.Balanced, false);
            if (ours && PawnIoModule.InstalledVersion() != null) UninstallPawnIo(log);
            RemoveAll(log);
            if (!quiet) MessageBox.Show(RemovedMessage, "Salva OPM");
            DeleteInstallDirLater();   // last: the box above keeps this exe open (Settings > Apps runs it from the install folder)
            return 0;
        }

        /// <summary>Profile 0x30 with the firmware's own fan control, concurrent TDP baseline, the GPU record read at first start.
        /// Skipped only on a laptop positively identified as another one; when WMI cannot tell, the saved values (written by
        /// the app, which writes only on the validated laptop) are put back, since the GPU record never lapses by itself.</summary>
        private static void RestoreFirmware(Settings s, Logger log)
        {
            try
            {
                string model = Preflight.Query(@"root\cimv2", "SELECT Model FROM Win32_ComputerSystem", "Model");
                string board = Preflight.Query(@"root\cimv2", "SELECT Product FROM Win32_BaseBoard", "Product");
                if (model != null && board != null && !Identity.WritesAllowed(model.Trim(), board.Trim()))
                {
                    log.Write("uninstall: not the validated laptop (" + model + ", " + board + "); firmware left alone");
                    return;
                }
                HpBios b = new HpBios(new WmiFirmware(false, null));
                PowerData p = b.ReadPower();
                if (p != null && s.BaselineTdp >= 0 && p.Tdp != s.BaselineTdp)
                {
                    b.SetProfile(HpBios.ProfilePerformance, true);
                    b.WriteTdp(s.BaselineTdp);
                }
                b.SetProfile(HpBios.ProfileBalanced, true);
                if (s.FactoryGpu != null) b.WriteGpu(s.FactoryGpu);
                log.Write("uninstall: firmware back to profile 0x30 / fans by BIOS" + (s.FactoryGpu != null ? ", GPU " + BitConverter.ToString(s.FactoryGpu) : ""));
            }
            catch (Exception e) { log.Write("uninstall restore: " + e.Message); }
        }

        private static void RemoveAll(Logger log)
        {
            Ogh.Restore(log.Write);
            Tasks.Delete(TaskName);
            try { if (File.Exists(Shortcut)) File.Delete(Shortcut); } catch (Exception) { }
            try { RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).DeleteSubKeyTree(UninstallKey, false); } catch (Exception) { }
            try { Directory.Delete(Settings.DefaultDirectory(), true); } catch (Exception) { }
        }

        private static void DeleteInstallDirLater()
        {
            // a running exe cannot delete its own folder: a hidden cmd does it a few seconds after this process ends
            // full paths: an elevated process must never pick up a cmd.exe or ping.exe planted next to the setup
            string sys = Environment.SystemDirectory;
            // cmd /c drops the first and the last quote of a line that starts with one, so the whole line gets one more
            // pair (without it the folder stayed: H4, 2026-11-13)
            ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(sys, "cmd.exe"),
                "/c \"\"" + Path.Combine(sys, "PING.EXE") + "\" -n 6 127.0.0.1 >nul & rmdir /s /q \"" + InstallDir + "\"\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = sys;
            try { Process.Start(psi); } catch (Exception) { }
        }
    }

    /// <summary>The window of SalvaOPM-Setup.exe: what will happen, then Install, or Update and Uninstall when already installed.</summary>
    public sealed class SetupForm : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private readonly Button _go = new Button();
        private readonly Button _remove = new Button();
        private string _status = "";
        private bool _done;

        public SetupForm()
        {
            Text = "Salva OPM setup";
            Icon = App.MakeIcon();
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Th.Bg;
            ClientSize = new Size(Th.D(460), Th.D(330));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            bool installed = Setup.IsInstalled();
            Style(_go, installed ? "Update" : "Install", Th.Accent);
            Style(_remove, "Uninstall", Th.Off);
            _go.SetBounds(Th.D(28), Th.D(262), Th.D(installed ? 196 : 404), Th.D(44));
            _remove.SetBounds(Th.D(236), Th.D(262), Th.D(196), Th.D(44));
            _remove.Visible = installed;
            _go.Click += delegate { if (_done) Close(); else Work(true); };
            _remove.Click += delegate
            {
                if (MessageBox.Show(Setup.UninstallQuestion, "Salva OPM", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) Work(false);
            };
            Controls.Add(_go);
            Controls.Add(_remove);
        }

        private static void Style(Button b, string text, Color back)
        {
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = Color.White;
            b.Font = Th.SegBold;
            b.Cursor = Cursors.Hand;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            try { DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch (Exception) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Th.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Th.Diamond(g, Th.D(44), Th.D(44), Th.D(16));
            Th.Text(g, "Salva OPM", Th.Title, Th.TextHi, Th.D(72), Th.D(30));
            Th.TextRight(g, "v" + Setup.Version, Th.Mono, Th.Sub, Th.D(432), Th.D(36));
            int y = Th.D(88);
            foreach (string line in new string[] {
                "Installs to C:\\Program Files\\Salva OPM and starts at logon",
                "Installs the PawnIO driver for the CPU temperature (downloaded once)",
                "Removes OMEN Gaming Hub; keyboard RGB stays as it is now",
                "Open this setup again to update or uninstall" })
            {
                using (SolidBrush b = new SolidBrush(Th.Accent)) g.FillEllipse(b, Th.D(30), y + Th.D(7), Th.D(6), Th.D(6));
                Th.Text(g, line, Th.Small, Th.TextB, Th.D(46), y);
                y += Th.D(26);
            }
            if (_status.Length > 0)
                TextRenderer.DrawText(g, _status, Th.Small, new Rectangle(Th.D(28), Th.D(196), Th.D(404), Th.D(60)), _done ? Th.Ok : Th.Sub,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        /// <summary>Runs the install or the uninstall elevated (one admin prompt) and shows its result.</summary>
        private void Work(bool install)
        {
            _go.Enabled = _remove.Enabled = false;
            _status = (install ? "Installing" : "Uninstalling") + "… (approve the admin prompt)";
            Invalidate();
            DateTime started = DateTime.UtcNow.AddSeconds(-2);
            Thread t = new Thread(delegate()
            {
                int rc = install ? Setup.Install(true) : Setup.Uninstall(true);
                string msg = rc == 1223 ? "The admin prompt was declined; nothing was changed."
                    : install && rc == Setup.InsecureDataDir ? "Not installed: C:\\ProgramData\\SalvaOPM could not be made administrators-only. Delete that folder and run the setup again."
                    : install ? "Install finished with code " + rc + "." : rc == 0 ? Setup.RemovedMessage : "Uninstall finished with code " + rc + ".";
                try
                {
                    string f = Setup.ResultFile();   // only this run's result (the file is not ours to delete: administrators-only folder)
                    if (install && rc != 1223 && File.Exists(f) && File.GetLastWriteTimeUtc(f) >= started) msg = File.ReadAllText(f);
                }
                catch (Exception) { }
                BeginInvoke((MethodInvoker)delegate
                {
                    _status = msg;
                    _done = rc == 0 || rc == 3010;
                    _go.Text = _done ? "Close" : install ? "Try again" : "Update";
                    _go.Width = Th.D(_done ? 404 : _remove.Visible ? 196 : 404);
                    if (_done) _remove.Visible = false;
                    _go.Enabled = _remove.Enabled = true;
                    Invalidate();
                });
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
