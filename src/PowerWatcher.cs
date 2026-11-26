// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SalvaOPM
{
    public sealed class PowerSnapshot
    {
        public bool OnAc;
        public int BatteryPercent = -1;   // 0..100, -1 unknown
        public bool BatteryDrain;         // OGH's IsBatteryDrain
        public bool ChargerWeak;          // charger below the laptop's requirement (treated like unplugged)
        public bool PowerOk { get { return OnAc && !ChargerWeak; } }
    }

    /// <summary>OGH's IsBatteryDrain rule (Entry.PowerEvent, docs/what-it-does.md "Safety in Performance"), evaluated on every power event.</summary>
    public sealed class DrainTracker
    {
        private bool _drain;
        private bool _hasLast;
        private int _last;

        public bool Update(bool onAc, bool acChanged, int percent)
        {
            if (percent < 0 || percent > 100) return _drain;          // OGH ignores out-of-range readings
            if (!onAc || acChanged || !_hasLast) _drain = false;
            else if (_drain) _drain = !(_last < percent);             // stays draining until the percentage rises
            else _drain = _last > percent;                            // starts draining when the percentage drops
            _last = percent;
            _hasLast = true;
            return _drain;
        }
    }

    /// <summary>Charger classification from the BIOS reply [status, flags, usbcDesign/5, rating/5].
    /// Stricter than OGH (fail-open): only BelowRequirement, or a USB-C charger rated below the design rating, counts as weak.</summary>
    public static class ChargerRule
    {
        public static bool IsWeak(byte[] d)
        {
            if (d == null || d.Length < 4) return false;              // unreadable: normal charger
            if (d[0] == 2) return true;                               // BelowRequirement
            if (d[0] == 5)                                            // ConnectedTypeC
            {
                int design = d[2] * 5;
                int rating = d[3] == 0xFF ? 0 : d[3] * 5;
                if (rating > 0 && rating < design) return true;
                if ((d[1] & 0x80) != 0 && design == 0) return true;
            }
            return false;                                             // MeetsRequirement, NotSupported, errors: normal
        }
    }

    /// <summary>Flap guard: more than 3 power-state changes within 30 s means wait before acting.</summary>
    public sealed class FlapGuard
    {
        private readonly List<long> _times = new List<long>();

        public bool Allow(long nowMs)
        {
            _times.RemoveAll(delegate(long t) { return nowMs - t >= 30000; });
            _times.Add(nowMs);
            return _times.Count <= 3;
        }
    }

    public interface IPowerSource
    {
        PowerSnapshot Read();
    }

    /// <summary>Real power source: GetSystemPowerStatus plus the BIOS charger read (two consecutive weak reads required).</summary>
    public sealed class SystemPowerSource : IPowerSource
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        private readonly HpBios _bios;
        private readonly DrainTracker _drain = new DrainTracker();
        private bool _hasLastAc;
        private bool _lastAc;

        public SystemPowerSource(HpBios bios) { _bios = bios; }

        public PowerSnapshot Read()
        {
            SYSTEM_POWER_STATUS s;
            PowerSnapshot p = new PowerSnapshot();
            if (!GetSystemPowerStatus(out s)) { p.OnAc = true; return p; }   // unknown: assume AC (no automatic Eco on a failed read)
            p.OnAc = s.ACLineStatus != 0;                                       // 1 = online, 255 = unknown (treated as AC)
            p.BatteryPercent = s.BatteryLifePercent == 255 ? -1 : s.BatteryLifePercent;
            bool acChanged = _hasLastAc && _lastAc != p.OnAc;
            _hasLastAc = true;
            _lastAc = p.OnAc;
            p.BatteryDrain = _drain.Update(p.OnAc, acChanged, p.BatteryPercent);
            if (p.OnAc && _bios != null && ChargerRule.IsWeak(_bios.ReadCharger()))
            {
                System.Threading.Thread.Sleep(700);
                p.ChargerWeak = ChargerRule.IsWeak(_bios.ReadCharger());   // must be weak twice in a row
            }
            return p;
        }
    }

    /// <summary>--demo / tests: a power source the caller sets.</summary>
    public sealed class SimPowerSource : IPowerSource
    {
        public PowerSnapshot Current = new PowerSnapshot();
        public SimPowerSource() { Current.OnAc = true; Current.BatteryPercent = 100; }
        public PowerSnapshot Read() { return Current; }
    }

    /// <summary>Listens for WM_POWERBROADCAST on a hidden top-level window (broadcasts do not reach message-only windows).
    /// Debounces 2 s; raises StatusChanged for AC/DC or battery changes and Resumed after sleep/hibernate. No polling.</summary>
    public sealed class PowerWatcher : NativeWindow, IDisposable
    {
        private const int WM_POWERBROADCAST = 0x218;
        private const int PBT_APMRESUMESUSPEND = 0x7;
        private const int PBT_APMPOWERSTATUSCHANGE = 0xA;
        private const int PBT_APMRESUMEAUTOMATIC = 0x12;

        private readonly Timer _settle = new Timer();
        private bool _resumePending;

        public event EventHandler StatusChanged;
        public event EventHandler Resumed;

        public PowerWatcher()
        {
            _settle.Interval = 2000;
            _settle.Tick += OnSettle;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_POWERBROADCAST)
            {
                int ev = m.WParam.ToInt32();
                if (ev == PBT_APMRESUMEAUTOMATIC || ev == PBT_APMRESUMESUSPEND) { _resumePending = true; Restart(); }
                else if (ev == PBT_APMPOWERSTATUSCHANGE) Restart();
            }
            base.WndProc(ref m);
        }

        private void Restart() { _settle.Stop(); _settle.Start(); }

        private void OnSettle(object sender, EventArgs e)
        {
            _settle.Stop();
            EventHandler h;
            if (_resumePending) { _resumePending = false; h = Resumed; }
            else h = StatusChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _settle.Dispose();
            DestroyHandle();
        }
    }

    /// <summary>Windows 11 power mode, as OGH's SyncWindowsPowerMode does: Eco = Best power efficiency, Balanced = Balanced,
    /// Unleashed = Best performance; sets the AC or DC slot for the current power source (on every mode change and every
    /// plug or unplug, so the slot in use always matches the mode).</summary>
    public interface IPowerModeSetter
    {
        void Set(Mode mode, bool onAc);
    }

    public static class PowerModes
    {
        /// <summary>The same Windows power mode for the charger and for battery (exit, uninstall).</summary>
        public static void SetBoth(IPowerModeSetter w, Mode m) { w.Set(m, true); w.Set(m, false); }
    }

    public sealed class NullPowerMode : IPowerModeSetter
    {
        public Mode Last = Mode.Balanced, Ac = Mode.Balanced, Dc = Mode.Balanced;   // Ac / Dc: the two slots Windows keeps
        public void Set(Mode mode, bool onAc) { Last = mode; if (onAc) Ac = mode; else Dc = mode; }
    }

    public sealed class WindowsPowerMode : IPowerModeSetter
    {
        public static readonly Guid BestEfficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid BestPerformance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid mode);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid mode);

        private bool _unavailable;

        public void Set(Mode mode, bool onAc)
        {
            if (_unavailable) return;
            Guid g = mode == Mode.Eco ? BestEfficiency : mode == Mode.Performance ? BestPerformance : Guid.Empty;
            try
            {
                if (onAc) PowerSetUserConfiguredACPowerMode(ref g);
                else PowerSetUserConfiguredDCPowerMode(ref g);
            }
            catch (EntryPointNotFoundException) { _unavailable = true; }   // Windows without the power-mode API
            catch (DllNotFoundException) { _unavailable = true; }
        }
    }
}
