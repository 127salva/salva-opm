// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva
// Portions adapted from Ohman (https://github.com/P4R1H/Ohman), GPL-3.0-or-later.

using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace SalvaOPM
{
    /// <summary>Chip temperatures for the window and the Performance governor. NaN = unknown. Poll() is called from the engine's tick.</summary>
    public interface ISensors : IDisposable
    {
        void Poll();
        double CpuTemp { get; }
        double GpuTemp { get; }
        double GpuLoad { get; }       // percent; NaN unknown
        bool GpuAsleep { get; }       // the dGPU is in D3: cool by definition, not polled (polling would wake it)
        string CpuStatus { get; }
        string GpuStatus { get; }
    }

    public sealed class SimSensors : ISensors
    {
        public double Cpu = 48, Gpu = 45, Load = 3;
        public bool Asleep;
        public void Poll() { }
        public double CpuTemp { get { return Cpu; } }
        public double GpuTemp { get { return Asleep ? double.NaN : Gpu; } }
        public double GpuLoad { get { return Asleep ? 0 : Load; } }
        public bool GpuAsleep { get { return Asleep; } }
        public string CpuStatus { get { return double.IsNaN(Cpu) ? "simulated: no CPU temperature" : "simulated"; } }
        public string GpuStatus { get { return "simulated"; } }
        public void Dispose() { }
    }

    /// <summary>The PawnIO driver (pawnio.eu, signed by namazso.eu): device \Device\PawnIO, one handle per loaded signed module,
    /// IOCTL_PIO_LOAD_BINARY with the module bytes, IOCTL_PIO_EXECUTE_FN with a 32-byte function name and 64-bit cells
    /// (protocol as documented by PawnIOLib.h / LibreHardwareMonitor, used the same way by Ohman Driver.cs).</summary>
    public sealed class PawnIoModule : IDisposable
    {
        private const uint DeviceType = 41394u << 16;
        private const uint IoctlLoad = DeviceType | (0x821u << 2);
        private const uint IoctlExecute = DeviceType | (0x841u << 2);
        private const int NameLength = 32;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, uint inLen, byte[] outBuf, uint outLen, out uint returned, IntPtr overlapped);

        private readonly SafeFileHandle _h;
        private PawnIoModule(SafeFileHandle h) { _h = h; }

        public static PawnIoModule Load(byte[] blob, out string why)
        {
            why = null;
            SafeFileHandle h = CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", 0xC0000000u, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            if (h == null || h.IsInvalid) { why = "PawnIO device not available (error " + Marshal.GetLastWin32Error() + ")"; return null; }
            uint ret;
            if (!DeviceIoControl(h, IoctlLoad, blob, (uint)blob.Length, null, 0, out ret, IntPtr.Zero))
            {
                why = "PawnIO rejected the CPU module (error " + Marshal.GetLastWin32Error() + ")";
                h.Close();
                return null;
            }
            return new PawnIoModule(h);
        }

        public bool Call(string fn, ulong[] input, ulong[] output)
        {
            int nin = input == null ? 0 : input.Length, nout = output == null ? 0 : output.Length;
            byte[] inBuf = new byte[NameLength + nin * 8];
            Encoding.ASCII.GetBytes(fn, 0, Math.Min(fn.Length, NameLength - 1), inBuf, 0);
            for (int i = 0; i < nin; i++) Array.Copy(BitConverter.GetBytes(input[i]), 0, inBuf, NameLength + i * 8, 8);
            byte[] outBuf = new byte[nout * 8];
            uint ret;
            if (!DeviceIoControl(_h, IoctlExecute, inBuf, (uint)inBuf.Length, outBuf, (uint)outBuf.Length, out ret, IntPtr.Zero)) return false;
            for (int i = 0; i < nout && i * 8 < ret; i++) output[i] = BitConverter.ToUInt64(outBuf, i * 8);
            return true;
        }

        public void Dispose() { try { _h.Close(); } catch (Exception) { } }

        /// <summary>Installed PawnIO version from its uninstall entry, or null.</summary>
        public static Version InstalledVersion()
        {
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
                {
                    string s = k == null ? null : k.GetValue("DisplayVersion") as string;
                    Version v;
                    return s != null && Version.TryParse(s.Trim(), out v) ? v : null;
                }
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>Real sensors: AMD die temperature (SMN THM_TCON_CUR_TMP through PawnIO's AMDFamily17 module, median of three)
    /// and NVIDIA GPU temperature/load through the driver's own nvml.dll, skipped while the dGPU sleeps in D3.</summary>
    public sealed class SystemSensors : ISensors
    {
        private const uint ThmTconCurTmp = 0x00059800;
        private PawnIoModule _cpu;
        private Mutex _pci;
        private readonly double[] _recent = { double.NaN, double.NaN, double.NaN };
        private string _cpuStatus = "not started";
        private string _gpuStatus = "not started";
        private double _cpuTemp = double.NaN, _gpuTemp = double.NaN, _gpuLoad = double.NaN;
        private bool _asleep;

        public double CpuTemp { get { return _cpuTemp; } }
        public double GpuTemp { get { return _gpuTemp; } }
        public double GpuLoad { get { return _gpuLoad; } }
        public bool GpuAsleep { get { return _asleep; } }
        public string CpuStatus { get { return _cpuStatus; } }
        public string GpuStatus { get { return _gpuStatus; } }

        public SystemSensors()
        {
            _nextCpuOpenMs = DateTime.UtcNow.Ticks / 10000 + 60000;
            OpenCpu();
            _nvDevice = FindNvidiaDevice();
            if (_nvDevice == null) _gpuStatus = "no NVIDIA GPU found";
        }

        private void OpenCpu()
        {
            Version v = PawnIoModule.InstalledVersion();
            if (v == null) { _cpuStatus = "PawnIO not installed"; return; }
            byte[] blob;
            using (Stream s = typeof(SystemSensors).Assembly.GetManifestResourceStream("SalvaOPM.AMDFamily17.bin"))
            {
                if (s == null) { _cpuStatus = "CPU module missing from this build"; return; }
                blob = new byte[s.Length];
                s.Read(blob, 0, blob.Length);
            }
            string why;
            _cpu = PawnIoModule.Load(blob, out why);
            if (_cpu == null) { _cpuStatus = why; return; }
            try { _pci = new Mutex(false, @"Global\Access_PCI"); } catch (Exception) { }   // the PCI-config lock other monitoring tools share
            _cpuStatus = "PawnIO " + v;
        }

        public void Poll()
        {
            double was = _cpuTemp;
            ReadCpu();
            if (_cpuTemp >= was + 5) _nextGpuMs = 0;   // load is starting: read an idle GPU now, not up to 30 s later
            ReadGpu();
        }

        private long _nextCpuOpenMs;

        private void ReadCpu()
        {
            if (_cpu == null)
            {
                long now = DateTime.UtcNow.Ticks / 10000;
                if (now >= _nextCpuOpenMs) { _nextCpuOpenMs = now + 60000; OpenCpu(); }   // the driver may not be ready at logon: try again once a minute
                if (_cpu == null) { _cpuTemp = double.NaN; return; }
            }
            double t = double.NaN;
            bool held = false;
            try
            {
                if (_pci != null) { try { held = _pci.WaitOne(250); } catch (AbandonedMutexException) { held = true; } }
                ulong[] o = new ulong[1];
                // another tool holds the lock: skip this reading (NaN; the median covers it) rather than share the index/data pair
                if ((_pci == null || held) && _cpu.Call("ioctl_read_smn", new ulong[] { ThmTconCurTmp }, o)) t = Tctl((uint)o[0]);
            }
            catch (Exception) { }
            finally { if (held) { try { _pci.ReleaseMutex(); } catch (Exception) { } } }
            _recent[2] = _recent[1]; _recent[1] = _recent[0]; _recent[0] = t;
            _cpuTemp = Median(_recent);
        }

        /// <summary>THM_TCON_CUR_TMP to degrees C: bits 31:21 in eighths of a degree, minus 49 when bit 19 (range select,
        /// Zen 2 and later) is set; NaN outside 0..130.</summary>
        public static double Tctl(uint r)
        {
            double t = ((r >> 21) & 0x7FF) * 0.125;
            if ((r & (1u << 19)) != 0) t -= 49;
            return t > 0 && t < 130 ? t : double.NaN;
        }

        private static double Median(double[] v)
        {
            int n = 0; double[] k = new double[3];
            foreach (double d in v) if (!double.IsNaN(d)) k[n++] = d;
            if (n == 0) return double.NaN;
            if (n < 3) return k[0];
            double a = k[0], b = k[1], c = k[2];
            return Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
        }

        // ---- GPU ----
        private readonly string _nvDevice;
        private int _quiet;
        private long _nextGpuMs;

        private static string FindNvidiaDevice()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", "SELECT PNPDeviceID FROM Win32_VideoController"))
                    foreach (ManagementObject m in s.Get())
                    {
                        string id = m["PNPDeviceID"] as string;
                        if (id != null && id.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) >= 0) return id;
                    }
            }
            catch (Exception) { }
            return null;
        }

        [StructLayout(LayoutKind.Sequential)] private struct DevPropKey { public Guid Fmtid; public uint Pid; }
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out uint inst, string id, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_DevNode_PropertyW(uint inst, ref DevPropKey key, out uint type, byte[] buf, ref uint size, uint flags);

        /// <summary>Windows' own device record, which does not wake the GPU: DEVPKEY_Device_PowerData, PD_MostRecentPowerState 4 = D3.</summary>
        private bool Asleep()
        {
            if (_nvDevice == null) return false;
            try
            {
                uint inst;
                if (CM_Locate_DevNodeW(out inst, _nvDevice, 0) != 0) return false;
                DevPropKey key = new DevPropKey();
                key.Fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
                key.Pid = 32;
                byte[] buf = new byte[64];
                uint type, size = (uint)buf.Length;
                if (CM_Get_DevNode_PropertyW(inst, ref key, out type, buf, ref size, 0) != 0 || size < 8) return false;
                return BitConverter.ToInt32(buf, 4) == 4;
            }
            catch (Exception) { return false; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryW(string path);
        [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] private static extern int nvmlShutdown();
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
        [StructLayout(LayoutKind.Sequential)] private struct NvmlUtil { public uint Gpu, Memory; }
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtil util);

        private bool _nvmlLoaded, _nvmlGone, _nvmlOpen;
        private IntPtr _dev;

        private void ReadGpu()
        {
            if (_nvDevice == null || _nvmlGone) { _gpuTemp = double.NaN; return; }
            long now = DateTime.UtcNow.Ticks / 10000;
            if (Asleep())
            {
                CloseNvml();
                _asleep = true; _gpuTemp = double.NaN; _gpuLoad = 0;
                _gpuStatus = "NVIDIA GPU asleep";
                return;
            }
            if (_asleep) { _asleep = false; _nextGpuMs = 0; }   // just woke up: read it now
            if (now < _nextGpuMs) return;                      // an idle GPU is read every 30 s, a busy one every tick
            try
            {
                if (!_nvmlLoaded)
                {
                    string dll = Path.Combine(Environment.SystemDirectory, "nvml.dll");   // full path first: never a dll planted next to the exe
                    if (!File.Exists(dll) || LoadLibraryW(dll) == IntPtr.Zero) { _nvmlGone = true; _gpuStatus = "nvml.dll not found"; return; }
                    _nvmlLoaded = true;
                }
                if (!_nvmlOpen)
                {
                    if (nvmlInit_v2() != 0) throw new InvalidOperationException("NVML init failed");
                    _nvmlOpen = true;
                    if (nvmlDeviceGetHandleByIndex_v2(0, out _dev) != 0) throw new InvalidOperationException("NVML device not found");
                }
                uint t;
                if (nvmlDeviceGetTemperature(_dev, 0, out t) != 0) throw new InvalidOperationException("NVML temperature failed");
                _gpuTemp = t > 0 && t < 130 ? t : double.NaN;
                NvmlUtil u;
                _gpuLoad = nvmlDeviceGetUtilizationRates(_dev, out u) == 0 && u.Gpu <= 100 ? u.Gpu : double.NaN;
                _gpuStatus = "NVML";
                bool busy = !double.IsNaN(_gpuLoad) && _gpuLoad > 1;
                _quiet = busy ? 0 : Math.Min(_quiet + 1, 100);
                if (_quiet >= 3) { CloseNvml(); _nextGpuMs = now + 30000; }   // let an idle GPU go back to sleep
                else _nextGpuMs = 0;
            }
            catch (Exception e)
            {
                CloseNvml();
                _gpuTemp = double.NaN;
                _gpuStatus = e.Message;
                _nextGpuMs = now + 30000;
            }
        }

        private void CloseNvml()
        {
            if (!_nvmlOpen) return;
            try { nvmlShutdown(); } catch (Exception) { }
            _nvmlOpen = false;
        }

        public void Dispose()
        {
            CloseNvml();
            if (_cpu != null) _cpu.Dispose();
            if (_pci != null) _pci.Close();
        }
    }
}
