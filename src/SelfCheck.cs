// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.Collections.Generic;
using System.IO;

namespace SalvaOPM
{
    /// <summary>Entry point of out\selftest.exe (build.cmd selftest). The app runs the same checks with --selftest.</summary>
    public static class SelfCheckMain
    {
        public static int Main() { return SelfCheck.Run(); }
    }

    /// <summary>Assert-based self-check: pure rules, the allow-list, the simulator and end-to-end scenarios of the Engine
    /// against the simulator. No hardware is touched. Returns the number of failures.</summary>
    public static class SelfCheck
    {
        private static int _pass, _fail, _dirN;

        private static void Check(bool ok, string name)
        {
            if (ok) _pass++;
            else { _fail++; Console.WriteLine("FAIL  " + name); }
        }

        private static bool Throws(Action a)
        {
            try { a(); return false; }
            catch (Exception) { return true; }
        }

        private static GovernorInput Inp(bool ac, bool drain, double batt, int gpu, int ir, bool thr)
        {
            GovernorInput i = new GovernorInput();
            i.OnAc = ac; i.BatteryDrain = drain; i.BatteryPercent = batt; i.GpuUsage = gpu; i.IrTemp = ir; i.Throttling = thr;
            return i;
        }

        private static GovernorOutput Run(GovernorConfig c, GovernorState s, GovernorInput i, int steps)
        {
            GovernorOutput o = new GovernorOutput();
            for (int k = 0; k < steps; k++) o = Governor.Step(c, s, i);
            return o;
        }

        private static string NewDir()
        {
            // the user's TEMP, not beside the exe: --selftest also runs from Program Files, which is read-only
            string d = Path.Combine(Path.Combine(Path.GetTempPath(), "SalvaOPM-selftest"), (++_dirN).ToString());
            if (Directory.Exists(d)) Directory.Delete(d, true);
            Directory.CreateDirectory(d);
            return d;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool Same(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static int Find(List<string> log, string needle, int from)
        {
            for (int i = from; i < log.Count; i++) if (log[i].IndexOf(needle, StringComparison.Ordinal) >= 0) return i;
            return -1;
        }

        private static int Count(List<string> log, string needle, int from)
        {
            int n = 0;
            for (int i = from; i < log.Count; i++) if (log[i].IndexOf(needle, StringComparison.Ordinal) >= 0) n++;
            return n;
        }

        /// <summary>Firmware writes in the simulator log from 'from' on (profile, GPU, power, fan levels, and the type-16 trigger).</summary>
        private static int Writes(List<string> log, int from)
        {
            return Count(log, "type 26 ", from) + Count(log, "type 34 ", from) + Count(log, "type 41 ", from) + Count(log, "type 46 ", from) + Count(log, "type 16 ", from);
        }

        /// <summary>Advances simulator and engine together in 5 s steps.</summary>
        private static long RunFor(Rig r, long now, long ms)
        {
            for (long t = 0; t < ms; t += 5000) { r.Sim.Advance(5000); now += 5000; r.Core.Tick(now); }
            return now;
        }

        private sealed class Rig
        {
            public SimFirmware Sim;
            public NullPowerMode Win = new NullPowerMode();
            public SimSensors Sens = new SimSensors();
            public string Dir;
            public Settings St;
            public Engine Core;
            public int Kills;
            public Rig() : this(new SimFirmware(), NewDir()) { }
            public Rig(SimFirmware sim, string dir)
            {
                Sim = sim; Dir = dir; St = Settings.Load(dir);
                Core = new Engine(new HpBios(Sim), St, Win, Sens, new GovernorConfig(), null);
                Core.KillOgh = delegate { Kills++; };
            }
            public void Unplug(long now) { Core.SetPowerInputs(false, false, 80); Core.PowerChanged(false, now); }
            public void Plug(long now) { Core.SetPowerInputs(true, false, 80); Core.PowerChanged(true, now); }
        }

        public static int Run()
        {
            _pass = 0; _fail = 0;
            ModeTests();
            GovernorTests();
            PowerRuleTests();
            CurveTests();
            GuardTests();
            IdentityTests();
            FirmwareTests();
            CoreTests();
            SettingsTests();
            EnduranceTests();
            Console.WriteLine("self-check: " + _pass + " passed, " + _fail + " failed");
            return _fail;
        }

        // ---------------------------------------------------------------- pure rules

        private static void ModeTests()
        {
            Outcome o;
            ModeController c;

            c = new ModeController(); o = c.Boot(Mode.Performance, true);
            Check(o.Effective == Mode.Balanced && o.SavedMode == Mode.Balanced, "boot: a saved Performance becomes Balanced");
            c = new ModeController(); o = c.Boot(Mode.Balanced, true);
            Check(o.Effective == Mode.Balanced && !o.Auto && o.Apply, "boot Balanced on AC stays Balanced");
            c = new ModeController(); o = c.Boot(Mode.Eco, true);
            Check(o.Effective == Mode.Eco && !o.Auto, "boot Eco on AC stays Eco");
            c = new ModeController(); o = c.Boot(Mode.Balanced, false);
            Check(o.Effective == Mode.Eco && o.Auto, "boot Balanced on battery -> automatic Eco");
            c = new ModeController(); c.Boot(Mode.Eco, false); o = c.PowerChanged(true);
            Check(c.Effective == Mode.Eco && !o.Apply && !c.AutoEco, "boot manual Eco on battery: replug leaves Eco");

            c = new ModeController(); c.Boot(Mode.Balanced, true);
            o = c.PowerChanged(false);
            Check(o.Effective == Mode.Eco && o.Auto && o.Apply, "Balanced: unplug -> automatic Eco");
            o = c.PowerChanged(true);
            Check(o.Effective == Mode.Balanced && !o.Auto && o.Apply, "automatic Eco: replug -> Balanced");

            c = new ModeController(); c.Boot(Mode.Balanced, true); c.Select(Mode.Performance);
            Check(c.Effective == Mode.Performance, "Performance selectable with power");
            o = c.PowerChanged(false);
            Check(o.Effective == Mode.Eco && o.Apply, "Performance: unplug -> Eco");
            o = c.PowerChanged(true);
            Check(o.Effective == Mode.Balanced, "Performance -> unplug -> replug gives Balanced, never Performance");

            c = new ModeController(); c.Boot(Mode.Balanced, true); c.Select(Mode.Eco);
            o = c.PowerChanged(false);
            Check(!o.Apply && c.Effective == Mode.Eco && !c.AutoEco, "manual Eco: unplug changes nothing");
            o = c.PowerChanged(true);
            Check(!o.Apply && c.Effective == Mode.Eco, "manual Eco: replug stays Eco");

            c = new ModeController(); c.Boot(Mode.Balanced, false);
            o = c.Select(Mode.Performance);
            Check(o.Rejected && !o.Apply && c.Effective == Mode.Eco && c.AutoEco && !c.PerformanceAllowed, "Performance rejected on battery");
            c = new ModeController(); c.Boot(Mode.Balanced, false);
            o = c.Select(Mode.Balanced);
            Check(c.Effective == Mode.Balanced && !c.AutoEco && o.Apply, "battery: manual Balanced overrides automatic Eco");
            o = c.PowerChanged(true);
            Check(!o.Apply && c.Effective == Mode.Balanced, "manual Balanced on battery: replug changes nothing");
            c = new ModeController(); c.Boot(Mode.Balanced, false);
            o = c.Select(Mode.Eco);
            Check(!o.Apply && !c.AutoEco && o.SaveChanged && o.SavedMode == Mode.Eco, "battery: manual Eco makes Eco manual (saved)");
            o = c.PowerChanged(true);
            Check(c.Effective == Mode.Eco && !o.Apply, "manual Eco chosen on battery stays Eco after replug");

            c = new ModeController(); c.Boot(Mode.Eco, true); o = c.Select(Mode.Performance);
            Check(o.SaveChanged && o.SavedMode == Mode.Balanced, "choosing Performance saves Balanced");
            c = new ModeController(); c.Boot(Mode.Balanced, true); o = c.Select(Mode.Eco);
            Check(o.SaveChanged && o.SavedMode == Mode.Eco, "choosing Eco saves Eco");
            c = new ModeController(); c.Boot(Mode.Balanced, true);
            Check(!c.PowerChanged(false).SaveChanged && !c.PowerChanged(true).SaveChanged && c.SavedMode == Mode.Balanced, "unplug/replug never changes the saved mode");
            ModeController before = new ModeController(); before.Boot(Mode.Balanced, true); before.PowerChanged(false);
            ModeController after = new ModeController(); o = after.Boot(before.SavedMode, true);
            Check(o.Effective == Mode.Balanced && !o.Auto, "shutdown during automatic Eco, boot on AC -> Balanced");
            before = new ModeController(); before.Boot(Mode.Balanced, true); before.Select(Mode.Performance);
            after = new ModeController(); o = after.Boot(before.SavedMode, true);
            Check(o.Effective == Mode.Balanced, "shutdown as Performance, boot -> Balanced");
            before = new ModeController(); before.Boot(Mode.Balanced, true); before.Select(Mode.Eco);
            after = new ModeController(); o = after.Boot(before.SavedMode, true);
            Check(o.Effective == Mode.Eco && !o.Auto, "shutdown as manual Eco, boot -> Eco");

            c = new ModeController(); c.Boot(Mode.Balanced, true);
            o = c.Resume(true);
            Check(o.Apply && c.Effective == Mode.Balanced, "resume always re-applies");
            o = c.Resume(false);
            Check(o.Apply && o.Effective == Mode.Eco && o.Auto, "resume with a missed unplug -> automatic Eco");
            c = new ModeController(); c.Boot(Mode.Balanced, true);
            Check(!c.PowerChanged(true).Apply, "same power state is a no-op");
            Check(!c.Select(Mode.Balanced).Apply, "selecting the current mode writes nothing");
        }

        private static void GovernorTests()
        {
            GovernorConfig cfg = new GovernorConfig();
            GovernorState s;
            GovernorOutput o;

            s = Governor.NewState(cfg, 87, true);
            o = Run(cfg, s, Inp(true, false, 1.0, 5, 35, false), 12);
            Check(o.Pl1 == 80 && o.Gps == 87 && o.PpabOn && !s.IrCut, "governor idle: nothing changes");
            s = Governor.NewState(cfg, 87, true);
            o = Run(cfg, s, Inp(true, true, 0.55, 50, 35, false), 10);
            Check(o.Pl1 == 70, "battery 40 %: draining below 60 % with a busy GPU: -1 W per tick");
            bool floorOk = true;
            for (int k = 0; k < 100; k++) { o = Governor.Step(cfg, s, Inp(true, true, 0.55, 50, 35, false)); if (o.Pl1 < 45) floorOk = false; }
            Check(floorOk && o.Pl1 == 45, "battery: PL1 stops at the 45 W floor");
            s = Governor.NewState(cfg, 87, true); s.Pl1 = 60;
            o = Run(cfg, s, Inp(true, true, 0.61, 50, 35, false), 10);
            Check(o.Pl1 == 60, "battery: inside the 3 % band PL1 holds");
            o = Run(cfg, s, Inp(true, true, 0.70, 50, 35, false), 5);
            Check(o.Pl1 == 65, "battery: above the band PL1 recovers +1 W per tick");
            s = Governor.NewState(cfg, 87, true); s.Pl1 = 60;
            o = Run(cfg, s, Inp(true, true, 0.50, 10, 35, false), 1);
            Check(o.Pl1 == 61, "battery rule only acts while the GPU is busy (OGH behaviour)");
            s = Governor.NewState(cfg, 87, true);
            o = Run(cfg, s, Inp(true, false, 1.0, 5, 55, false), 1);
            Check(s.IrCut && o.Gps == 75 && !o.PpabOn && o.Pl1 == 75, "chassis >= 54: GPU target 75, PPAB off, PL1 -5 W");
            o = Run(cfg, s, Inp(true, false, 1.0, 5, 55, false), 6);
            Check(o.Pl1 == 70, "chassis: the next 30 s cycle cuts another 5 W");
            o = Run(cfg, s, Inp(true, false, 1.0, 5, 50, false), 6);
            Check(s.IrCut && o.Gps == 75, "chassis between 49 and 54: hold the reduction");
            o = Run(cfg, s, Inp(true, false, 1.0, 5, 49, false), 6);
            Check(!s.IrCut && o.Gps == 87 && o.PpabOn, "chassis <= 49: release restores the GPU record");
            s = Governor.NewState(cfg, 87, true); s.Pl1 = 60;
            o = Run(cfg, s, Inp(true, false, 1.0, 5, 35, true), 5);
            Check(o.Pl1 == 60, "throttling (BIOS flag or CPU >= 90) blocks increases");
            s = Governor.NewState(cfg, 87, true);
            uint seed = 12345; bool inRange = true;
            for (int k = 0; k < 3000; k++)
            {
                seed = seed * 1664525u + 1013904223u;
                int r = (int)(seed >> 8);
                o = Governor.Step(cfg, s, Inp((r & 1) == 0, (r & 2) == 0, ((r >> 2) % 101) / 100.0, (r >> 9) % 101, 20 + ((r >> 16) % 45), (r & 4) == 0));
                if (o.Pl1 < cfg.Pl1Floor || o.Pl1 > cfg.Pl1Max || (o.Gps != 87 && o.Gps != cfg.GpsMin)) inRange = false;
            }
            Check(inRange, "3000 random steps: PL1 stays in 45..80 and the GPU target is only 87 or 75");
        }

        private static void PowerRuleTests()
        {
            DrainTracker d = new DrainTracker();
            Check(!d.Update(true, false, 100), "drain: first sample is never draining");
            Check(d.Update(true, false, 99), "drain: percentage fell on AC -> draining");
            Check(!d.Update(true, false, 100), "drain: percentage rose -> not draining");
            d.Update(true, false, 98);
            Check(!d.Update(true, true, 97), "drain: a power-source change resets it");
            Check(!ChargerRule.IsWeak(new byte[] { 1, 0xAE, 0, 9 }), "charger: this laptop's reply (MeetsRequirement) is normal");
            Check(ChargerRule.IsWeak(new byte[] { 2, 0, 0, 0 }), "charger: BelowRequirement is weak");
            Check(ChargerRule.IsWeak(new byte[] { 5, 0, 20, 12 }), "charger: USB-C 60 W below a 100 W design is weak");
            Check(!ChargerRule.IsWeak(null) && !ChargerRule.IsWeak(new byte[] { 0, 0, 0, 0 }), "charger: unreadable / NotSupported fail open");
            FlapGuard f = new FlapGuard();
            Check(f.Allow(0) && f.Allow(1000) && f.Allow(2000) && !f.Allow(3000) && f.Allow(40000), "flap guard: a 4th change within 30 s waits");
        }

        private static void CurveTests()
        {
            FanCurve a = FanCurve.Auto();
            Check(Same(a.CpuLevels, new int[] { 19, 21, 24, 29, 34, 42, 48 }) && Same(a.GpuLevels, a.CpuLevels)
                && Same(a.CpuTemps, new int[] { 60, 65, 70, 75, 80, 85, 90 }), "Auto curve: the quiet table, 1900 rpm at 60 C to 4800 at 90 C");
            Check(a.Fallback == 35 && a.Ceiling == 48 && a.Floor == 19 && a.Linked, "Auto curve: fallback 35, writes capped to 19..48, both fans linked");
            Check(Same(a.Target(double.NaN, double.NaN), new int[] { 35, 35 }), "Auto: no temperature at all -> fallback level on both fans");
            Check(Same(a.Target(90, 30), new int[] { 48, 48 }) && Same(a.Target(30, 85), new int[] { 42, 42 }), "Auto: both fans follow the hotter chip");
            Check(Same(a.Target(double.NaN, 70), new int[] { 24, 24 }), "Auto: GPU only (CPU unknown) drives both");
            Check(Same(a.Target(40, 40), new int[] { 19, 19 }) && Same(a.Target(75, 40), new int[] { 29, 29 }) && Same(a.Target(80, 40), new int[] { 34, 34 }),
                "Auto: 1900 rpm up to 60 C, 2900 at 75 C, 3400 at 80 C");
            bool bounds = true;
            for (int t = -20; t <= 150; t += 3) { int[] v = a.Target(t, 150 - t); if (v[0] < 19 || v[0] > 48 || v[1] < 19 || v[1] > 48) bounds = false; }
            Check(bounds, "every target stays inside 19..48 (never 0..18, never above the fan table)");
            Check(a.Step(-1, 40) == 25 && a.Step(25, 40) == 28 && a.Step(39, 40) == 40 && a.Step(40, 19) == 37, "step: at most 3 levels per tick; from unknown, the floor quickly");

            Check(Same(FanCurve.CustomTemps, new int[] { 60, 65, 70, 75, 80, 85, 90 }) && Same(FanCurve.DefaultCustom, new int[] { 24, 24, 24, 29, 31, 34, 34 }),
                "custom curve: seven points at 60..90 C, default = the old OGH curve");
            FanCurve c = FanCurve.Custom(FanCurve.DefaultCustom);
            Check(Same(c.Target(85, 45), new int[] { 34, 24 }), "custom: one curve, each fan from its own chip (CPU 85 -> 34, GPU 45 -> 24)");
            Check(Same(c.Target(45, 85), new int[] { 24, 34 }), "custom: a hot GPU raises only the GPU fan");
            Check(Same(c.Target(20, 59), new int[] { 24, 24 }) && Same(c.Target(95, 120), new int[] { 34, 34 }), "custom: below 60 C the 60 C value, above 90 C the 90 C value");
            Check(Same(c.Target(85, double.NaN), new int[] { 34, 34 }), "custom: with the GPU asleep or unknown both fans follow the CPU (safe)");
            Check(c.StepPerTick == 3 && c.Floor == 19 && c.Ceiling == 48, "custom: fixed ramp (3 levels per 5 s) and no extra floor");

            int gone = 0;
            double v1 = FanCurve.Smooth(60, 50, ref gone), v2 = FanCurve.Smooth(40, 60, ref gone);
            Check(v1 == 60 && Math.Abs(v2 - 52) < 0.001, "smoothing: rises at once, falls 40 % per tick");
            double s = FanCurve.Smooth(double.NaN, 50, ref gone); s = FanCurve.Smooth(double.NaN, s, ref gone);
            Check(s == 50 && double.IsNaN(FanCurve.Smooth(double.NaN, s, ref gone)), "smoothing: two missing readings keep the value, the third makes it unknown");
        }

        private static void GuardTests()
        {
            ThermalGuard g = new ThermalGuard();
            int[] fans = { 30, 30 };
            Check(!g.Tick(96, 40, fans, 0) && !g.Active, "guard: one hot reading is not enough");
            Check(g.Tick(96, 40, fans, 10000) && g.Active, "guard: two hot readings in a row engage (CPU >= 95)");
            g = new ThermalGuard(); g.Tick(50, 62, fans, 0); g.Tick(50, 62, fans, 10000);
            Check(g.Active, "guard: chassis >= 62 engages");
            g = new ThermalGuard(); g.Tick(80, 40, new int[] { 4, 4 }, 0); g.Tick(80, 40, new int[] { 4, 4 }, 10000);
            Check(g.Active && g.Stalled, "guard: warm CPU with stalled fans engages");
            g = new ThermalGuard(); g.Tick(96, 40, fans, 0); g.Tick(96, 40, fans, 10000);
            g.Tick(70, 40, fans, 20000); g.Tick(70, 40, fans, 30000);
            Check(g.Active, "guard: holds while the safe minute runs");
            g.Tick(88, 40, fans, 40000);
            g.Tick(70, 40, fans, 80000);
            Check(!g.Active, "guard: one warm sample does not restart the minute; released after 60 s safe");
            g = new ThermalGuard(); g.Tick(96, 40, fans, 0); g.Tick(96, 40, fans, 10000);
            g.Tick(70, 40, fans, 20000); g.Tick(88, 40, fans, 30000); g.Tick(88, 40, fans, 40000); g.Tick(70, 40, fans, 50000); g.Tick(70, 40, fans, 90000);
            Check(g.Active, "guard: two warm samples in a row restart the minute");
            g = new ThermalGuard(); g.Tick(double.NaN, -1, null, 0); g.Tick(double.NaN, -1, null, 10000);
            Check(!g.Active, "guard: no readings at all never engages by itself");
        }

        private static void IdentityTests()
        {
            Check(Identity.WritesAllowed("OMEN Gaming Laptop 16-ap0xxx", "8E35"), "identity: this laptop (16-ap0xxx, 8E35) may write");
            Check(!Identity.WritesAllowed("OMEN Gaming Laptop 16-ap0xxx", "8D2F") && !Identity.WritesAllowed("OMEN Transcend 14", "8E35"), "identity: other board or other model is blocked");
            Check(!Identity.WritesAllowed(null, "8E35") && !Identity.WritesAllowed("", "") && !Identity.WritesAllowed("OMEN Gaming Laptop 16-ap0xxx", null), "identity: unknown values are blocked");
            Check(Identity.BiosWarning("F.13") == null && Identity.BiosWarning("F.14") != null && Identity.BiosWarning("") != null, "identity: only BIOS F.13 is without a warning");
        }

        // ---------------------------------------------------------------- firmware layer

        private static void FirmwareTests()
        {
            bool exact = true;
            int[] ins = { 0, 1, 4, 128 }, outs = { 0, 4, 128, 1024 };
            for (int t = 0; t < 256; t++)
                foreach (int i in ins) foreach (int o in outs)
                    {
                        bool omen = (t == 16 || t == 26 || t == 33 || t == 34 || t == 35 || t == 41) ? i == 4 && o == 4
                            : (t == 45 || t == 53) ? i == 4 && o == 128 : t == 42 ? i == 0 && o == 128 : t == 46 ? i == 128 && o == 4 : false;
                        if (FirmwareRules.IsAllowed(0x20008, t, i, o) != omen) exact = false;
                        if (FirmwareRules.IsAllowed(1, t, i, o) != (t == 15 && i == 4 && o == 4)) exact = false;
                        if (FirmwareRules.IsAllowed(2, t, i, o) || FirmwareRules.IsAllowed(3, t, i, o) || FirmwareRules.IsAllowed(0x20009, t, i, o)) exact = false;
                    }
            Check(exact, "allow-list: exactly the pairs the app uses, with exact sizes; nothing else for any type, size or command");
            Check(!FirmwareRules.IsAllowed(1, 0x52, 4, 4) && !FirmwareRules.IsAllowed(2, 0x52, 4, 4) && !FirmwareRules.IsAllowed(0x20008, 40, 4, 128) && !FirmwareRules.IsAllowed(0x20008, 39, 1, 4),
                "allow-list: graphics mode (0x52), system data (40) and max fan (39) are not used");

            SimFirmware sim = new SimFirmware();
            HpBios b = new HpBios(sim);
            Check(Throws(() => b.WriteFanLevels(18, 30)) && Throws(() => b.WriteFanLevels(30, 49)) && Throws(() => b.WriteFanLevels(0, 0)), "range: fan levels outside 19..48 throw (never 0..18)");
            Check(Throws(() => b.WritePowerLimits(81)) && Throws(() => b.WriteTdp(83)), "range: PL above 80 or TDP above 82 throw");
            Check(Throws(() => b.WriteGpu(new byte[] { 2, 1, 1, 87 })) && Throws(() => b.WriteGpu(new byte[] { 1, 1, 1, 88 })) && Throws(() => b.WriteGpu(new byte[] { 0, 0, 1, 74 })), "range: GPU record outside the presets' ranges throws");
            Check(b.WriteGpu(HpBios.GpuBase) && b.WriteGpu(HpBios.GpuBoost) && b.WriteGpu(HpBios.GpuMax), "range: all three Ohman presets are writable");
            Check(Throws(() => b.SetProfile(0x31, false)) && Throws(() => b.ReadSensor(4)), "range: unknown profile or sensor throws");
            Check(Throws(() => sim.Execute(0x20008, 39, new byte[1], 4)) && Throws(() => sim.Execute(2, 0x52, new byte[4], 4)), "simulator enforces the same allow-list");

            int mark = sim.Log.Count;
            b.SetProfile(HpBios.ProfileBalanced, false);
            b.WriteFanLevels(30, 25);
            int i16 = Find(sim.Log, "type 16", mark), i46 = Find(sim.Log, "type 46", mark);
            Check(i16 > 0 && i46 == i16 + 1 && Same(b.ReadFanLevels(), new int[] { 30, 25 }), "fan write = type 16 then type 46; the levels read back");
            sim.Advance(100000);
            Check(Same(b.ReadFanLevels(), new int[] { 30, 25 }), "sim: levels held 100 s after the write");
            sim.Advance(20000);
            Check(Same(b.ReadFanLevels(), new int[] { 21, 19 }), "sim: without a refresh the firmware's own curve returns after ~110 s");
            b.SetProfile(HpBios.ProfileBalanced, true);
            b.WriteFanLevels(40, 40);
            Check(Same(b.ReadFanLevels(), new int[] { 21, 19 }), "sim: with fans-by-BIOS set, levels are ignored");
            Check(b.SetProfile(HpBios.ProfileBalanced, false) && b.WriteTdp(55) && b.ReadPower().Tdp == 30, "sim: concurrent TDP is ignored outside 0x04");
            Check(b.SetProfile(HpBios.ProfilePerformance, false) && b.WriteTdp(82) && b.ReadPower().Tdp == 82, "sim: concurrent TDP applies under 0x04");

            FwReply ro = new WmiFirmware(true, null).Execute(FirmwareRules.Omen, 16, new byte[4], 4);
            Check(!ro.Ok && ro.Error != null && ro.Error.StartsWith("read-only", StringComparison.Ordinal),
                "read-only mode (--read, --dry-run) refuses type 16, which arms host fan control, before anything is sent");
        }

        // ---------------------------------------------------------------- end-to-end (Engine + simulator)

        private static void CoreTests()
        {
            Rig r = new Rig();
            Check(r.Core.Boot(true, 0) && r.Sim.Profile == 0x30 && !r.Sim.FansByBios && r.Core.Effective == Mode.Balanced && r.Win.Last == Mode.Balanced,
                "boot Balanced on AC: profile 0x30 with software fan control");
            Check(Same(r.Sim.Gpu, HpBios.GpuBoost) && Same(r.St.FactoryGpu, new byte[] { 0, 1, 0, 87 }) && Same(Settings.Load(r.Dir).FactoryGpu, new byte[] { 0, 1, 0, 87 }),
                "Balanced = GPU Boost; the record read first is kept for uninstall");
            int a = Find(r.Sim.Log, "type 26 in FF-30-00-00", 0), f = Find(r.Sim.Log, "type 46", 0), gw = Find(r.Sim.Log, "type 34", 0);
            Check(a >= 0 && f > a && gw > f, "apply order is Ohman's: profile -> fans -> GPU");
            Check(r.Sim.HeldCpu == 19 && r.Sim.HeldGpu == 19 && r.Core.Fault == null, "Auto curve written at boot: 1900 rpm at 48 C; no fault shown");

            Check(r.Core.Select(Mode.Eco, 0) && Same(r.Sim.Gpu, HpBios.GpuBase) && r.Sim.Profile == 0x30 && r.Win.Last == Mode.Eco, "Eco: profile 0x30, GPU Base, Windows efficiency");

            int mark = r.Sim.Log.Count;
            Check(r.Core.Select(Mode.Performance, 0) && r.Core.InPerformance && r.Sim.Profile == 0x04 && r.Sim.HostPl1 == 80 && r.Sim.HostPl2 == 80 && r.Sim.Tdp == 82
                && Same(r.Sim.Gpu, HpBios.GpuMax) && r.St.BaselineTdp == 30 && r.Win.Last == Mode.Performance,
                "Performance: 0x04, PL 80/80, TDP 82 verified, GPU Max, Windows best performance, baseline TDP 30 saved");
            int p26 = Find(r.Sim.Log, "type 26 in FF-04-00-00", mark), p46 = Find(r.Sim.Log, "type 46", mark), pPl = Find(r.Sim.Log, "type 41 in 50-50-FF-FF", mark),
                pTdp = Find(r.Sim.Log, "type 41 in FF-FF-FF-52", mark), p34 = Find(r.Sim.Log, "type 34", mark);
            Check(p26 >= 0 && p46 > p26 && pPl > p46 && pTdp > pPl && p34 > pTdp, "Performance order: profile -> fans -> PL -> TDP -> GPU");
            Check(r.St.SavedMode == Mode.Balanced, "Performance is saved as Balanced");

            mark = r.Sim.Log.Count;
            long now = RunFor(r, 0, 600000);
            Check(r.Sim.HostPl1 == 80 && r.Sim.Tdp == 82 && r.Sim.Profile == 0x04 && r.Core.InPerformance && !r.Core.Failed, "Performance held for 10 min");
            bool paired = true; int n16 = 0;
            for (int i = mark; i < r.Sim.Log.Count; i++)
                if (r.Sim.Log[i].IndexOf("type 16", StringComparison.Ordinal) >= 0) { n16++; if (i + 1 >= r.Sim.Log.Count || r.Sim.Log[i + 1].IndexOf("type 46", StringComparison.Ordinal) < 0) paired = false; }
            Check(paired && n16 >= 20, "type 16 is only ever sent right before a fan write, at least every 30 s");
            Check(Count(r.Sim.Log, "type 26 in FF-04-00-00", mark) >= 13, "heartbeat re-sends the profile every 45 s");

            mark = r.Sim.Log.Count;
            Check(r.Core.Select(Mode.Balanced, now) && !r.Core.InPerformance && r.Sim.Tdp == 30 && r.Sim.HostPl1 == 55 && r.Sim.Profile == 0x30 && Same(r.Sim.Gpu, HpBios.GpuBoost),
                "leave Performance: TDP 30, PL 55, profile 0x30, GPU Boost");
            int tdp = Find(r.Sim.Log, "type 41 in FF-FF-FF-1E", mark);
            Check(tdp >= 0 && r.Sim.Log[tdp].IndexOf("(profile 0x04)", StringComparison.Ordinal) >= 0, "TDP is restored while 0x04 is still active");

            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0);
            mark = r.Sim.Log.Count;
            r.Unplug(0);
            Check(r.Core.Effective == Mode.Eco && r.Core.Controller.AutoEco && r.Sim.FansByBios && Same(r.Sim.Gpu, HpBios.GpuBase) && r.Sim.Tdp == 30,
                "unplug in Performance: automatic Eco, fans to the firmware, GPU Base, TDP back");
            now = RunFor(r, 0, 120000);
            Check(Count(r.Sim.Log, "type 46", mark) == 0 && Count(r.Sim.Log, "type 16", mark) == 0, "on battery: no fan writes and no type 16");
            r.Plug(now);
            now = RunFor(r, now, 10000);   // the lapse after Performance ends first
            Check(r.Core.Effective == Mode.Balanced && !r.Sim.FansByBios && r.Sim.HeldCpu > 0, "replug: Balanced, software fans again");

            // ---- after Performance the old host state is left to lapse before the app drives the fans again
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0);
            now = RunFor(r, 0, 60000);
            mark = r.Sim.Log.Count;
            r.Core.Select(Mode.Balanced, now);
            Check(r.Core.Lapsing && r.Sim.FansByBios && r.Sim.HostPl1 == 55, "leaving Performance: PL 55 written, fans to the firmware, lapse started");
            int after = r.Sim.Log.Count;
            long t0 = now;
            now = RunFor(r, now, 120000);
            Check(r.Core.Lapsing && Count(r.Sim.Log, "type 16 ", after) == 0 && Count(r.Sim.Log, "type 46 ", after) == 0 && Count(r.Sim.Log, "type 26 ", after) == 0
                && Count(r.Sim.Log, "type 41 ", after) == 0, "for two minutes nothing is sent that would keep the old state alive");
            now = RunFor(r, now, 10000);
            Check(!r.Core.Lapsing && r.Sim.HostPl1 == 0 && r.Sim.HostPl2 == 0 && !r.Sim.FansByBios && r.Sim.HeldCpu == 19 && now - t0 <= 130000,
                "then the 55 W limit is gone (as after a fresh start) and the app's fan curve is back");
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0); r.Core.Select(Mode.Balanced, 0);
            r.Core.Select(Mode.Performance, 5000);
            Check(!r.Core.Lapsing && !r.Sim.FansByBios && r.Sim.HostPl1 == 80, "back to Performance during the lapse: lapse cancelled, Performance applied");
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0); r.Core.Select(Mode.Balanced, 0);
            r.Sens.Cpu = 96;
            RunFor(r, 0, 20000);
            Check(r.Core.Guard.Active && !r.Sim.FansByBios && r.Sim.HeldCpu == 48, "the thermal guard still takes the fans during the lapse");
            now = RunFor(r, 20000, 80000);
            r.Sens.Cpu = 60;
            now = RunFor(r, now, 400000);
            Check(!r.Core.Guard.Active && !r.Core.Lapsing && r.Sim.HostPl1 == 0 && r.Sim.HostPl2 == 0 && !r.Sim.FansByBios,
                "a guard during the lapse kept the old limit alive: the lapse starts again after it, and the 55 W limit is still dropped");

            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Eco, 0);
            r.Unplug(0);
            Check(r.Core.Effective == Mode.Eco && !r.Core.Controller.AutoEco && r.Sim.FansByBios, "manual Eco unplugged: stays Eco, fans to the firmware");
            r.Plug(0);
            Check(r.Core.Effective == Mode.Eco && !r.Sim.FansByBios && r.Sim.HeldCpu > 0, "manual Eco replugged: stays Eco, software fans again");
            r.Unplug(0);
            Check(!r.Core.Select(Mode.Performance, 0) && r.Core.Failed && r.Core.Effective == Mode.Eco, "Performance refused on battery");

            r = new Rig(); r.Core.Boot(true, 0);
            r.Sens.Cpu = 85;
            int last = r.Sim.HeldCpu; bool steps = true; now = 0;
            for (int k = 0; k < 12; k++) { now = RunFor(r, now, 5000); if (Math.Abs(r.Sim.HeldCpu - last) > 3) steps = false; last = r.Sim.HeldCpu; }
            Check(steps && r.Sim.HeldCpu == 42 && r.Sim.HeldGpu == 42, "rising temperature: both fans climb at most 3 levels per tick to 4200 rpm");
            now = RunFor(r, now, 120000);
            Check(r.Sim.HeldCpu == 42 && r.Sim.HeldGpu == 42 && !r.Core.Guard.Active, "85 C for minutes: the curve's 4200 rpm, no extra rule below the guard");

            // ---- the fan choice: Eco and Balanced are always Auto; Performance may use the one custom curve
            r = new Rig(); r.Core.Boot(true, 0);
            r.Sens.Cpu = 85;
            now = RunFor(r, 0, 5000);                     // the engine reads temperatures on its 5 s tick
            r.Core.SetFanMode(true, now);
            Check(!r.Core.CustomFans && !r.St.PerfCustom && r.Sim.HeldCpu != 34, "Balanced has no Custom: the request is ignored");
            r.Core.Select(Mode.Performance, now);
            Check(!r.Core.CustomFans && r.Sim.HeldCpu == 42, "Performance starts on Auto");
            r.Core.SetFanMode(true, now);
            Check(r.Core.CustomFans && r.Sim.HeldCpu == 34 && r.Sim.HeldGpu == 24, "Custom in Performance: CPU fan 34 from 85 C, GPU fan 24 from 45 C");
            r.Core.Select(Mode.Balanced, now);
            now = RunFor(r, now, Engine.LapseMs);   // the lapse after Performance, then the app's curve
            Check(!r.Core.CustomFans && r.Sim.HeldCpu == 42, "Balanced stays on Auto while Performance is set to Custom");
            r.Core.Select(Mode.Eco, now);
            Check(!r.Core.CustomFans && r.Sim.HeldCpu == 42, "Eco stays on Auto too");
            r.Core.Select(Mode.Performance, now);
            Check(r.Core.CustomFans && r.Sim.HeldCpu == 34, "back in Performance: Custom was remembered");
            Check(r.Core.SetCurve(new int[] { 30, 30, 30, 30, 30, 40, 40 }, now) && r.Sim.HeldCpu == 40 && r.Sim.HeldGpu == 30, "editing the curve applies at once");
            Check(!r.Core.SetCurve(new int[] { 30, 25, 25, 25, 25, 25, 25 }, now) && !r.Core.SetCurve(new int[] { 10, 20, 30, 40, 45, 46, 47 }, now)
                && !r.Core.SetCurve(new int[] { 30, 30, 30 }, now) && Same(r.St.Curve, new int[] { 30, 30, 30, 30, 30, 40, 40 }), "a falling, out-of-range or short curve is refused");
            Check(!r.Core.SetCurve(new int[] { 19, 19, 19, 24, 29, 29, 29 }, now) && r.Core.SetCurve(new int[] { 19, 19, 19, 29, 29, 29, 29 }, now),
                "OGH's lower bound holds: from 75 C up a point below 2900 rpm is refused, the bound itself is accepted");
            r.Core.SetCurve(new int[] { 30, 30, 30, 30, 30, 40, 40 }, now);
            Rig again = new Rig(new SimFirmware(), r.Dir);
            Check(again.St.PerfCustom && Same(again.St.Curve, new int[] { 30, 30, 30, 30, 30, 40, 40 }), "the choice and the custom curve survive a restart");
            again.Sens.Cpu = 85; again.Core.Boot(true, 0);
            Check(again.Sim.HeldCpu == 42, "after a restart Balanced is on Auto");
            again.Core.Select(Mode.Performance, 0);
            Check(again.Sim.HeldCpu == 40, "and Performance comes back on the custom curve");

            // ---- the BIOS does not answer for a moment (e.g. right after sleep)
            r = new Rig(); r.Core.Boot(true, 0); now = RunFor(r, 0, 10000);
            r.Sim.FailAll = true;
            Check(!r.Core.Resume(true, now) && r.Core.Failed && r.Core.Fault != null, "resume while the BIOS does not answer: failure shown");
            r.Sim.FailAll = false;
            now = RunFor(r, now, 50000);
            Check(!r.Core.Failed && r.Core.Fault == null && r.Sim.Profile == 0x30 && !r.Sim.FansByBios && r.Sim.HeldCpu > 0, "the next heartbeat applies the mode again by itself");

            // ---- thermal guard
            r = new Rig(); r.Core.Boot(true, 0);
            r.Sens.Cpu = 96;
            now = RunFor(r, 0, 20000);
            Check(r.Core.Guard.Active && r.Sim.HeldCpu == 48 && r.Sim.HeldGpu == 48 && r.Core.Fault != null, "thermal guard: CPU >= 95 for two readings -> fans at 48, fault shown");
            r.Sens.Cpu = 60;
            now = RunFor(r, now, 40000);
            Check(r.Core.Guard.Active && r.Sim.HeldCpu == 48, "thermal guard holds during the safe minute");
            now = RunFor(r, now, 40000);
            Check(!r.Core.Guard.Active && r.Sim.HeldCpu < 48 && r.Core.Fault == null, "thermal guard released after 60 s safe; the curve takes over");

            r = new Rig(); r.Core.Boot(true, 0); r.Unplug(0);
            r.Sens.Cpu = 96;
            now = RunFor(r, 0, 20000);
            Check(r.Core.Guard.Active && !r.Sim.FansByBios && r.Sim.HeldCpu == 48, "thermal guard on battery takes the fans from the firmware");
            r.Sens.Cpu = 50;
            now = RunFor(r, now, 90000);
            Check(!r.Core.Guard.Active && r.Sim.FansByBios, "released on battery: fans back to the firmware");

            // ---- no CPU temperature -> the firmware drives the fans
            r = new Rig(); r.Sens.Cpu = double.NaN; r.Core.Boot(true, 0);
            Check(r.Sim.FansByBios && Count(r.Sim.Log, "type 46", 0) == 0 && r.Core.Fault != null && r.Core.Fault.IndexOf("No CPU temperature", StringComparison.Ordinal) >= 0,
                "no CPU temperature at start: fans by BIOS, no level written, fault shown");
            r.Sens.Cpu = 55;
            now = RunFor(r, 0, 5000);
            Check(!r.Sim.FansByBios && r.Sim.HeldCpu == 19 && r.Core.Fault == null, "CPU temperature back: software fans again");
            r.Sens.Cpu = double.NaN;
            now = RunFor(r, now, 10000);
            Check(!r.Sim.FansByBios, "two missing readings are a hiccup: nothing changes");
            now = RunFor(r, now, 5000);
            mark = r.Sim.Log.Count;
            Check(r.Sim.FansByBios, "three missing readings in a row: fans handed to the firmware");
            RunFor(r, now, 60000);
            Check(Count(r.Sim.Log, "type 46", mark) == 0 && Count(r.Sim.Log, "type 16", mark) == 0, "while the firmware has the fans: no level writes, no type 16");

            // ---- monitoring only (wrong laptop): never a write
            r = new Rig(); r.Core.MonitorOnly = true; r.Core.MonitorReason = "test";
            Check(r.Core.Boot(true, 0), "monitoring only: boot succeeds");
            RunFor(r, 0, 600000);
            Check(Writes(r.Sim.Log, 0) == 0 && r.Sim.FansByBios && r.Core.Fans != null, "monitoring only: 10 min without a single write; fans are still read");
            Check(!r.Core.Select(Mode.Performance, 0) && r.Core.Failed && Writes(r.Sim.Log, 0) == 0 && r.Core.Fault.IndexOf("Monitoring only", StringComparison.Ordinal) >= 0,
                "monitoring only: mode changes are refused with the reason");
            r.Core.SetFanMode(true, 0); r.Unplug(0); r.Plug(0); r.Core.Resume(true, 0); r.Core.Exit(false);
            Check(Writes(r.Sim.Log, 0) == 0, "monitoring only: fan choice, power events, resume and exit write nothing either");

            // ---- fans not following
            r = new Rig(); r.Core.Boot(true, 0);
            r.Sens.Cpu = 80; r.Sim.IgnoreType = 46;
            RunFor(r, 0, 150000);
            Check(r.Core.FanControlLost && r.Sim.FansByBios && r.Core.Fault != null, "fans far below the level written for a minute: control handed to the firmware");

            r = new Rig(); r.Sim.ErrorType = 46; r.Core.Boot(true, 0);
            RunFor(r, 0, 480000);
            Check(r.Core.FanControlLost && r.Sim.FansByBios, "fan writes refused 8 times: control handed to the firmware");

            r = new Rig(); r.Core.Boot(true, 0); RunFor(r, 0, 30000);
            r.Sim.Advance(120000);
            Check(r.Sim.HeldCpu == -1 && Same(r.Sim.FansNow(), new int[] { 21, 19 }), "crash: without the app the firmware's own curve returns within ~2 min");

            // ---- leaving
            r = new Rig(); r.Core.Boot(true, 0);
            r.Core.Exit(false);
            Check(r.Sim.FansByBios && r.Sim.Profile == 0x30 && Find(r.Sim.Log, "type 46 in 23-23", 0) >= 0, "exit: fans parked at 35 then handed to the firmware");
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Exit(true);
            Check(Find(r.Sim.Log, "type 46 in 23-23", 0) < 0 && r.Sim.FansByBios, "logoff exit keeps the current level (no fallback replayed into the next boot)");
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0); r.Core.Exit(false);
            Check(r.Sim.Tdp == 30 && r.Sim.HostPl1 == 55 && r.Sim.Profile == 0x30 && r.Sim.FansByBios, "exit in Performance: TDP back, PL 55, profile 0x30, firmware fans");
            Check(r.Sim.Gpu[0] == HpBios.GpuBoost[0] && r.Sim.Gpu[1] == HpBios.GpuBoost[1] && r.Sim.Gpu[3] == HpBios.GpuBoost[3], "exit in Performance: GPU record back to Boost (it does not lapse by itself)");

            // ---- Performance safety rules and conflicts
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0);
            r.Sim.IrTemp = 56;
            now = RunFor(r, 0, 5000);
            Check(Same(r.Sim.Gpu, new byte[] { 1, 0, 1, 75 }) && r.Sim.HostPl1 == 75, "governor (chassis 54): from GPU Max to GPS 75 + PPAB off, PL -5 W");
            r.Sim.IrTemp = 45;
            now = RunFor(r, now, 30000);
            Check(Same(r.Sim.Gpu, HpBios.GpuMax), "governor: below 49 the GPU goes back to Max");

            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0); now = 0;
            for (int k = 0; k < 6; k++) { r.Sim.HostPl1 = 55; now = RunFor(r, now, 45000); }
            Check(r.Core.Failed && r.Core.Effective == Mode.Balanced && !r.Core.InPerformance && r.Core.LastError.IndexOf("OMEN Gaming Hub", StringComparison.Ordinal) >= 0,
                "conflict: another tool keeps overriding -> bounded, then Balanced with a message");
            // entry, the two re-checks after the GPU write, then one per conflicting heartbeat
            Check(Count(r.Sim.Log, "type 41 in 50-50-FF-FF", 0) <= 4 + Engine.MaxConflicts, "conflict: bounded number of re-applies");

            r = new Rig(); r.Core.Boot(true, 0);
            RunFor(r, 0, 100000);
            Check(r.Kills >= 2, "OGH's background is ended again on every heartbeat");

            r = new Rig(); r.Sim.EchoDState = false; r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0);
            mark = r.Sim.Log.Count;
            RunFor(r, 0, 60000);
            Check(!r.Core.Failed && Count(r.Sim.Log, "type 34", mark) == 0 && r.Core.Fault == null, "a firmware that keeps dState 0 is accepted: no rewrite loop, no warning");

            r = new Rig(); r.Core.Boot(true, 0); r.Sim.IgnoreType = 34; r.Core.Select(Mode.Performance, 0);
            mark = r.Sim.Log.Count;
            RunFor(r, 0, 60000);
            Check(r.Core.Fault != null && r.Core.Fault.IndexOf("GPU power", StringComparison.Ordinal) >= 0 && Count(r.Sim.Log, "type 34", mark) == 0, "a GPU preset the firmware does not take: fault shown, not retried in a loop");

            SimFirmware crashed = new SimFirmware(); crashed.Tdp = 82;
            r = new Rig(crashed, NewDir()); r.St.BaselineTdp = 30;
            Check(r.Core.Boot(true, 0) && crashed.Tdp == 30, "boot after a crash in Performance: concurrent TDP repaired to 30");

            r = new Rig(); r.Sim.FailAll = true;
            Check(!r.Core.Boot(true, 0) && r.Core.Failed, "WMI not ready at logon -> failure, no crash");
            r.Sim.FailAll = false;
            Check(r.Core.Boot(true, 0) && !r.Core.Failed, "the retry succeeds once WMI answers");

            r = new Rig(); r.Core.Boot(true, 0); r.Sim.IgnoreType = 41;
            Check(!r.Core.Select(Mode.Performance, 0) && r.Core.Failed && r.Core.Effective == Mode.Balanced && !r.Core.InPerformance && r.Sim.Profile == 0x30,
                "Performance read-back mismatch -> message, back to Balanced");

            // ---- start-up with a host limit left by a session that ended moments ago (update, crash, kill)
            SimFirmware stale = new SimFirmware(); stale.HostPl1 = 80; stale.HostPl2 = 80; stale.KeepAliveMs = 0;
            r = new Rig(stale, NewDir());
            Check(r.Core.Boot(true, 0) && r.Core.Lapsing && r.Sim.FansByBios, "boot with a host limit still set: it is left to lapse first, the firmware drives the fans");
            mark = r.Sim.Log.Count;
            now = RunFor(r, 0, 120000);
            Check(Count(r.Sim.Log, "type 16 ", mark) == 0 && Count(r.Sim.Log, "type 46 ", mark) == 0 && Count(r.Sim.Log, "type 26 ", mark) == 0
                && Count(r.Sim.Log, "type 41 ", mark) == 0, "boot lapse: nothing is sent that would keep the old limit alive");
            now = RunFor(r, now, 15000);
            Check(!r.Core.Lapsing && r.Sim.HostPl1 == 0 && r.Sim.HostPl2 == 0 && !r.Sim.FansByBios && r.Sim.HeldCpu > 0, "then the limit is gone and the app's curve runs");

            // ---- the firmware changes concurrent TDP right after a GPU write (seen once on the laptop)
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Eco, 0);
            r.Sim.TdpAfterGpuWrite = 45;
            Check(r.Core.Select(Mode.Performance, 0) && r.Core.InPerformance && r.Sim.Tdp == 82 && r.Sim.HostPl1 == 80, "TDP changed after the GPU write: written again at once");
            now = RunFor(r, 0, 200000);
            Check(r.Core.InPerformance && !r.Core.Failed && r.Sim.Tdp == 82, "and Performance holds, no conflict counted");

            // ... and on the laptop it happens about a second later (entering Performance from Eco): caught on the next tick
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Eco, 0);
            r.Sim.TdpAfterGpuWrite = 45; r.Sim.TdpResetDelayMs = 1000;
            r.Core.Select(Mode.Performance, 0);
            now = RunFor(r, 0, 10000);
            Check(r.Core.InPerformance && r.Sim.Tdp == 82, "TDP changed a second after the GPU write: written again within 5 s, not at the next heartbeat");

            // ---- a mode change while the old limit lapses is not sent (on the laptop a profile write kept PL 55 alive)
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0);
            now = RunFor(r, 0, 60000);
            r.Core.Select(Mode.Balanced, now);
            mark = r.Sim.Log.Count;
            long left = now;
            now = RunFor(r, now, 30000);
            Check(r.Core.Select(Mode.Eco, now) && r.Win.Last == Mode.Eco && r.Core.Effective == Mode.Eco && r.Core.Lapsing,
                "Eco during the lapse: chosen at once (Windows power mode too), the lapse goes on");
            now = RunFor(r, now, 30000);
            r.Core.Select(Mode.Balanced, now);
            now = RunFor(r, now, 50000);
            Check(Count(r.Sim.Log, "type 26 ", mark) == 0 && Count(r.Sim.Log, "type 34 ", mark) == 0 && Count(r.Sim.Log, "type 41 ", mark) == 0
                && Count(r.Sim.Log, "type 16 ", mark) == 0 && Count(r.Sim.Log, "type 46 ", mark) == 0, "mode changes during the lapse send nothing to the firmware");
            now = RunFor(r, now, Engine.LapseMaxMs - (now - left) + 10000);
            Check(!r.Core.Lapsing && r.Sim.HostPl1 == 0 && r.Sim.Profile == 0x30 && !r.Sim.FansByBios && Same(r.Sim.Gpu, HpBios.GpuBoost) && r.Sim.HeldCpu > 0,
                "the limit lapses anyway, then the last choice (Balanced) is applied with the app's fans");

            // ---- fail safe after an unexpected error
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0); now = RunFor(r, 0, 30000);
            r.Core.FailSafe("test");
            Check(r.Core.Halted && r.Sim.FansByBios && r.Sim.Profile == 0x30 && r.Sim.Tdp == 30 && r.Sim.HostPl1 == 55 && Same(r.Sim.Gpu, HpBios.GpuBoost)
                && r.Core.Fault != null && r.Core.Fault.IndexOf("Restart", StringComparison.Ordinal) >= 0, "fail safe: Performance left, firmware fans, GPU Boost, message shown");
            mark = r.Sim.Log.Count;
            now = RunFor(r, now, 600000);
            bool refused = !r.Core.Select(Mode.Eco, now);
            r.Unplug(now); r.Plug(now); r.Core.Resume(true, now); r.Core.SetFanMode(true, now); r.Core.SetCurve(FanCurve.DefaultCustom, now); r.Core.Exit(false);
            Check(refused && Writes(r.Sim.Log, mark) == 0 && r.Sim.HostPl1 == 0, "fail safe: 10 min and every later event without one write; the old limit lapsed");
        }

        // ---------------------------------------------------------------- endurance (E1)

        /// <summary>Fourteen simulated days: one app session per day (sometimes restarted within seconds, as an update does),
        /// random load, unplugging, sleep, mode changes, missing temperatures and BIOS calls that fail. Checked every 5 s.</summary>
        private static void EnduranceTests()
        {
            Random rnd = new Random(20261113);
            SimFirmware sim = new SimFirmware();
            string dir = NewDir();
            int bad = 0; string firstBad = null;
            long hotSince = -1, lapseSince = -1, perf04Since = -1, ticks = 0;
            long mem0 = -1, mem1 = 0;
            List<string> trail = new List<string>();
            long[] seen = new long[5];   // ticks in Performance, lapsing, guard, on battery, failure shown
            try
            {
                for (int day = 0; day < 14; day++)
                {
                    Rig r = new Rig(sim, dir);
                    bool ac = true;
                    double target = 50;
                    long now = 0;
                    r.Core.SetPowerInputs(true, false, 80);
                    r.Core.Boot(true, now);
                    for (int k = 0; k < 17280; k++, ticks++)   // 24 h of 5 s ticks
                    {
                        if (rnd.NextDouble() < 0.003) target = target > 70 ? 50 : 82 + rnd.Next(16);   // load starts or stops
                        double cpu = double.IsNaN(r.Sens.Cpu) ? 50 : r.Sens.Cpu;
                        cpu += (target - cpu) * 0.1 + (rnd.NextDouble() - 0.5) * 4;
                        r.Sens.Cpu = rnd.NextDouble() < 0.01 ? double.NaN : Math.Max(30, Math.Min(100, cpu));
                        r.Sens.Gpu = Math.Max(30, Math.Min(90, cpu - 5)); r.Sens.Asleep = target < 70 && rnd.NextDouble() < 0.5;
                        r.Sens.Load = target > 70 ? 95 : 3;
                        sim.FailAll = rnd.NextDouble() < 0.01;
                        double ev = rnd.NextDouble();
                        if (ev < 0.002) r.Core.Select((Mode)rnd.Next(3), now);
                        else if (ev < 0.003) { ac = !ac; r.Core.SetPowerInputs(ac, false, 80); r.Core.PowerChanged(ac, now); }
                        else if (ev < 0.0033) { long slept = 60000L * rnd.Next(1, 120); sim.Advance(slept); now += slept; r.Core.Resume(ac, now); hotSince = -1; }
                        sim.Advance(5000); now += 5000;
                        r.Core.Tick(now);
                        trail.Add(now / 1000 + " fail " + sim.FailAll + " cpu " + r.Sens.Cpu.ToString("0") + " calls " + sim.Log.Count
                            + " held " + sim.HeldCpu + " byBios " + sim.FansByBios + " ka " + sim.KeepAliveMs / 1000 + " guard " + r.Core.Guard.Active + " lapsing " + r.Core.Lapsing + " ev " + ev.ToString("0.0000"));
                        if (trail.Count > 30) trail.RemoveAt(0);
                        sim.Log.Clear();

                        string why = null;
                        if (sim.HostPl1 > 80 || sim.HostPl2 > 80 || (sim.HostPl1 != 0 && sim.HostPl1 < 45)) why = "PL outside 45..80";
                        else if (r.Core.InPerformance && !ac) why = "Performance on battery";
                        else if (r.St.SavedMode == Mode.Performance) why = "Performance saved";
                        lapseSince = r.Core.Lapsing && !r.Core.Guard.Active ? (lapseSince < 0 ? now : lapseSince) : -1;   // the guard restarts it
                        if (lapseSince >= 0 && now - lapseSince > Engine.LapseMaxMs + Engine.TickMs) why = "lapse longer than 180 s";
                        perf04Since = sim.Profile == HpBios.ProfilePerformance && !r.Core.InPerformance ? (perf04Since < 0 ? now : perf04Since) : -1;
                        if (perf04Since >= 0 && now - perf04Since > Engine.LapseMaxMs) why = "profile 0x04 without Performance for 3 minutes";
                        // the thermal guard: CPU at 95 C or more (on battery and during a lapse too) puts the fans at maximum;
                        // 40 s = two guard readings (20 s) plus one failed write retried at the next reading, with margin
                        bool hot = r.Sens.Cpu >= 95 && !r.Core.FanControlLost;
                        hotSince = hot ? (hotSince < 0 ? now : hotSince) : -1;
                        if (hotSince >= 0 && now - hotSince >= 40000 && sim.FansNow()[0] != HpBios.FanMax)
                            why = "CPU at 95 C for 40 s and the fans not at maximum (fans " + sim.FansNow()[0] + ", levels " + r.Core.Levels[0]
                                + ", guard " + r.Core.Guard.Active + ", sim byBios " + sim.FansByBios + ", fault " + r.Core.Fault + ")";
                        if (r.Core.InPerformance) seen[0]++;
                        if (r.Core.Lapsing) seen[1]++;
                        if (r.Core.Guard.Active) seen[2]++;
                        if (!ac) seen[3]++;
                        if (r.Core.Failed) seen[4]++;
                        if (why != null) { bad++; if (firstBad == null) { firstBad = "day " + day + " t " + now + ": " + why; foreach (string s in trail) Console.WriteLine(s); } }
                    }
                    r.Core.Exit(false);
                    if (Settings.Load(dir).SavedMode == Mode.Performance) { bad++; if (firstBad == null) firstBad = "Performance saved on disk"; }
                    sim.Advance(rnd.NextDouble() < 0.3 ? 20000 : 8 * 3600000L);   // an update restarts within seconds; otherwise the night
                    GC.Collect();
                    if (day == 0) mem0 = GC.GetTotalMemory(true); else mem1 = GC.GetTotalMemory(true);
                }
            }
            catch (Exception e) { bad++; firstBad = "exception: " + e.Message; }
            Check(bad == 0, "endurance: 14 simulated days (" + ticks + " ticks) keep every invariant" + (firstBad == null ? "" : "; first: " + firstBad));
            bool all = true; foreach (long n in seen) if (n < 100) all = false;
            Check(all, "endurance: the run covered Performance, lapses, the guard, battery and failures ("
                + string.Join("/", Array.ConvertAll(seen, x => x.ToString())) + " ticks)");
            Check(mem1 - mem0 < 1024 * 1024,"endurance: memory does not grow from day 1 to day 14 (" + (mem1 - mem0) / 1024 + " KB)");
        }

        private static void SettingsTests()
        {
            string dir = NewDir();
            Settings s = Settings.Load(dir);
            Check(s.SavedMode == Mode.Balanced && s.BaselineTdp == -1 && !s.PerfCustom && Same(s.Curve, FanCurve.DefaultCustom) && Settings.ValidCurve(FanCurve.DefaultCustom),
                "settings: defaults without a file (Performance on Auto, a valid default curve)");
            s.SavedMode = Mode.Eco; s.BaselineTdp = 30; s.FactoryGpu = new byte[] { 0, 1, 0, 87 }; s.PerfCustom = true;
            s.Curve = new int[] { 20, 22, 24, 30, 36, 42, 48 };
            Check(s.Save(), "settings: save");
            Settings t = Settings.Load(dir);
            Check(t.SavedMode == Mode.Eco && t.BaselineTdp == 30 && Same(t.FactoryGpu, new byte[] { 0, 1, 0, 87 }) && t.PerfCustom && Same(t.Curve, s.Curve), "settings: round trip");
            File.WriteAllText(Path.Combine(dir, "state.txt"), "savedMode=Performance\r\nbaselineTdp=200\r\nfactoryGpu=00-01-00-FF\r\ncurve=20,19,20,20,20,20,20\r\nfanPerformance=maybe\r\nfanEco=custom\r\ntakeOver=0\r\n");
            t = Settings.Load(dir);
            Check(t.SavedMode == Mode.Balanced && t.BaselineTdp == -1 && t.FactoryGpu == null && Same(t.Curve, FanCurve.DefaultCustom) && !t.PerfCustom,
                "settings: out-of-range, falling or unknown values are ignored");
            Check(Settings.ParseCurve("19,19,19,19,19,19,49") == null && Settings.ParseCurve("19,20,21") == null && Settings.ParseCurve("a,b,c,d,e,f,g") == null
                && Settings.ParseCurve("18,20,21,29,30,31,32") == null && Settings.ParseCurve("19,20,21,29,30,31,48") != null, "curve parse: 7 rising levels inside 19..48");
            Check(Same(Settings.ParseCurve("19,24,24,24,39,48,48"), new int[] { 19, 24, 24, 29, 39, 48, 48 }) && Same(Settings.ParseCurve("19,19,19,19,19,19,19"), FanCurve.CustomFloor),
                "a curve saved by an older build is raised to OGH's lower bound, not thrown away");
        }
    }
}
