// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;

namespace SalvaOPM
{
    /// <summary>Task Scheduler through its COM API, late-bound (no extra references).</summary>
    public static class Tasks
    {
        public static object Call(object o, string name, params object[] a) { return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, a); }
        public static object Get(object o, string name) { return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null); }
        public static void Set(object o, string name, object v) { o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new object[] { v }); }

        public static object Service()
        {
            object svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            Call(svc, "Connect");
            return svc;
        }

        public static object Root(object svc) { return Call(svc, "GetFolder", "\\"); }

        /// <summary>Stops a running task (ends its process); false when it does not exist.</summary>
        public static bool Stop(string name)
        {
            try { Call(Call(Root(Service()), "GetTask", name), "Stop", 0); return true; } catch (Exception) { return false; }
        }

        public static bool Delete(string name)
        {
            try { Call(Root(Service()), "DeleteTask", name, 0); return true; } catch (Exception) { return false; }
        }

        public static bool Run(string name)
        {
            try { Call(Call(Root(Service()), "GetTask", name), "Run", new object[] { null }); return true; } catch (Exception) { return false; }
        }

        /// <summary>Starts the logon task from a normal (non-admin) process; if Windows refuses, asks for admin once instead.</summary>
        public static void RunAsUser(string name)
        {
            if (Run(name)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, "--show");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
            }
            catch (Exception) { }
        }

        /// <summary>Deletes every root task whose name starts with prefix (hidden ones included). Returns how many.</summary>
        public static int DeleteByPrefix(string prefix)
        {
            int n = 0;
            object folder = Root(Service());
            object tasks = Call(folder, "GetTasks", 1);   // TASK_ENUM_HIDDEN
            int count = (int)Get(tasks, "Count");
            for (int i = count; i >= 1; i--)
            {
                object t = tasks.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, tasks, new object[] { i });
                string name = (string)Get(t, "Name");
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                try { Call(t, "Stop", 0); } catch (Exception) { }
                try { Call(folder, "DeleteTask", name, 0); n++; } catch (Exception) { }
            }
            return n;
        }
    }

    /// <summary>
    /// Removing OMEN Gaming Hub at install: the Store app, the helpers that bring it back, and the service behind them.
    ///  - service HPOmenCap ("HP Omen HSA Service") stopped and disabled: it is what reinstalls OGH; other HP services untouched;
    ///  - tasks OmenInstallMonitor* and OmenOverlay* deleted, their two folders under Program Files\HP removed;
    ///  - Store package AD2F1837.OMENCommandCenter uninstalled for all users, and its provisioning removed.
    /// Uninstalling Salva OPM re-enables the service, so OGH can be installed again from the Microsoft Store.
    /// </summary>
    public static class Ogh
    {
        public const string Package = "AD2F1837.OMENCommandCenter";
        public const string ServiceName = "HPOmenCap";
        private static readonly string[] Background = { "OmenCommandCenterBackground", "OmenInstallMonitor" };
        private static readonly string[] Helpers = { "OverlayHelper", "OMENKeyboardRemapper", "OMENKeyboardRemapLauncher", "OmenCap" };
        private static readonly string[] TaskPrefixes = { "OmenInstallMonitor", "OmenOverlay" };
        private static readonly string[] Folders = { "OmenInstallMonitor", "Overlay" };

        private static int Kill(string[] names, Action<string> log)
        {
            int n = 0;
            foreach (string name in names)
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(name))
                        using (p)
                        {
                            try { p.Kill(); n++; if (log != null) log("stopped " + name + " (pid " + p.Id + ")"); }
                            catch (Exception e) { if (log != null) log("could not stop " + name + ": " + e.Message); }
                        }
                }
                catch (Exception) { }
            }
            return n;
        }

        /// <summary>While the app runs (start and every heartbeat): end OGH's background if something brought it back.</summary>
        public static int KillBackground(Action<string> log) { return Kill(Background, log); }

        /// <summary>Full removal. True when the Store app is gone afterwards. Every step is logged; none stops the others.</summary>
        public static bool Remove(Action<string> log)
        {
            SetService(false, log);
            Kill(Background, log);
            Kill(Helpers, log);
            foreach (string prefix in TaskPrefixes)
            {
                try { int n = Tasks.DeleteByPrefix(prefix); if (n > 0) log("deleted " + n + " " + prefix + " task(s)"); }
                catch (Exception e) { log("tasks " + prefix + ": " + e.Message); }
            }
            bool gone = RemovePackage(log);
            string hp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HP");
            foreach (string f in Folders)
            {
                string dir = Path.Combine(hp, f);
                try { if (Directory.Exists(dir)) { Directory.Delete(dir, true); log("deleted " + dir); } }
                catch (Exception e) { log("could not delete " + dir + ": " + e.Message); }
            }
            return gone;
        }

        /// <summary>Uninstall of Salva OPM: the HP service runs again, so OGH can be installed from the Microsoft Store.</summary>
        public static void Restore(Action<string> log) { SetService(true, log); }

        private static void SetService(bool enable, Action<string> log)
        {
            try
            {
                using (ManagementObject svc = new ManagementObject("Win32_Service.Name='" + ServiceName + "'"))
                {
                    svc.Get();
                    if (enable)
                    {
                        svc.InvokeMethod("ChangeStartMode", new object[] { "Automatic" });
                        svc.InvokeMethod("StartService", null);
                    }
                    else
                    {
                        svc.InvokeMethod("StopService", null);
                        svc.InvokeMethod("ChangeStartMode", new object[] { "Disabled" });
                    }
                    log(ServiceName + (enable ? " service enabled" : " service stopped and disabled"));
                }
            }
            catch (Exception e) { log(ServiceName + " service: " + e.Message); }   // not present on this Windows: nothing to do
        }

        /// <summary>Windows' own Appx/DISM cmdlets (in-box, offline). Exit 0 = the package is not installed any more.</summary>
        private static bool RemovePackage(Action<string> log)
        {
            string script = "$ErrorActionPreference='SilentlyContinue'; "
                + "Get-AppxPackage -AllUsers -Name '" + Package + "' | Remove-AppxPackage -AllUsers; "
                + "Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq '" + Package + "' } | Remove-AppxProvisionedPackage -Online | Out-Null; "
                + "if (Get-AppxPackage -AllUsers -Name '" + Package + "') { exit 3 } else { exit 0 }";
            try
            {
                string ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                ProcessStartInfo psi = new ProcessStartInfo(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(180000)) { try { p.Kill(); } catch (Exception) { } log("OGH package removal timed out"); return false; }
                    log(p.ExitCode == 0 ? "OMEN Gaming Hub package is not installed any more" : "OMEN Gaming Hub package is still installed (code " + p.ExitCode + ")");
                    return p.ExitCode == 0;
                }
            }
            catch (Exception e) { log("OGH package removal: " + e.Message); return false; }
        }
    }
}
