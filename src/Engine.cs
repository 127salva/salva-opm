// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva
// Portions adapted from Ohman (https://github.com/P4R1H/Ohman), GPL-3.0-or-later.

using System;

namespace SalvaOPM
{
    /// <summary>
    /// Ties the pure rules (ModeController, Governor) to the firmware (HpBios), Ohman's way:
    ///  apply = profile -> power -> GPU preset -> Windows power mode (Ohman Engine.ApplyAll, without the fan step);
    ///  every 5 s: sensors, governor in Performance; every 45 s: heartbeat re-sends profile and Performance power, ends OGH.
    /// Modes: Eco and Balanced = profile 0x30 + GPU Base / Boost (Ohman presets); Performance = profile 0x04, PL1/PL2 80,
    /// concurrent TDP 82 (verified on this laptop) + GPU Max. The fans are always the firmware's: every profile write says
    /// so (byte 2 = 1) and the app never writes fan levels; it only reads their speed for the window.
    /// An unexpected error stops all writes for the session (FailSafe).
    /// All calls happen on the caller's thread; 'now' is milliseconds from the caller's clock.
    /// </summary>
    public sealed class Engine
    {
        public const int PerfPl = 80, PerfTdp = 82, LeavePl = 55;
        public const long TickMs = 5000, HeartbeatMs = 45000, FansStaleMs = 30000;
        public const int MaxConflicts = 3;

        private readonly HpBios _bios;
        private readonly Settings _s;
        private readonly IPowerModeSetter _win;
        private readonly ISensors _sensors;
        private readonly GovernorConfig _cfg;
        private readonly Action<string> _log;
        private readonly ModeController _ctl = new ModeController();

        /// <summary>Ends OMEN Gaming Hub's background if it ever comes back (set by the app; null in tests and the demo).</summary>
        public Action KillOgh;
        public event Action Changed;

        private bool _onAc = true, _drain, _profileSent, _applyFailed;
        private double _battery = 1.0;
        private bool _inPerf;
        private GovernorState _gov;
        private int _currentPl;
        private int _conflicts;
        private int _lastIr = 35;
        private long _nextTick, _nextHeartbeat;
        private long _fansAt = long.MinValue / 2;
        private byte[] _gpuWritten, _gpuRefused;

        public string BiosWarning;             // BIOS version other than the validated one
        public bool Failed;
        public string LastError;
        public bool Halted;                    // an unexpected error: nothing is written until the app restarts
        public int[] Fans;                     // last fan reading, 100 rpm units

        public Engine(HpBios bios, Settings settings, IPowerModeSetter winMode, ISensors sensors, GovernorConfig cfg, Action<string> log)
        {
            _bios = bios; _s = settings; _win = winMode; _sensors = sensors; _cfg = cfg; _log = log;
        }

        public ModeController Controller { get { return _ctl; } }
        public ISensors Sensors { get { return _sensors; } }
        public Mode Effective { get { return _ctl.Effective; } }
        public bool InPerformance { get { return _inPerf; } }
        public bool FansFresh(long now) { return Fans != null && now - _fansAt <= FansStaleMs; }

        /// <summary>A fan-speed read for the RPM history, on every 5 s tick (type 45; reads do not refresh the host state:
        /// lapses ended on time while the fans were read every 2 and every 10 s, docs/firmware.md).</summary>
        private void ReadFans(long now)
        {
            int[] f = _bios.ReadFanLevels();
            if (f != null) { Fans = f; _fansAt = now; }
        }
        public static byte ProfileFor(Mode m) { return m == Mode.Performance ? HpBios.ProfilePerformance : HpBios.ProfileBalanced; }
        public static byte[] PresetFor(Mode m) { return m == Mode.Eco ? HpBios.GpuBase : m == Mode.Performance ? HpBios.GpuMax : HpBios.GpuBoost; }
        public static string GpuName(byte[] r) { return r == null ? "--" : r[0] == 1 ? "max" : r[1] == 1 ? "boost" : "base"; }

        /// <summary>The one thing wrong right now, for the window's warning line; null when all is well.</summary>
        public string Fault
        {
            get
            {
                if (Failed && LastError != null) return LastError;
                if (_gpuRefused != null) return "GPU setting not accepted";
                return BiosWarning;
            }
        }

        private void Log(string s) { if (_log != null) _log(s); }
        private void Fire() { Action h = Changed; if (h != null) h(); }
        /// <summary>A failure for the window; logged (with why, when given) only when it is new: a lasting one is retried every
        /// 45 s, and logged each time it would push the start of the trouble out of the log (2 x 256 KB) within days.</summary>
        private bool Fail(string message, string why = null)
        {
            if (!Failed || LastError != message) { if (why != null) Log(why); Log("FAILURE: " + message); }
            Failed = true; LastError = message; return false;
        }
        private void ClearFailure() { Failed = false; LastError = null; }

        public void SetPowerInputs(bool onAc, bool batteryDrain, int batteryPercent)
        {
            _onAc = onAc;
            _drain = batteryDrain;
            _battery = BatteryShare(batteryPercent);
        }

        /// <summary>A battery percentage as the safety rules take it (0..1); unknown counts as full.</summary>
        public static double BatteryShare(int percent) { return percent < 0 ? 1.0 : percent / 100.0; }

        /// <summary>The Performance safety rules' inputs as the app reads them (--dry-run uses the same): an unknown GPU load
        /// counts as busy (stricter); throttling is the firmware's flag or a CPU at 90 °C or more (OGH CpuTemperatureThreshold).</summary>
        public static GovernorInput GovernorInputs(bool onAc, bool drain, double battery, double gpuLoad, int irTemp, int throttleFlag, double cpuTemp)
        {
            GovernorInput i = new GovernorInput();
            i.OnAc = onAc;
            i.BatteryDrain = drain;
            i.BatteryPercent = battery;
            i.GpuUsage = double.IsNaN(gpuLoad) ? 100 : (int)gpuLoad;
            i.IrTemp = irTemp;
            i.Throttling = throttleFlag == 1 || (!double.IsNaN(cpuTemp) && cpuTemp >= 90);
            return i;
        }

        // ---------------- events ----------------

        public bool Boot(bool powerOk, long now)
        {
            if (Halted) return false;
            RepairTdp();
            // a host limit at start-up was left by a session that ended within the last two minutes (update, crash, kill):
            // the heartbeat's re-sends would hold it for good, so let it lapse first, as after leaving Performance
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
            if (Halted) return false;
            Outcome o = _ctl.Select(mode);
            if (o.Rejected) return Fail("Performance needs the charger");
            if (o.SaveChanged) { _s.SavedMode = o.SavedMode; _s.Save(); }
            if (!o.Apply) { ClearFailure(); Fire(); return true; }
            return Apply(o.Effective, now);
        }

        public bool PowerChanged(bool powerOk, long now)
        {
            if (Halted) return true;
            Outcome o = _ctl.PowerChanged(powerOk);
            if (!o.Apply)
            {
                // Windows keeps one power mode for the charger and one for battery: the slot now in use must match the mode
                _win.Set(_ctl.Effective, _onAc);
                return true;
            }
            Log("power " + (powerOk ? "ok" : "lost") + " -> " + o.Effective + (o.Auto ? " (auto)" : ""));
            return Apply(o.Effective, now);
        }

        public bool Resume(bool powerOk, long now)
        {
            if (Halted) return true;
            Outcome o = _ctl.Resume(powerOk);
            Log("resume: powerOk " + powerOk + " -> re-apply " + o.Effective);
            return Apply(o.Effective, now);
        }

        /// <summary>Exit, sign-out, shutdown (Ohman Park, without the fans; uninstall has its own Setup.Restore): leave Performance in order and end on
        /// Balanced: profile 0x30, the GPU on Boost (its record does not lapse by itself, so Eco's or Performance's would stay
        /// after the app is gone) and the Windows power mode Balanced for both power sources.</summary>
        public void Exit()
        {
            if (Halted)
            {
                // the fail safe writes nothing more to the firmware, but the Windows power mode is not the firmware: set it back
                PowerModes.SetBoth(_win, Mode.Balanced);
                return;
            }
            if (_inPerf) LeavePerformance();
            _bios.SetProfile(HpBios.ProfileBalanced);   // Eco, Balanced and a left Performance all end on 0x30
            _profileSent = true;
            ApplyGpu(HpBios.GpuBoost);                 // written only when it differs (from Eco or Performance)
            PowerModes.SetBoth(_win, Mode.Balanced);
            Log("exit: firmware back to profile 0x30, GPU Boost, Windows power mode Balanced");
        }

        /// <summary>An unexpected error: leave Performance, put profile 0x30 back, then write nothing more until the app
        /// restarts (host state lapses by itself). Each step is tried on its own.</summary>
        public void FailSafe(string why)
        {
            if (Halted) return;
            Halted = true;
            Log("STOPPED after an unexpected error: " + why);
            bool wasPerf = _inPerf;
            try { if (wasPerf) LeavePerformance(true); } catch (Exception) { }
            try { _bios.SetProfile(HpBios.ProfileBalanced); _profileSent = true; } catch (Exception) { }
            try { if (wasPerf) _bios.WriteGpu(HpBios.GpuBoost); } catch (Exception) { }   // the GPU record does not lapse by itself
            _inPerf = false;
            _gov = null;
            Failed = true;
            LastError = "Stopped after an error";
            Fire();
        }

        // ---------------- applying ----------------

        private bool SendProfile(byte profile)
        {
            _profileSent = true;
            return _bios.SetProfile(profile);
        }

        private bool Apply(Mode target, long now)
        {
            // An Eco/Balanced change while an old limit lapses is not sent: on the laptop a profile write in the lapse kept
            // PL 55 alive ("did not lapse", 2026-11-13). The profile is already 0x30; EndLapse applies the choice. The first
            // apply of a session still sends its profile once (it hands the fans back if an older version held them).
            if (Lapsing && target != Mode.Performance && _profileSent && !_applyFailed)
            {
                _deferred = true;
                _win.Set(target, _onAc);
                ClearFailure();
                Log(target + " chosen while the old limit lapses; sent when it is gone");
                Fire();
                return true;
            }
            if (_inPerf && target != Mode.Performance) { LeavePerformance(); StartLapse(now); }
            if (target == Mode.Performance) _lapseUntil = -1;
            _deferred = false;
            _sensors.Poll();
            _gpuRefused = null;
            _applyFailed = !SendProfile(ProfileFor(target));
            if (_applyFailed) { Fire(); return Fail(target + " not applied yet", target + ": the BIOS did not answer"); }
            // the window says it simply; the log keeps which step failed
            if (target == Mode.Performance && !EnterPerformance())
                return FallBackToBalanced(now, "Performance did not start: " + LastError, "Performance didn't start");
            ApplyGpu(_inPerf ? GovernedGpu() : PresetFor(target));
            _win.Set(target, _onAc);
            ClearFailure();
            _nextTick = now + TickMs;
            _nextHeartbeat = now + HeartbeatMs;
            LogState(target + " applied");
            Fire();
            return true;
        }

        private bool EnterPerformance()
        {
            PowerData p = _bios.ReadPower();
            if (p == null) return Fail("could not read power data");
            // baseline = concurrent TDP before the first Performance write, kept from then on: the firmware sets 45 about a
            // second after a GPU write, and a Performance left within that second leaves 45 behind (it is not the baseline);
            // never a left-over 82, and only a value HpBios.WriteTdp accepts (anything else would throw when leaving Performance)
            if (_s.BaselineTdp < 0 && p.Tdp != PerfTdp)
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

        /// <summary>Performance cannot go on (it did not start, or another tool keeps changing it): leave it in order and fall
        /// back to Balanced, the old limit left to lapse. The window shows message; the log keeps why.</summary>
        private bool FallBackToBalanced(long now, string why, string message)
        {
            LeavePerformance(true);
            StartLapse(now);
            _ctl.Select(Mode.Balanced);
            SendProfile(HpBios.ProfileBalanced);
            ApplyGpu(HpBios.GpuBoost);
            _win.Set(Mode.Balanced, _onAc);
            Log(why);
            Fail(message);
            Fire();
            return false;
        }

        private void LeavePerformance() { LeavePerformance(false); }

        /// <summary>Concurrent TDP back to its baseline while 0x04 is still active (the only time the firmware takes it),
        /// then PL1/PL2 55 W (OGH's Balanced cap until the host limit lapses).</summary>
        private void LeavePerformance(bool force)
        {
            if (!_inPerf && !force) return;
            if (_s.BaselineTdp >= 0) _bios.RestoreTdp(_s.BaselineTdp);
            _bios.WritePowerLimits(LeavePl);
            _inPerf = false;
            _gov = null;
            Log("left Performance");
        }

        /// <summary>Start-up: a crash in Performance can leave concurrent TDP at 82; put the baseline back.</summary>
        private void RepairTdp()
        {
            if (_s.BaselineTdp < 0 || !_bios.RestoreTdp(_s.BaselineTdp)) return;
            _bios.SetProfile(HpBios.ProfileBalanced);
            Log("start-up: concurrent TDP restored to " + _s.BaselineTdp);
        }

        /// <summary>Writes a GPU record if it differs, verified by read-back of cTGP, PPAB and GPS (Ohman always writes dState 1;
        /// this laptop echoes it, but the check leaves that byte out so a firmware that keeps dState 0
        /// causes no rewrite loop). A record the firmware did not take is not retried until the next mode change. The first
        /// record ever read is kept for uninstall.</summary>
        private bool ApplyGpu(byte[] want)
        {
            byte[] cur = _bios.ReadGpu();
            // only a record uninstall can write back (Settings.Load drops any other): an out-of-range one waits for a later read
            if (_s.FactoryGpu == null && HpBios.IsLegalGpu(cur)) { _s.FactoryGpu = cur; _s.Save(); }
            // already set: nothing outstanding, so a warning left by an earlier failed read-back goes too
            if (GpuMatches(cur, want) && (cur[2] == want[2] || Same(want, _gpuWritten))) { _gpuRefused = null; return true; }
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
            if (now >= _nextTick)
            {
                _nextTick = now + TickMs;
                if (Halted)   // reads only, so the window keeps showing live readings
                {
                    try { _sensors.Poll(); ReadFans(now); } catch (Exception) { }
                    Fire();
                    return;
                }
                _sensors.Poll();
                ReadFans(now);
                if (Lapsing && now >= _lapseUntil) EndLapse(now);
                if (_inPerf) GovernorStep();
                if (_inPerf && _perfRechecks > 0) { _perfRechecks--; KeepPerfPower(); }
                Fire();
            }
            if (!Halted && !Lapsing && now >= _nextHeartbeat)   // nothing is re-sent while the old state lapses
            {
                _nextHeartbeat = now + HeartbeatMs;
                if (_applyFailed) Apply(_ctl.Effective, now);   // the BIOS did not answer last time (e.g. just after sleep): try again
                else Heartbeat(now);
            }
        }

        /// <summary>After Performance the host power limit (55 W) would be held for as long as the heartbeat re-sends the
        /// profile, and the laptop stays hotter than after a fresh start. So for LapseMs nothing that refreshes the firmware's
        /// host state is sent (no heartbeat, mode changes deferred): the firmware drops the limit by itself (~110 s). Then the
        /// limit is read every 5 s until it is gone, at most LapseMaxMs.</summary>
        public const long LapseMs = 125000, LapseMaxMs = 180000;
        private long _lapseUntil = -1, _lapseStart;
        public bool Lapsing { get { return _lapseUntil >= 0; } }

        private void StartLapse(long now) { _lapseStart = now; _lapseUntil = now + LapseMs; }

        private void EndLapse(long now)
        {
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

        private static string Fmt(double v) { return double.IsNaN(v) ? "?" : v.ToString("0"); }

        /// <summary>Ohman Heartbeat: pin the profile and Performance's power; end OGH again if it came back.
        /// In Performance a value found changed counts as a conflict (another tool writing): bounded, then Balanced.</summary>
        private void Heartbeat(long now)
        {
            if (_inPerf)
            {
                PowerData p = _bios.ReadPower();
                bool drifted = p != null && (p.Pl1 != _currentPl || p.Pl2 != _currentPl || p.Tdp != PerfTdp);
                // a failed read still re-sends below: without the refresh the firmware drops Performance in ~2 minutes
                if (p == null) Fail("Couldn't check the power");
                else if (!drifted) _conflicts = 0;
                else
                {
                    _conflicts++;
                    Log(string.Format("drift detected (PL {0}/{1}, TDP {2}), attempt {3}", p.Pl1, p.Pl2, p.Tdp, _conflicts));
                    if (_conflicts > MaxConflicts)
                    {
                        FallBackToBalanced(now, "another tool keeps changing the firmware (is OMEN Gaming Hub running?)", "Another app took over");
                        if (KillOgh != null) KillOgh();
                        return;
                    }
                }
            }
            SendProfile(ProfileFor(_ctl.Effective));
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
            int thr = _gov.Pl1 < _cfg.Pl1Max ? _bios.ReadThrottling() : 0;   // only an increase needs it
            GovernorOutput o = Governor.Step(_cfg, _gov, GovernorInputs(_onAc, _drain, _battery, _sensors.GpuLoad, _lastIr, thr, _sensors.CpuTemp));
            if (o.Pl1 != _currentPl && _bios.WritePowerLimits(o.Pl1))
            {
                PowerData v = _bios.ReadPower();
                if (v != null && v.Pl1 == o.Pl1 && v.Pl2 == o.Pl1) { Log("governor: PL1/PL2 " + _currentPl + " -> " + o.Pl1); _currentPl = o.Pl1; }
            }
            ApplyGpu(GovernedGpu());
        }

        /// <summary>Two byte records with the same values (internal: the self-checks compare with it too).</summary>
        internal static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
