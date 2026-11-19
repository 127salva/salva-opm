// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva
// Portions adapted from Ohman (https://github.com/P4R1H/Ohman), GPL-3.0-or-later.

using System;

namespace SalvaOPM
{
    /// <summary>
    /// Ties the pure rules (ModeController, FanCurve, ThermalGuard, Governor) to the firmware (HpBios), Ohman's way:
    ///  apply = profile -> fans -> power -> GPU preset -> Windows power mode (Ohman Engine.ApplyAll);
    ///  every 5 s: sensors, software fan curve (type 16 + 46 when the level changes and at least every 30 s), governor in Performance;
    ///  every 10 s: thermal guard (fans + ambient sensor); every 45 s: heartbeat re-sends profile and Performance power, ends OGH.
    /// Modes: Eco and Balanced = profile 0x30 + GPU Base / Boost (Ohman presets); Performance = profile 0x04, PL1/PL2 80,
    /// concurrent TDP 82 (verified on this laptop) + GPU Max. Fans: one quiet Auto curve for every mode, plus one custom curve
    /// that only Performance can use. The firmware drives the fans on battery, without a CPU temperature, while an old power
    /// limit lapses, after fan control was lost and when the user chose Firmware for the mode; the thermal guard takes them back
    /// from it while engaged, except from a Firmware choice (FansByBios).
    /// An unexpected error stops all writes for the session (FailSafe).
    /// MonitorOnly (wrong laptop): reads only, never a write.
    /// All calls happen on the caller's thread; 'now' is milliseconds from the caller's clock.
    /// </summary>
    public sealed class Engine
    {
        public const int PerfPl = 80, PerfTdp = 82, LeavePl = 55;
        public const long TickMs = 5000, GuardMs = 10000, HeartbeatMs = 45000, FanRefreshMs = 30000, FansStaleMs = 30000;
        public const int MaxConflicts = 3;
        public const int ChassisSensor = 1;     // Ohman's guard reads 0x23 index 1 (ambient); OGH's chassis rule reads index 0 (IR)

        private readonly HpBios _bios;
        private readonly Settings _s;
        private readonly IPowerModeSetter _win;
        private readonly ISensors _sensors;
        private readonly GovernorConfig _cfg;
        private readonly Action<string> _log;
        private readonly ModeController _ctl = new ModeController();
        private readonly FanCurve _auto = FanCurve.Auto();
        public readonly ThermalGuard Guard = new ThermalGuard();
        public readonly HeatPush Push = new HeatPush();

        /// <summary>Ends OMEN Gaming Hub's background if it ever comes back (set by the app; null in tests and the demo).</summary>
        public Action KillOgh;
        public event Action Changed;

        private bool _onAc = true, _drain, _byBiosSent, _applyFailed;
        private double _battery = 1.0;
        private bool _inPerf;
        private GovernorState _gov;
        private int _currentPl;
        private int _conflicts;
        private int _lastIr = 35;
        private long _nextTick, _nextGuard, _nextHeartbeat;
        private int _lvlCpu = -1, _lvlGpu = -1;
        private long _lastFanWrite = long.MinValue / 2, _fansAt = long.MinValue / 2;
        private double _smCpu = double.NaN, _smGpu = double.NaN;
        private int _cpuGone, _gpuGone, _fanFailures;
        private long _underSince = -1;
        private string _fanLostWhy;
        private byte[] _gpuWritten, _gpuRefused;

        public bool MonitorOnly;               // set before Boot: this is not the validated laptop
        public string MonitorReason;
        public string BiosWarning;             // BIOS version other than the validated one
        public bool Failed;
        public string LastError;
        public bool FanControlLost;            // the firmware refused or ignored levels: its own curve drives the fans
        public bool Halted;                    // an unexpected error: nothing is written until the app restarts
        public int[] Fans;                     // last fan reading, 100 rpm units
        public int Chassis = -1;               // last ambient (index 1) reading

        public Engine(HpBios bios, Settings settings, IPowerModeSetter winMode, ISensors sensors, GovernorConfig cfg, Action<string> log)
        {
            _bios = bios; _s = settings; _win = winMode; _sensors = sensors; _cfg = cfg; _log = log;
        }

        public ModeController Controller { get { return _ctl; } }
        public Settings Settings { get { return _s; } }
        public ISensors Sensors { get { return _sensors; } }
        public Mode Effective { get { return _ctl.Effective; } }
        public bool InPerformance { get { return _inPerf; } }
        public int[] Levels { get { return new int[] { _lvlCpu, _lvlGpu }; } }
        public FanCurve AutoCurve { get { return _auto; } }
        /// <summary>True while the custom curve drives the fans: only Performance can use it (Eco and Balanced choose Recommended or Firmware).</summary>
        public bool CustomFans { get { return _ctl.Effective == Mode.Performance && FanChoice == FanSource.Custom; } }
        /// <summary>The fan choice of the mode in effect (each mode remembers its own).</summary>
        public FanSource FanChoice { get { return _s.Fan[(int)_ctl.Effective]; } }
        /// <summary>The user chose Firmware for this mode: the firmware alone drives the fans, no thermal guard, no 85 C rule.</summary>
        public bool FirmwareFans { get { return FanChoice == FanSource.Firmware; } }
        public FanCurve ActiveCurve { get { return CustomFans ? FanCurve.Custom(_s.Curve) : _auto; } }
        public bool CpuUnknown { get { return double.IsNaN(_smCpu); } }
        /// <summary>Who drives the fans. The firmware does on battery (Ohman FansByBios), without a CPU temperature, after
        /// fan control was lost, in monitoring and when the user chose Firmware; the thermal guard takes them whenever it is
        /// engaged, except from a Firmware choice.</summary>
        public bool FansByBios { get { return FanControlLost || MonitorOnly || FirmwareFans || ((!_onAc || CpuUnknown || Lapsing) && !Guard.Active); } }
        public bool FansFresh(long now) { return Fans != null && now - _fansAt <= FansStaleMs; }

        /// <summary>An extra fan-speed read for the open window (type 45; reads do not refresh the host state: lapses ended
        /// on time while the guard read the fans every 10 s).</summary>
        public void ReadFans(long now)
        {
            int[] f = _bios.ReadFanLevels();
            if (f != null) { Fans = f; _fansAt = now; }
        }
        private byte Profile { get { return _ctl.Effective == Mode.Performance ? HpBios.ProfilePerformance : HpBios.ProfileBalanced; } }

        public static byte[] PresetFor(Mode m) { return m == Mode.Eco ? HpBios.GpuBase : m == Mode.Performance ? HpBios.GpuMax : HpBios.GpuBoost; }
        public static string Name(Mode m) { return m == Mode.Performance ? "Performance" : m.ToString(); }
        public static string GpuName(byte[] r) { return r == null ? "--" : r[0] == 1 ? "max" : r[1] == 1 ? "boost" : "base"; }

        /// <summary>The one thing wrong right now, for the window's warning line; null when all is well.</summary>
        public string Fault
        {
            get
            {
                if (MonitorOnly) return "Monitoring only: " + MonitorReason;
                if (Failed && LastError != null) return LastError;
                if (Guard.Active) return "Thermal guard: fans at maximum";
                if (FanControlLost) return "Fans handed to the firmware: " + _fanLostWhy;
                if (_onAc && CpuUnknown) return "No CPU temperature: the firmware drives the fans";
                if (_fanFailures >= 3) return "Fan writes failing; retrying once a minute";
                if (_gpuRefused != null) return "GPU power preset not accepted by the firmware";
                return BiosWarning;
            }
        }

        /// <summary>Temperatures and fan speeds in one line, for the window and the tray tooltip.</summary>
        public string InfoLine(long now)
        {
            int[] f = FansFresh(now) ? Fans : null;
            return "CPU " + Deg(_sensors.CpuTemp) + "  " + Rpm(f, 0) + " rpm   ·   GPU "
                + (_sensors.GpuAsleep ? "asleep" : Deg(_sensors.GpuTemp)) + "  " + Rpm(f, 1) + " rpm";
        }

        private static string Deg(double t) { return double.IsNaN(t) ? "--" : ((int)Math.Round(t)).ToString() + "°"; }
        private static string Rpm(int[] f, int i) { return f == null || f[i] < 0 ? "--" : (f[i] * 100).ToString(); }

        private void Log(string s) { if (_log != null) _log(s); }
        private void Fire() { Action h = Changed; if (h != null) h(); }
        private bool Fail(string message) { Failed = true; LastError = message; Log("FAILURE: " + message); return false; }
        private void ClearFailure() { Failed = false; LastError = null; }

        public void SetPowerInputs(bool onAc, bool batteryDrain, int batteryPercent)
        {
            _onAc = onAc;
            _drain = batteryDrain;
            _battery = batteryPercent < 0 ? 1.0 : batteryPercent / 100.0;
        }

        // ---------------- events ----------------

        public bool Boot(bool powerOk, long now)
        {
            if (MonitorOnly)
            {
                _ctl.Boot(_s.SavedMode, powerOk);
                Log("monitoring only: " + MonitorReason);
                Fire();
                return true;
            }
            if (Halted) return false;
            RepairTdp();
            // a host limit at start-up was left by a session that ended within the last two minutes (update, crash, kill):
            // the fan keep-alive would hold it for good, so let it lapse first, as after leaving Performance
            PowerData left = _bios.ReadPower();
            if (left != null && (left.Pl1 != 0 || left.Pl2 != 0))
            {
                StartLapse(now);
                Log("start-up: host power limit " + left.Pl1 + "/" + left.Pl2 + " still set; letting it lapse");
            }
            Outcome o = _ctl.Boot(_s.SavedMode, powerOk);
            Log("boot: saved " + _s.SavedMode + ", powerOk " + powerOk + " -> " + o.Effective + (o.Auto ? " (auto)" : ""));
            return Apply(o.Effective, now);
        }

        public bool Select(Mode mode, long now)
        {
            if (MonitorOnly) return Fail("Monitoring only: " + MonitorReason);
            if (Halted) return false;
            Outcome o = _ctl.Select(mode);
            if (o.Rejected) return Fail("Performance needs the charger connected");
            if (o.SaveChanged) { _s.SavedMode = o.SavedMode; _s.Save(); }
            if (!o.Apply) { ClearFailure(); Fire(); return true; }
            return Apply(o.Effective, now);
        }

        public bool PowerChanged(bool powerOk, long now)
        {
            if (MonitorOnly || Halted) return true;
            Outcome o = _ctl.PowerChanged(powerOk);
            if (!o.Apply) { if (FansByBios != _byBiosSent) OwnerChanged(now); return true; }
            Log("power " + (powerOk ? "ok" : "lost") + " -> " + o.Effective + (o.Auto ? " (auto)" : ""));
            return Apply(o.Effective, now);
        }

        public bool Resume(bool powerOk, long now)
        {
            if (MonitorOnly || Halted) return true;
            Outcome o = _ctl.Resume(powerOk);
            Log("resume: powerOk " + powerOk + " -> re-apply " + o.Effective);
            return Apply(o.Effective, now);
        }

        /// <summary>Exit, logoff, shutdown, uninstall (Ohman Park): leave Performance in order, park the fans at a level they can
        /// survive (never lower than now), then hand them to the firmware's own curve. quiet (logoff/shutdown): keep the
        /// current level instead of raising it to the fallback, which the firmware would replay into the next boot.</summary>
        public void Exit(bool quiet)
        {
            if (MonitorOnly || Halted) return;
            if (_inPerf) { LeavePerformance(); _bios.SetProfile(HpBios.ProfileBalanced, FansByBios); ApplyGpu(HpBios.GpuBoost); }   // the GPU record does not lapse by itself
            if (!FansByBios && _lvlCpu > 0)
            {
                int cur = Math.Max(_lvlCpu, _lvlGpu);
                int want = quiet ? cur : Math.Max(_auto.Fallback, cur);
                _bios.WriteFanLevels(want, want);
                Log("parked fans at " + want);
            }
            _bios.SetProfile(HpBios.ProfileBalanced, true);   // Eco, Balanced and a left Performance all end on 0x30
            _byBiosSent = true;
            Log("fans handed to the firmware's own curve");
        }

        /// <summary>An unexpected error: leave Performance, hand fans and power to the firmware, then write nothing more
        /// until the app restarts (host state lapses by itself). Each step is tried on its own.</summary>
        public void FailSafe(string why)
        {
            if (Halted) return;
            Halted = true;
            Log("STOPPED after an unexpected error: " + why);
            if (!MonitorOnly)
            {
                bool wasPerf = _inPerf;
                try { if (wasPerf) LeavePerformance(true); } catch (Exception) { }
                try { _bios.SetProfile(HpBios.ProfileBalanced, true); _byBiosSent = true; } catch (Exception) { }
                try { if (wasPerf) _bios.WriteGpu(HpBios.GpuBoost); } catch (Exception) { }   // the GPU record does not lapse by itself
            }
            _inPerf = false;
            _gov = null;
            Failed = true;
            LastError = "Stopped after an unexpected error; the firmware drives the fans. Restart Salva OPM.";
            Fire();
        }

        // ---------------- applying ----------------

        /// <summary>The profile write always carries who drives the fans right now.</summary>
        private bool SendProfile(byte profile)
        {
            bool by = FansByBios;
            _byBiosSent = by;
            return _bios.SetProfile(profile, by);
        }

        private bool Apply(Mode target, long now)
        {
            // An Eco/Balanced change while an old limit lapses is not sent: on the laptop a profile write in the lapse kept
            // PL 55 alive ("did not lapse", 2026-11-13). The profile is already 0x30 with firmware fans; EndLapse applies it.
            if (Lapsing && target != Mode.Performance && _byBiosSent && !_applyFailed && !Guard.Active)
            {
                _deferred = true;
                _win.Set(target, _onAc);
                ClearFailure();
                Log(Name(target) + " chosen while the old limit lapses; sent when it is gone");
                Fire();
                return true;
            }
            if (_inPerf && target != Mode.Performance) { LeavePerformance(); StartLapse(now); }
            if (target == Mode.Performance) _lapseUntil = -1;
            _deferred = false;
            _sensors.Poll();
            SmoothTemps(true);
            _gpuRefused = null;
            byte profile = target == Mode.Performance ? HpBios.ProfilePerformance : HpBios.ProfileBalanced;
            _applyFailed = !SendProfile(profile);
            if (_applyFailed) { Fire(); return Fail("could not apply " + Name(target) + " (the BIOS did not answer)"); }
            _lvlCpu = _lvlGpu = -1;
            if (Guard.Active && !FirmwareFans) GuardFans(now);   // e.g. a resume while hot: the levels held before sleep are gone
            else if (!FansByBios) FanTick(now, true);
            if (target == Mode.Performance && !EnterPerformance())
            {
                string why = LastError;
                LeavePerformance(true);
                StartLapse(now);
                _ctl.Select(Mode.Balanced);
                SendProfile(HpBios.ProfileBalanced);
                ApplyGpu(HpBios.GpuBoost);
                _win.Set(Mode.Balanced, _onAc);
                Fire();
                return Fail(why + " (back to Balanced)");
            }
            ApplyGpu(_inPerf ? GovernedGpu() : PresetFor(target));
            _win.Set(target, _onAc);
            ClearFailure();
            _nextTick = now + TickMs;
            _nextGuard = now + GuardMs;
            _nextHeartbeat = now + HeartbeatMs;
            LogState(Name(target) + " applied");
            Fire();
            return true;
        }

        private bool EnterPerformance()
        {
            PowerData p = _bios.ReadPower();
            if (p == null) return Fail("could not read power data");
            // baseline = concurrent TDP before the first Performance write; never overwrite it with a left-over 82, and keep
            // only a value HpBios.WriteTdp accepts (anything else would throw when leaving Performance)
            if (p.Tdp != PerfTdp && p.Tdp != _s.BaselineTdp)
            {
                if (p.Tdp >= HpBios.TdpMin && p.Tdp <= HpBios.TdpMax) { _s.BaselineTdp = p.Tdp; _s.Save(); }
                else Log("concurrent TDP " + p.Tdp + " is outside 20..82: not kept as the baseline");
            }
            if (!_bios.WritePowerLimits(PerfPl)) return Fail("PL1/PL2 write failed");
            if (!_bios.WriteTdp(PerfTdp)) return Fail("concurrent TDP write failed");
            PowerData v = _bios.ReadPower();
            if (v == null || v.Pl1 != PerfPl || v.Pl2 != PerfPl || v.Tdp != PerfTdp)
                return Fail(v == null ? "read-back failed" : string.Format("read-back mismatch (PL {0}/{1}, TDP {2})", v.Pl1, v.Pl2, v.Tdp));
            _inPerf = true;
            _currentPl = PerfPl;
            _conflicts = 0;
            _gov = Governor.NewState(_cfg, HpBios.GpuMax[3], HpBios.GpuMax[1] == 1);
            Log("Performance: PL 80/80, TDP 82 verified");
            return true;
        }

        private void LeavePerformance() { LeavePerformance(false); }

        /// <summary>Concurrent TDP back to its baseline while 0x04 is still active (the only time the firmware takes it),
        /// then PL1/PL2 55 W (OGH's Balanced cap; without it the fan keep-alive would hold Performance's 80 W).</summary>
        private void LeavePerformance(bool force)
        {
            if (!_inPerf && !force) return;
            if (_s.BaselineTdp >= 0)
            {
                PowerData p = _bios.ReadPower();
                if (p == null || p.Tdp != _s.BaselineTdp)
                {
                    _bios.SetProfile(HpBios.ProfilePerformance, FansByBios);
                    _bios.WriteTdp(_s.BaselineTdp);
                }
            }
            _bios.WritePowerLimits(LeavePl);
            _inPerf = false;
            _gov = null;
            Log("left Performance");
        }

        /// <summary>Start-up: a crash in Performance can leave concurrent TDP at 82; put the baseline back.</summary>
        private void RepairTdp()
        {
            PowerData p = _bios.ReadPower();
            if (p == null || _s.BaselineTdp < 0 || p.Tdp == _s.BaselineTdp) return;
            _bios.SetProfile(HpBios.ProfilePerformance, true);
            _bios.WriteTdp(_s.BaselineTdp);
            _bios.SetProfile(HpBios.ProfileBalanced, true);
            Log("start-up: concurrent TDP restored to " + _s.BaselineTdp);
        }

        /// <summary>Writes a GPU record if it differs, verified by read-back of cTGP, PPAB and GPS (Ohman always writes dState 1;
        /// this laptop echoes it, evidence section 12, but the check leaves that byte out so a firmware that keeps dState 0
        /// causes no rewrite loop). A record the firmware did not take is not retried until the next mode change. The first
        /// record ever read is kept for uninstall.</summary>
        private bool ApplyGpu(byte[] want)
        {
            byte[] cur = _bios.ReadGpu();
            if (cur != null && _s.FactoryGpu == null) { _s.FactoryGpu = cur; _s.Save(); }
            if (GpuMatches(cur, want) && (cur[2] == want[2] || Same(want, _gpuWritten))) return true;
            if (Same(want, _gpuRefused)) return false;
            _bios.WriteGpu(want);
            byte[] v = _bios.ReadGpu();
            if (GpuMatches(v, want))
            {
                _gpuWritten = want; _gpuRefused = null; Log("GPU record -> " + BitConverter.ToString(want));
                if (_inPerf) { KeepPerfPower(); _perfRechecks = 2; }   // the firmware may change TDP a second later: check again on the next two ticks
                return true;
            }
            _gpuRefused = want;
            Log("GPU power: the firmware did not take " + BitConverter.ToString(want));
            return false;
        }

        /// <summary>Seen on the laptop: about a second after the GPU record goes from Base to Max (entering Performance from
        /// Eco), concurrent TDP reads 45 instead of the verified 82. Checked right after every GPU write in Performance and on
        /// the next two 5 s ticks; written again when changed.</summary>
        private int _perfRechecks;

        private void KeepPerfPower()
        {
            PowerData p = _bios.ReadPower();
            if (p == null || (p.Pl1 == _currentPl && p.Pl2 == _currentPl && p.Tdp == PerfTdp)) return;
            _bios.WritePowerLimits(_currentPl);
            _bios.WriteTdp(PerfTdp);
            PowerData v = _bios.ReadPower();
            bool ok = v != null && v.Pl1 == _currentPl && v.Pl2 == _currentPl && v.Tdp == PerfTdp;
            Log(string.Format("Performance power changed after the GPU write (PL {0}/{1}, TDP {2}); written again{3}", p.Pl1, p.Pl2, p.Tdp, ok ? "" : ", not taken"));
        }

        private static bool GpuMatches(byte[] r, byte[] want)
        {
            return r != null && r.Length == 4 && r[0] == want[0] && r[1] == want[1] && r[3] == want[3];
        }

        private byte[] GovernedGpu()
        {
            byte[] m = HpBios.GpuMax;
            if (_gov == null) return m;
            return new byte[] { m[0], (byte)(_gov.PpabOn ? 1 : 0), m[2], (byte)_gov.Gps };
        }

        // ---------------- ticks ----------------

        /// <summary>Call every few seconds (a 5 s timer is enough); work runs when it is due.</summary>
        public void Tick(long now)
        {
            if (Halted)   // reads only, so the window and tooltip keep showing live values
            {
                if (now < _nextTick) return;
                _nextTick = now + TickMs;
                try { _sensors.Poll(); int[] f = _bios.ReadFanLevels(); if (f != null) { Fans = f; _fansAt = now; } } catch (Exception) { }
                Fire();
                return;
            }
            if (now >= _nextTick)
            {
                _nextTick = now + TickMs;
                _sensors.Poll();
                SmoothTemps(false);
                if (!MonitorOnly)
                {
                    if (Lapsing && now >= _lapseUntil) EndLapse(now);
                    if (FansByBios != _byBiosSent) OwnerChanged(now);
                    else if (!FansByBios && !Guard.Active) FanTick(now, false);
                    if (_inPerf) GovernorStep();
                    if (_inPerf && _perfRechecks > 0) { _perfRechecks--; KeepPerfPower(); }
                }
            }
            if (now >= _nextGuard) { _nextGuard = now + GuardMs; GuardTick(now); }
            if (!MonitorOnly && !Lapsing && now >= _nextHeartbeat)   // nothing is re-sent while the old state lapses
            {
                _nextHeartbeat = now + HeartbeatMs;
                if (_applyFailed) Apply(_ctl.Effective, now);   // the BIOS did not answer last time (e.g. just after sleep): try again
                else Heartbeat(now);
            }
        }

        /// <summary>After Performance the host power limit (55 W) would be held for as long as the fan keep-alive runs, and
        /// the laptop stays hotter than after a fresh start. So for LapseMs nothing that refreshes the firmware's host state is
        /// sent (no fan levels, no type 16, no re-sends): the firmware drives the fans and drops the limit by itself (~110 s).</summary>
        public const long LapseMs = 125000, LapseMaxMs = 180000;
        private long _lapseUntil = -1, _lapseStart;
        public bool Lapsing { get { return _lapseUntil >= 0; } }

        private void StartLapse(long now) { _lapseStart = now; _lapseUntil = now + LapseMs; }

        private void EndLapse(long now)
        {
            if (Guard.Active) { StartLapse(now); return; }   // the guard's fan writes refresh the host state: wait until it ends
            PowerData p = _bios.ReadPower();   // a read does not refresh the host state
            bool gone = p != null && p.Pl1 == 0 && p.Pl2 == 0;
            if (!gone && now - _lapseStart < LapseMaxMs) { _lapseUntil = now + TickMs; return; }
            _lapseUntil = -1;
            LogState(gone ? "host limits lapsed" : "host limits did not lapse");
            if (_deferred) Apply(_ctl.Effective, now);   // the mode chosen during the lapse
        }

        private bool _deferred;

        /// <summary>One line of readings for the log (reads only).</summary>
        private void LogState(string what)
        {
            int[] f = _bios.ReadFanLevels();
            PowerData p = _bios.ReadPower();
            Log(what + ": cpu " + Fmt(_sensors.CpuTemp) + ", gpu " + (_sensors.GpuAsleep ? "asleep" : Fmt(_sensors.GpuTemp))
                + ", fans " + (f == null ? "?" : f[0] * 100 + "/" + f[1] * 100)
                + ", PL " + (p == null ? "?" : p.Pl1 + "/" + p.Pl2 + ", TDP " + p.Tdp));
        }

        /// <summary>Ohman's smoothing of the two chip temperatures. A sleeping GPU has no temperature (it is cool by definition).</summary>
        private void SmoothTemps(bool immediate)
        {
            double cpu = _sensors.CpuTemp, gpu = _sensors.GpuAsleep ? double.NaN : _sensors.GpuTemp;
            if (immediate) { _smCpu = cpu; _smGpu = gpu; _cpuGone = _gpuGone = 0; return; }
            _smCpu = FanCurve.Smooth(cpu, _smCpu, ref _cpuGone);
            _smGpu = _sensors.GpuAsleep ? double.NaN : FanCurve.Smooth(gpu, _smGpu, ref _gpuGone);
        }

        /// <summary>The fans changed hands (plug/unplug without a mode change, CPU temperature lost or back).</summary>
        private void OwnerChanged(long now)
        {
            SendProfile(Profile);
            Log(_byBiosSent ? "fans to the firmware" + (CpuUnknown && _onAc ? " (no CPU temperature: " + _sensors.CpuStatus + ")" : "") : "fans back to the app's curve");
            if (!_byBiosSent && !Guard.Active) { _lvlCpu = _lvlGpu = -1; FanTick(now, true); }
            Fire();
        }

        /// <summary>One step of the software curve (Ohman AutoTick).</summary>
        private void FanTick(long now, bool immediate)
        {
            if (_fanFailures >= 3 && now - _lastFanWrite < 60000) return;   // back off; retry once a minute
            FanCurve curve = ActiveCurve;
            int[] target = curve.Target(_smCpu, _smGpu);
            bool was = Push.Active;
            bool push = Push.Tick(_smCpu, now);   // CPU at 85 C or more for a minute: full speed until it is under 80
            if (push != was) Log(push ? "CPU above 85 C for a minute: fans to maximum" : "CPU back under 80 C: fans back to the curve");
            if (push) target = new int[] { HpBios.FanMax, HpBios.FanMax };
            int n1 = immediate || push ? target[0] : curve.Step(_lvlCpu, target[0]);
            int n2 = immediate || push ? target[1] : curve.Step(_lvlGpu, target[1]);
            bool changed = n1 != _lvlCpu || n2 != _lvlGpu;
            bool refresh = now - _lastFanWrite >= FanRefreshMs;          // keep-alive even when steady
            if (changed && !immediate && !refresh && _lvlCpu > 0 && n1 <= _lvlCpu && n2 <= _lvlGpu
                && Math.Abs(n1 - _lvlCpu) < FanCurve.Deadband && Math.Abs(n2 - _lvlGpu) < FanCurve.Deadband) changed = false;
            if (!changed && !refresh) return;
            WriteLevels(n1, n2, now);   // routine steps are not logged: they would push the rare, important lines out of the log
        }

        private static string Fmt(double v) { return double.IsNaN(v) ? "?" : v.ToString("0"); }

        private bool WriteLevels(int cpu, int gpu, long now)
        {
            cpu = Math.Max(HpBios.FanMin, Math.Min(HpBios.FanMax, cpu));
            gpu = Math.Max(HpBios.FanMin, Math.Min(HpBios.FanMax, gpu));
            bool ok = _bios.WriteFanLevels(cpu, gpu);
            _lastFanWrite = now;
            if (ok) { _lvlCpu = cpu; _lvlGpu = gpu; _fanFailures = 0; return true; }
            _fanFailures++;
            if (_fanFailures >= 8) GiveUpFans("the firmware refused fan levels " + _fanFailures + " times");
            else if (_fanFailures == 3) Log("fan writes failing; retrying once a minute");
            return false;
        }

        /// <summary>Software fan control is over for this session: the firmware's own curve takes the fans.</summary>
        private void GiveUpFans(string why)
        {
            if (FanControlLost) return;
            FanControlLost = true;
            _fanLostWhy = why;
            SendProfile(Profile);
            _lvlCpu = _lvlGpu = -1;
            Log("fan control handed to the firmware: " + why);
            Fire();
        }

        /// <summary>Ohman GuardTick, plus a watch that the fans follow what is written (a fan reading far below the level
        /// written for a minute means the writes are not reaching the fans: hand them back to the firmware).</summary>
        private void GuardTick(long now)
        {
            int[] f = _bios.ReadFanLevels();
            if (f != null) { Fans = f; _fansAt = now; }
            Chassis = _bios.ReadSensor(ChassisSensor);
            if (MonitorOnly) { Fire(); return; }
            if (FirmwareFans)   // the user chose the firmware alone: no guard, no fan watch (readings still shown)
            {
                if (Guard.Active) { Guard.Reset(); Log("thermal guard stood down: Firmware chosen"); }
                Fire();
                return;
            }
            bool changed = Guard.Tick(_sensors.CpuTemp, Chassis, f, now);
            if (changed && Guard.Active)
            {
                Log("THERMAL GUARD engaged: cpu " + Fmt(_sensors.CpuTemp) + ", ambient " + Chassis + (Guard.Stalled ? ", fans stalled" : ""));
                GuardFans(now);
            }
            else if (Guard.Active)
            {
                GuardFans(now);
                if (Guard.IgnoredTicks == 3) GiveUpFans("the fans are not answering the thermal guard; save your work and restart");
            }
            else if (changed)
            {
                Log("thermal guard released");
                _lvlCpu = _lvlGpu = -1;
                SendProfile(Profile);
                if (Lapsing) StartLapse(now);   // that profile write refreshed the old limit: give it the full time again
                if (!FansByBios) FanTick(now, true);
            }
            WatchFans(now);
            Fire();
        }

        private void GuardFans(long now)
        {
            if (FanControlLost) return;
            if (_byBiosSent) SendProfile(Profile);      // on battery or without a CPU temperature the guard takes the fans from the firmware
            WriteLevels(HpBios.FanMax, HpBios.FanMax, now);
        }

        private void WatchFans(long now)
        {
            bool under = !FansByBios && Fans != null
                && ((_lvlCpu >= 25 && Fans[0] < _lvlCpu - 10) || (_lvlGpu >= 25 && Fans[1] < _lvlGpu - 10));   // either fan
            if (!under) { _underSince = -1; return; }
            if (_underSince < 0) { _underSince = now; return; }
            if (now - _underSince >= 60000) GiveUpFans("the fans stayed far below the level written for a minute");
        }

        /// <summary>Ohman Heartbeat: pin the profile and Performance's power; end OGH again if it came back.
        /// In Performance a value found changed counts as a conflict (another tool writing): bounded, then Balanced.</summary>
        private void Heartbeat(long now)
        {
            if (_inPerf)
            {
                PowerData p = _bios.ReadPower();
                if (p == null) { Fail("power read failed"); return; }
                bool drifted = p.Pl1 != _currentPl || p.Pl2 != _currentPl || p.Tdp != PerfTdp;
                if (!drifted) _conflicts = 0;
                else
                {
                    _conflicts++;
                    Log(string.Format("drift detected (PL {0}/{1}, TDP {2}), attempt {3}", p.Pl1, p.Pl2, p.Tdp, _conflicts));
                    if (_conflicts > MaxConflicts)
                    {
                        LeavePerformance(true);
                        StartLapse(now);
                        _ctl.Select(Mode.Balanced);
                        SendProfile(Profile);
                        ApplyGpu(HpBios.GpuBoost);
                        _win.Set(Mode.Balanced, _onAc);
                        Fail("another tool keeps changing the firmware (is OMEN Gaming Hub running?); switched to Balanced");
                        Fire();
                        if (KillOgh != null) KillOgh();
                        return;
                    }
                }
            }
            SendProfile(Profile);
            if (_inPerf) { _bios.WritePowerLimits(_currentPl); _bios.WriteTdp(PerfTdp); }
            if (KillOgh != null) KillOgh();
        }

        private void GovernorStep()
        {
            if (_gov.Tick % _cfg.IrCycleTicks == 0)          // the next step evaluates the chassis rule
            {
                int ir = _bios.ReadSensor(0);
                if (ir >= 0) _lastIr = ir;
            }
            double cpu = _sensors.CpuTemp;
            int thr = _gov.Pl1 < _cfg.Pl1Max ? _bios.ReadThrottling() : 0;
            GovernorInput i = new GovernorInput();
            i.OnAc = _onAc;
            i.BatteryDrain = _drain;
            i.BatteryPercent = _battery;
            i.GpuUsage = double.IsNaN(_sensors.GpuLoad) ? 100 : (int)_sensors.GpuLoad;   // unknown: treat the GPU as busy (stricter)
            i.IrTemp = _lastIr;
            i.Throttling = thr == 1 || (!double.IsNaN(cpu) && cpu >= 90);            // OGH CpuTemperatureThreshold 90
            GovernorOutput o = Governor.Step(_cfg, _gov, i);
            if (o.Pl1 != _currentPl && _bios.WritePowerLimits(o.Pl1))
            {
                PowerData v = _bios.ReadPower();
                if (v != null && v.Pl1 == o.Pl1 && v.Pl2 == o.Pl1) { Log("governor: PL1/PL2 " + _currentPl + " -> " + o.Pl1); _currentPl = o.Pl1; }
            }
            ApplyGpu(GovernedGpu());
        }

        // ---------------- settings from the window ----------------

        /// <summary>The fan choice of the mode in effect: Auto or Firmware in every mode, Custom only in Performance.
        /// Handing the fans over (or back) sends the profile; during a lapse the firmware has them already and nothing is sent.</summary>
        public void SetFanMode(FanSource source, long now) { SetFanMode(_ctl.Effective, source, now); }

        /// <summary>The fan choice for mode (the mode on screen when it was clicked). For a mode not in effect yet (its mode
        /// click is still waiting) it is only saved; that mode uses it when applied.</summary>
        public void SetFanMode(Mode mode, FanSource source, long now)
        {
            if (mode != _ctl.Effective)
            {
                if (MonitorOnly || Halted || (source == FanSource.Custom && mode != Mode.Performance) || _s.Fan[(int)mode] == source) return;
                _s.Fan[(int)mode] = source;
                _s.Save();
                Log(Name(mode) + " fans: " + FanLabels.Of(source).ToLowerInvariant());
                Fire();
                return;
            }
            if (MonitorOnly || Halted) return;
            if (source == FanSource.Custom && _ctl.Effective != Mode.Performance) return;
            if (source == FanChoice) return;
            _s.Fan[(int)_ctl.Effective] = source;
            _s.Save();
            Log(Name(_ctl.Effective) + " fans: " + FanLabels.Of(source).ToLowerInvariant());
            if (FansByBios != _byBiosSent) OwnerChanged(now);
            else if (!FansByBios && !Guard.Active) FanTick(now, true);
            Fire();
        }

        /// <summary>The one custom curve (seven levels, 19..48, never falling). Refused when invalid.</summary>
        public bool SetCurve(int[] levels, long now)
        {
            if (Halted || !Settings.ValidCurve(levels)) return false;
            _s.Curve = (int[])levels.Clone();
            _s.Save();
            if (CustomFans && !FansByBios && !Guard.Active) FanTick(now, true);
            Fire();
            return true;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
