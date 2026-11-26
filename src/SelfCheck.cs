// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

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

        // ---- the window checks: controls painted without a window (OnPaint and the other WinForms handlers by reflection)
        private static readonly System.Reflection.BindingFlags NonPublic = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        /// <summary>A control as it paints itself at w x h (its OnPaint, without a window). The caller disposes the picture.</summary>
        private static System.Drawing.Bitmap Painted(System.Windows.Forms.Control c, int w, int h)
        {
            System.Drawing.Bitmap b = new System.Drawing.Bitmap(w, h);
            c.Size = new System.Drawing.Size(w, h);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(b))
                typeof(System.Windows.Forms.Control).GetMethod("OnPaint", NonPublic).Invoke(c, new object[] { new System.Windows.Forms.PaintEventArgs(g, new System.Drawing.Rectangle(0, 0, w, h)) });
            return b;
        }

        /// <summary>A CPU card with this temperature history (oldest first; empty: nothing read yet).</summary>
        private static ChipCard CpuCard(double[] temps) { ChipCard c = new ChipCard(); c.Title = "CPU"; c.IconName = "cpu"; c.TempDots = temps; return c; }

        /// <summary>The mouse on a slot of a history line (0 temperature, 1 fan speed), there for a second (the slot is out).</summary>
        private static void HoverOn(ChipCard c, int row, int slot) { c._hoverRow = row; c._hoverSlot = slot; c._hoverSince = Environment.TickCount - 1000; }

        // ---- colour maths for the theme checks: linear light, OKLab
        private static double Lin(int c) { double v = c / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }

        /// <summary>OKLab distance x100 between two colours.</summary>
        private static double Oklab(System.Drawing.Color a, System.Drawing.Color b)
        {
            double[] la = Lab(a), lb = Lab(b);
            return 100 * Math.Sqrt((la[0] - lb[0]) * (la[0] - lb[0]) + (la[1] - lb[1]) * (la[1] - lb[1]) + (la[2] - lb[2]) * (la[2] - lb[2]));
        }

        private static double[] Lab(System.Drawing.Color c)
        {
            double[] v = { Lin(c.R), Lin(c.G), Lin(c.B) };
            double l = Math.Pow(0.4122214708 * v[0] + 0.5363325363 * v[1] + 0.0514459929 * v[2], 1 / 3.0);
            double m = Math.Pow(0.2119034982 * v[0] + 0.6806995451 * v[1] + 0.1073969566 * v[2], 1 / 3.0);
            double s = Math.Pow(0.0883024619 * v[0] + 0.2817188376 * v[1] + 0.6299787005 * v[2], 1 / 3.0);
            return new double[] { 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s, 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s, 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s };
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

        /// <summary>Firmware writes in the simulator log from 'from' on (profile, GPU, power).</summary>
        private static int Writes(List<string> log, int from)
        {
            return Count(log, "type 26 ", from) + Count(log, "type 34 ", from) + Count(log, "type 41 ", from);
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
            IdentityTests();
            FirmwareTests();
            CoreTests();
            SettingsTests();
            WindowTests();
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
                        bool omen = (t == 26 || t == 33 || t == 34 || t == 35 || t == 41) ? i == 4 && o == 4
                            : (t == 45 || t == 53) ? i == 4 && o == 128 : t == 42 ? i == 0 && o == 128 : false;
                        if (FirmwareRules.IsAllowed(0x20008, t, i, o) != omen) exact = false;
                        if (FirmwareRules.IsAllowed(1, t, i, o) != (t == 15 && i == 4 && o == 4)) exact = false;
                        if (FirmwareRules.IsAllowed(2, t, i, o) || FirmwareRules.IsAllowed(3, t, i, o) || FirmwareRules.IsAllowed(0x20009, t, i, o)) exact = false;
                    }
            Check(exact, "allow-list: exactly the pairs the app uses, with exact sizes; nothing else for any type, size or command");
            bool noFanControl = true;
            foreach (int t in new int[] { 16, 39, 46 }) foreach (int i in ins) foreach (int o in outs) if (FirmwareRules.IsAllowed(0x20008, t, i, o)) noFanControl = false;
            Check(noFanControl && !FirmwareRules.ChangesState(0x20008, 16) && !FirmwareRules.ChangesState(0x20008, 46),
                "allow-list: no fan control at any size: fan levels (46), their trigger (16) and max fan (39) are refused before sending");
            Check(!FirmwareRules.IsAllowed(1, 0x52, 4, 4) && !FirmwareRules.IsAllowed(2, 0x52, 4, 4) && !FirmwareRules.IsAllowed(0x20008, 40, 4, 128),
                "allow-list: graphics mode (0x52) and system data (40) are not used");

            SimFirmware sim = new SimFirmware();
            HpBios b = new HpBios(sim);
            Check(Throws(() => b.WritePowerLimits(81)) && Throws(() => b.WriteTdp(83)), "range: PL above 80 or TDP above 82 throw");
            Check(Throws(() => b.WriteGpu(new byte[] { 2, 1, 1, 87 })) && Throws(() => b.WriteGpu(new byte[] { 1, 1, 1, 88 })) && Throws(() => b.WriteGpu(new byte[] { 0, 0, 1, 74 })), "range: GPU record outside the presets' ranges throws");
            Check(b.WriteGpu(HpBios.GpuBase) && b.WriteGpu(HpBios.GpuBoost) && b.WriteGpu(HpBios.GpuMax), "range: all three Ohman presets are writable");
            Check(Throws(() => b.SetProfile(0x31)) && Throws(() => b.ReadSensor(4)), "range: unknown profile or sensor throws");
            Check(Throws(() => sim.Execute(0x20008, 39, new byte[1], 4)) && Throws(() => sim.Execute(0x20008, 46, new byte[128], 4)) && Throws(() => sim.Execute(2, 0x52, new byte[4], 4)),
                "simulator enforces the same allow-list");

            int mark = sim.Log.Count;
            Check(b.SetProfile(HpBios.ProfileBalanced) && b.SetProfile(HpBios.ProfilePerformance) && Find(sim.Log, "type 26 in FF-30-01-00", mark) >= 0
                && Find(sim.Log, "type 26 in FF-04-01-00", mark) >= 0, "profile write: byte 2 is always 1 (the firmware drives the fans)");
            int[] levels = b.ReadFanLevels();
            Check(levels != null && levels.Length == 2 && levels[0] == 21 && levels[1] == 19, "sim: fan reads give the firmware's own levels");
            Check(b.SetProfile(HpBios.ProfileBalanced) && b.WriteTdp(55) && b.ReadPower().Tdp == 30, "sim: concurrent TDP is ignored outside 0x04");
            Check(b.SetProfile(HpBios.ProfilePerformance) && b.WriteTdp(82) && b.ReadPower().Tdp == 82, "sim: concurrent TDP applies under 0x04");
            b.WritePowerLimits(70);
            sim.Advance(100000);
            bool held = sim.HostPl1 == 70;
            sim.Advance(20000);
            Check(held && sim.HostPl1 == 0 && sim.Profile == HpBios.ProfileBalanced, "sim: a host limit is held 100 s and dropped ~110 s after the last refresh");

            SimFirmware quiet = new SimFirmware { KeepLog = false };   // as --demo uses it
            HpBios qb = new HpBios(quiet);
            qb.WritePowerLimits(70);
            bool heldQuiet = quiet.HostPl1 == 70;
            quiet.Advance(200000);
            Check(heldQuiet && quiet.HostPl1 == 0 && quiet.Log.Count == 0,
                "sim without its call log (--demo): same behaviour, and the log stays empty instead of growing");

            long wall = 0;
            SimFirmware clocked = new SimFirmware { KeepLog = false, Clock = delegate { return wall; } };   // as --demo uses it
            HpBios cb = new HpBios(clocked);
            cb.WritePowerLimits(70);
            wall = 100000; bool heldClocked = cb.ReadPower().Pl1 == 70;
            wall = 120000;
            Check(heldClocked && cb.ReadPower().Pl1 == 0, "sim on the real clock (--demo): a host limit lapses ~110 s later with no one calling Advance");

            FwReply ro = new WmiFirmware(true).Execute(FirmwareRules.Omen, 41, new byte[] { 70, 70, 0xFF, 0xFF }, 4);
            Check(!ro.Ok && ro.Error != null && ro.Error.StartsWith("read-only", StringComparison.Ordinal),
                "read-only mode (--read, --dry-run) refuses a write (type 41) before anything is sent");

            // the CPU temperature register (SystemSensors; the simulator never runs this formula)
            Check(SystemSensors.Tctl(400u << 21) == 50 && SystemSensors.Tctl(401u << 21) == 50.125 && SystemSensors.Tctl((792u << 21) | (1u << 19)) == 50
                && double.IsNaN(SystemSensors.Tctl(0)) && double.IsNaN(SystemSensors.Tctl(0x7FFu << 21)),
                "CPU temperature: bits 31:21 in eighths of a degree, -49 C with the range bit, nothing outside 0..130");
        }


        // ---------------------------------------------------------------- end-to-end (Engine + simulator)

        private static void CoreTests()
        {
            Rig r = new Rig();
            Check(r.Core.Boot(true, 0) && r.Sim.Profile == 0x30 && Find(r.Sim.Log, "type 26 in FF-30-01-00", 0) >= 0 && r.Core.Effective == Mode.Balanced
                && r.Win.Last == Mode.Balanced && r.Core.Fault == null, "boot Balanced on AC: profile 0x30 with the firmware's fans; no fault shown");
            Check(Engine.Same(r.Sim.Gpu, HpBios.GpuBoost) && Engine.Same(r.St.FactoryGpu, new byte[] { 0, 1, 0, 87 }) && Engine.Same(Settings.Load(r.Dir).FactoryGpu, new byte[] { 0, 1, 0, 87 }),
                "Balanced = GPU Boost; the record read first is kept for uninstall");
            int a = Find(r.Sim.Log, "type 26 in FF-30-01-00", 0), gw = Find(r.Sim.Log, "type 34", 0);
            Check(a >= 0 && gw > a, "apply order is Ohman's: profile -> GPU");

            Check(r.Core.Select(Mode.Eco, 0) && Engine.Same(r.Sim.Gpu, HpBios.GpuBase) && r.Sim.Profile == 0x30 && r.Win.Last == Mode.Eco, "Eco: profile 0x30, GPU Base, Windows efficiency");

            int mark = r.Sim.Log.Count;
            Check(r.Core.Select(Mode.Performance, 0) && r.Core.InPerformance && r.Sim.Profile == 0x04 && r.Sim.HostPl1 == 80 && r.Sim.HostPl2 == 80 && r.Sim.Tdp == 82
                && Engine.Same(r.Sim.Gpu, HpBios.GpuMax) && r.St.BaselineTdp == 30 && r.Win.Last == Mode.Performance,
                "Performance: 0x04, PL 80/80, TDP 82 verified, GPU Max, Windows best performance, baseline TDP 30 saved");
            int p26 = Find(r.Sim.Log, "type 26 in FF-04-01-00", mark), pPl = Find(r.Sim.Log, "type 41 in 50-50-FF-FF", mark),
                pTdp = Find(r.Sim.Log, "type 41 in FF-FF-FF-52", mark), p34 = Find(r.Sim.Log, "type 34", mark);
            Check(p26 >= 0 && pPl > p26 && pTdp > pPl && p34 > pTdp, "Performance order: profile -> PL -> TDP -> GPU");
            Check(r.St.SavedMode == Mode.Balanced, "Performance is saved as Balanced");

            mark = r.Sim.Log.Count;
            long now = RunFor(r, 0, 600000);
            Check(r.Sim.HostPl1 == 80 && r.Sim.Tdp == 82 && r.Sim.Profile == 0x04 && r.Core.InPerformance && !r.Core.Failed,
                "Performance held for 10 min by the heartbeat alone (no fan writes keep it alive)");
            Check(Count(r.Sim.Log, "type 26 in FF-04-01-00", mark) >= 13, "heartbeat re-sends the profile every 45 s");

            mark = r.Sim.Log.Count;
            Check(r.Core.Select(Mode.Balanced, now) && !r.Core.InPerformance && r.Sim.Tdp == 30 && r.Sim.HostPl1 == 55 && r.Sim.Profile == 0x30 && Engine.Same(r.Sim.Gpu, HpBios.GpuBoost),
                "leave Performance: TDP 30, PL 55, profile 0x30, GPU Boost");
            int tdp = Find(r.Sim.Log, "type 41 in FF-FF-FF-1E", mark);
            Check(tdp >= 0 && r.Sim.Log[tdp].IndexOf("(profile 0x04)", StringComparison.Ordinal) >= 0, "TDP is restored while 0x04 is still active");

            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0);
            r.Unplug(0);
            Check(r.Core.Effective == Mode.Eco && r.Core.Controller.AutoEco && Engine.Same(r.Sim.Gpu, HpBios.GpuBase) && r.Sim.Tdp == 30,
                "unplug in Performance: automatic Eco, GPU Base, TDP back");
            now = RunFor(r, 0, 120000);
            r.Plug(now);
            now = RunFor(r, now, 10000);   // the lapse after Performance ends first
            Check(r.Core.Effective == Mode.Balanced && r.Sim.Profile == 0x30 && Engine.Same(r.Sim.Gpu, HpBios.GpuBoost), "replug: Balanced");

            // ---- after Performance the old host limit is left to lapse
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0);
            now = RunFor(r, 0, 60000);
            r.Core.Select(Mode.Balanced, now);
            Check(r.Core.Lapsing && r.Sim.HostPl1 == 55, "leaving Performance: PL 55 written, lapse started");
            int after = r.Sim.Log.Count;
            long t0 = now;
            now = RunFor(r, now, 120000);
            Check(r.Core.Lapsing && Count(r.Sim.Log, "type 26 ", after) == 0 && Count(r.Sim.Log, "type 41 ", after) == 0,
                "for two minutes nothing is sent that would keep the old limit alive");
            now = RunFor(r, now, 10000);
            Check(!r.Core.Lapsing && r.Sim.HostPl1 == 0 && r.Sim.HostPl2 == 0 && now - t0 <= 130000,
                "then the 55 W limit is gone, as after a fresh start");
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0); r.Core.Select(Mode.Balanced, 0);
            r.Core.Select(Mode.Performance, 5000);
            Check(!r.Core.Lapsing && r.Sim.HostPl1 == 80, "back to Performance during the lapse: lapse cancelled, Performance applied");

            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Eco, 0);
            r.Unplug(0);
            Check(r.Core.Effective == Mode.Eco && !r.Core.Controller.AutoEco, "manual Eco unplugged: stays Eco");
            r.Plug(0);
            Check(r.Core.Effective == Mode.Eco, "manual Eco replugged: stays Eco");
            r.Unplug(0);
            Check(!r.Core.Select(Mode.Performance, 0) && r.Core.Failed && r.Core.Effective == Mode.Eco, "Performance refused on battery");

            // Windows keeps one power mode for the charger and one for battery; plugging in or out must not leave a stale one
            r = new Rig(); r.Core.Boot(true, 0);
            r.Core.Select(Mode.Eco, 0);          // charger slot: Best power efficiency
            r.Unplug(0);                         // manual Eco stays Eco
            r.Core.Select(Mode.Balanced, 0);     // battery slot: Balanced
            r.Plug(0);                           // manual Balanced stays Balanced: no mode change
            Check(r.Core.Effective == Mode.Balanced && r.Win.Ac == Mode.Balanced && r.Win.Dc == Mode.Balanced,
                "Windows power mode: Eco on the charger, Balanced on battery, plugged in again: the charger slot says Balanced too");

            // ---- the BIOS does not answer for a moment (e.g. right after sleep)
            r = new Rig(); r.Core.Boot(true, 0); now = RunFor(r, 0, 10000);
            r.Sim.FailAll = true;
            Check(!r.Core.Resume(true, now) && r.Core.Failed && r.Core.Fault != null, "resume while the BIOS does not answer: failure shown");
            r.Sim.FailAll = false;
            now = RunFor(r, now, 50000);
            Check(!r.Core.Failed && r.Core.Fault == null && r.Sim.Profile == 0x30, "the next heartbeat applies the mode again by itself");

            // ---- a BIOS that stops answering for good: logged once, not at every 45 s retry (the log keeps only 2 x 256 KB)
            SimFirmware gone = new SimFirmware();
            List<string> logged = new List<string>();
            Engine ge = new Engine(new HpBios(gone), Settings.Load(NewDir()), new NullPowerMode(), new SimSensors(), new GovernorConfig(), delegate(string s) { logged.Add(s); });
            ge.Boot(true, 0);
            gone.FailAll = true;
            ge.Select(Mode.Eco, 0);
            for (long t = 5000; t <= 200000; t += 5000) { gone.Advance(5000); ge.Tick(t); }   // four heartbeats retry the apply
            int failures = 0, unanswered = 0;
            foreach (string l in logged) { if (l.StartsWith("FAILURE", StringComparison.Ordinal)) failures++; if (l.IndexOf("did not answer", StringComparison.Ordinal) >= 0) unanswered++; }
            Check(ge.Failed && ge.Fault == "Eco not applied yet" && failures == 1 && unanswered == 1,
                "a lasting BIOS failure: the warning stays, and the log says it once, not at every retry (" + failures + " lines)");

            // ---- no CPU temperature (PawnIO missing): the modes still work; the window shows --
            r = new Rig(); r.Sens.Cpu = double.NaN;
            Check(r.Core.Boot(true, 0) && r.Core.Fault == null && r.Core.Select(Mode.Performance, 0) && r.Core.InPerformance,
                "no CPU temperature: Balanced and Performance apply, no warning (the panel shows --)");
            RunFor(r, 0, 60000);
            Check(r.Core.InPerformance && !r.Core.Failed, "no CPU temperature: the Performance governor keeps running");

            // ---- the app gone (crash, kill): the firmware drops the host state by itself
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0); RunFor(r, 0, 30000);
            r.Sim.Advance(120000);
            Check(r.Sim.HostPl1 == 0 && r.Sim.HostPl2 == 0 && r.Sim.Profile == 0x30, "crash: without the app the host limit and profile lapse within ~2 min");

            // ---- leaving
            r = new Rig(); r.Core.Boot(true, 0);
            mark = r.Sim.Log.Count;
            r.Core.Exit();
            Check(r.Sim.Profile == 0x30 && Find(r.Sim.Log, "type 26 in FF-30-01-00", mark) >= 0 && Writes(r.Sim.Log, mark) == 1, "exit from Balanced: one profile write (0x30), nothing else");
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Eco, 0);
            r.Core.Exit();
            Check(r.Sim.Profile == 0x30 && Engine.Same(new byte[] { r.Sim.Gpu[0], r.Sim.Gpu[1], r.Sim.Gpu[3] }, new byte[] { HpBios.GpuBoost[0], HpBios.GpuBoost[1], HpBios.GpuBoost[3] })
                && r.Win.Ac == Mode.Balanced && r.Win.Dc == Mode.Balanced,
                "exit from Eco: GPU back to Boost (Eco's record would stay after the app is gone), Windows power mode Balanced on both power sources");

            // the uninstall's firmware restore (after the app was ended in Performance, without its exit writes)
            SimFirmware ended = new SimFirmware(); ended.Profile = HpBios.ProfilePerformance; ended.Tdp = 82; ended.Gpu = (byte[])HpBios.GpuMax.Clone();
            Settings saved = new Settings(NewDir()); saved.BaselineTdp = 30; saved.FactoryGpu = new byte[] { 0, 1, 0, 87 };
            Setup.Restore(new HpBios(ended), saved, delegate(string s) { });
            Check(ended.Tdp == 30 && ended.Profile == HpBios.ProfileBalanced && Engine.Same(ended.Gpu, saved.FactoryGpu),
                "uninstall: concurrent TDP back to its baseline, profile 0x30 with the fans by the firmware, the GPU record read at the first start");
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0); r.Core.Exit();
            Check(r.Sim.Tdp == 30 && r.Sim.HostPl1 == 55 && r.Sim.Profile == 0x30, "exit in Performance: TDP back, PL 55, profile 0x30");
            Check(r.Sim.Gpu[0] == HpBios.GpuBoost[0] && r.Sim.Gpu[1] == HpBios.GpuBoost[1] && r.Sim.Gpu[3] == HpBios.GpuBoost[3], "exit in Performance: GPU record back to Boost (it does not lapse by itself)");
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0);
            r.Core.FailSafe("test");
            mark = r.Sim.Log.Count;
            r.Core.Exit();
            Check(r.Win.Ac == Mode.Balanced && r.Win.Dc == Mode.Balanced && Writes(r.Sim.Log, mark) == 0,
                "exit after a fail safe: the Windows power mode back to Balanced on both power sources, still no firmware write");

            // ---- Performance safety rules and conflicts
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0);
            r.Sim.IrTemp = 56;
            now = RunFor(r, 0, 5000);
            Check(Engine.Same(r.Sim.Gpu, new byte[] { 1, 0, 1, 75 }) && r.Sim.HostPl1 == 75, "governor (chassis 54): from GPU Max to GPS 75 + PPAB off, PL -5 W");
            r.Sim.IrTemp = 45;
            now = RunFor(r, now, 30000);
            Check(Engine.Same(r.Sim.Gpu, HpBios.GpuMax), "governor: below 49 the GPU goes back to Max");

            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0); now = 0;
            for (int k = 0; k < 6; k++) { r.Sim.HostPl1 = 55; now = RunFor(r, now, 45000); }
            Check(r.Core.Failed && r.Core.Effective == Mode.Balanced && !r.Core.InPerformance && r.Core.LastError.IndexOf("Another app took over", StringComparison.Ordinal) >= 0,
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
            Check(r.Core.Fault != null && r.Core.Fault.IndexOf("GPU setting not accepted", StringComparison.Ordinal) >= 0 && Count(r.Sim.Log, "type 34", mark) == 0, "a GPU preset the firmware does not take: fault shown, not retried in a loop");

            r = new Rig(); r.Core.Boot(true, 0); r.Sim.ErrorType = 33; r.Core.Select(Mode.Performance, 0);   // the GPU read-back fails once
            bool warned = r.Core.Fault != null;
            r.Sim.ErrorType = -1;
            RunFor(r, 0, 10000);
            Check(warned && r.Core.Fault == null && Engine.Same(r.Sim.Gpu, HpBios.GpuMax), "a failed GPU read-back: once a later read shows the record set, the warning goes");

            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0);
            now = RunFor(r, 0, 10000);
            r.Sim.ErrorType = 42;   // the power reads fail; the writes still go through
            RunFor(r, now, 150000);
            Check(r.Sim.HostPl1 == 80 && r.Sim.Profile == 0x04 && r.Core.InPerformance && r.Core.Fault == "Couldn't check the power",
                "heartbeat whose power read fails: the warning shows and Performance is still re-sent (not dropped by the firmware)");

            SimFirmware crashed = new SimFirmware(); crashed.Tdp = 82;
            r = new Rig(crashed, NewDir()); r.St.BaselineTdp = 30;
            Check(r.Core.Boot(true, 0) && crashed.Tdp == 30, "boot after a crash in Performance: concurrent TDP repaired to 30");
            // one rule for putting concurrent TDP back (HpBios.RestoreTdp): written when it differs or cannot be read
            SimFirmware unread = new SimFirmware(); unread.Tdp = 82; unread.ErrorType = 42;   // the power read fails
            r = new Rig(unread, NewDir()); r.St.BaselineTdp = 30; r.Core.Boot(true, 0);
            SimFirmware unreadGone = new SimFirmware(); unreadGone.Profile = HpBios.ProfilePerformance; unreadGone.Tdp = 82; unreadGone.ErrorType = 42;
            Settings unreadSaved = new Settings(NewDir()); unreadSaved.BaselineTdp = 30;
            Setup.Restore(new HpBios(unreadGone), unreadSaved, delegate(string s) { });
            Check(unread.Tdp == 30 && unread.Profile == HpBios.ProfileBalanced && unreadGone.Tdp == 30 && unreadGone.Profile == HpBios.ProfileBalanced,
                "concurrent TDP is put back even when the power read fails, at start-up and at uninstall (as when leaving Performance)");

            r = new Rig(); r.Sim.Tdp = 0; r.Core.Boot(true, 0);
            Rig odd = r;
            Check(!Throws(() => { odd.Core.Select(Mode.Performance, 0); odd.Core.Select(Mode.Balanced, 0); }) && odd.St.BaselineTdp == -1 && !odd.Core.Halted,
                "a concurrent TDP outside 20..82 is never kept as the baseline (leaving Performance would throw)");

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
            Check(r.Core.Boot(true, 0) && r.Core.Lapsing, "boot with a host limit still set: it is left to lapse first");
            now = RunFor(r, 0, 120000);
            Check(Count(r.Sim.Log, "type 26 ", 0) == 1 && Find(r.Sim.Log, "type 26 in FF-30-01-00", 0) >= 0 && Count(r.Sim.Log, "type 41 ", 0) == 0,
                "boot lapse: exactly one profile write (it hands the fans back if an older version held them), then nothing until the limit is gone");
            now = RunFor(r, now, 15000);
            Check(!r.Core.Lapsing && r.Sim.HostPl1 == 0 && r.Sim.HostPl2 == 0, "then the limit is gone");

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

            // ... and when Performance is left within that second, the 45 stays behind: it must not become the baseline
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Eco, 0);
            r.Sim.TdpAfterGpuWrite = 45; r.Sim.TdpResetDelayMs = 1000;
            r.Core.Select(Mode.Performance, 0); r.Core.Select(Mode.Balanced, 0);
            now = RunFor(r, 0, 200000);
            bool leftBehind = r.Sim.Tdp == 45;
            r.Core.Select(Mode.Performance, now); r.Core.Select(Mode.Balanced, now);
            Check(leftBehind && r.St.BaselineTdp == 30 && Settings.Load(r.Dir).BaselineTdp == 30 && r.Sim.Tdp == 30,
                "Performance left within a second of the GPU write: the firmware's later TDP 45 is never saved as the baseline (30 stays, and is put back)");

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
            Check(Writes(r.Sim.Log, mark) == 0, "mode changes during the lapse send nothing to the firmware");
            now = RunFor(r, now, Engine.LapseMaxMs - (now - left) + 10000);
            Check(!r.Core.Lapsing && r.Sim.HostPl1 == 0 && r.Sim.Profile == 0x30 && Engine.Same(r.Sim.Gpu, HpBios.GpuBoost),
                "the limit lapses anyway, then the last choice (Balanced) is applied");

            // ---- fail safe after an unexpected error
            r = new Rig(); r.Core.Boot(true, 0); r.Core.Select(Mode.Performance, 0); now = RunFor(r, 0, 30000);
            r.Core.FailSafe("test");
            Check(r.Core.Halted && r.Sim.Profile == 0x30 && r.Sim.Tdp == 30 && r.Sim.HostPl1 == 55 && Engine.Same(r.Sim.Gpu, HpBios.GpuBoost)
                && r.Core.Fault != null && r.Core.Fault.IndexOf("Stopped after an error", StringComparison.Ordinal) >= 0, "fail safe: Performance left, profile 0x30, GPU Boost, message shown");
            mark = r.Sim.Log.Count;
            now = RunFor(r, now, 600000);
            bool refused = !r.Core.Select(Mode.Eco, now);
            r.Unplug(now); r.Plug(now); r.Core.Resume(true, now); r.Core.Exit();
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
            long lapseSince = -1, perf04Since = -1, ticks = 0;
            long mem0 = -1, mem1 = 0;
            List<string> trail = new List<string>();
            long[] seen = new long[4];   // ticks in Performance, lapsing, on battery, failure shown
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
                        else if (ev < 0.0033) { long slept = 60000L * rnd.Next(1, 120); sim.Advance(slept); now += slept; r.Core.Resume(ac, now); }
                        sim.Advance(5000); now += 5000;
                        r.Core.Tick(now);
                        trail.Add(now / 1000 + " fail " + sim.FailAll + " cpu " + r.Sens.Cpu.ToString("0") + " calls " + sim.Log.Count
                            + " ka " + sim.KeepAliveMs / 1000 + " lapsing " + r.Core.Lapsing + " ev " + ev.ToString("0.0000"));
                        if (trail.Count > 30) trail.RemoveAt(0);

                        string why = null;
                        foreach (string line in sim.Log)   // the firmware always drives the fans: every profile write says so
                            if (line.IndexOf("type 26 in ", StringComparison.Ordinal) >= 0 && line.IndexOf("-01-00 (", StringComparison.Ordinal) < 0) why = "a profile write without fans by BIOS: " + line;
                        sim.Log.Clear();
                        if (sim.HostPl1 > 80 || sim.HostPl2 > 80 || (sim.HostPl1 != 0 && sim.HostPl1 < 45)) why = "PL outside 45..80";
                        else if (r.Core.InPerformance && !ac) why = "Performance on battery";
                        else if (r.St.SavedMode == Mode.Performance) why = "Performance saved";
                        lapseSince = r.Core.Lapsing ? (lapseSince < 0 ? now : lapseSince) : -1;
                        if (lapseSince >= 0 && now - lapseSince > Engine.LapseMaxMs + Engine.TickMs) why = "lapse longer than 180 s";
                        perf04Since = sim.Profile == HpBios.ProfilePerformance && !r.Core.InPerformance ? (perf04Since < 0 ? now : perf04Since) : -1;
                        if (perf04Since >= 0 && now - perf04Since > Engine.LapseMaxMs) why = "profile 0x04 without Performance for 3 minutes";
                        if (r.Core.InPerformance) seen[0]++;
                        if (r.Core.Lapsing) seen[1]++;
                        if (!ac) seen[2]++;
                        if (r.Core.Failed) seen[3]++;
                        if (why != null) { bad++; if (firstBad == null) { firstBad = "day " + day + " t " + now + ": " + why; foreach (string s in trail) Console.WriteLine(s); } }
                    }
                    r.Core.Exit();
                    if (Settings.Load(dir).SavedMode == Mode.Performance) { bad++; if (firstBad == null) firstBad = "Performance saved on disk"; }
                    sim.Advance(rnd.NextDouble() < 0.3 ? 20000 : 8 * 3600000L);   // an update restarts within seconds; otherwise the night
                    GC.Collect();
                    if (day == 0) mem0 = GC.GetTotalMemory(true); else mem1 = GC.GetTotalMemory(true);
                }
            }
            catch (Exception e) { bad++; firstBad = "exception: " + e.Message; }
            Check(bad == 0, "endurance: 14 simulated days (" + ticks + " ticks) keep every invariant, every profile write with fans by BIOS" + (firstBad == null ? "" : "; first: " + firstBad));
            bool all = true; foreach (long n in seen) if (n < 100) all = false;
            Check(all, "endurance: the run covered Performance, lapses, battery and failures ("
                + string.Join("/", Array.ConvertAll(seen, x => x.ToString())) + " ticks)");
            Check(mem1 - mem0 < 1024 * 1024,"endurance: memory does not grow from day 1 to day 14 (" + (mem1 - mem0) / 1024 + " KB)");
        }

        private static void SettingsTests()
        {
            string dir = NewDir();
            Settings s = Settings.Load(dir);
            Check(s.SavedMode == Mode.Balanced && s.BaselineTdp == -1 && s.FactoryGpu == null, "settings: defaults without a file");
            s.SavedMode = Mode.Eco; s.BaselineTdp = 30; s.FactoryGpu = new byte[] { 0, 1, 0, 87 };
            Check(s.Save(), "settings: save");
            Settings t = Settings.Load(dir);
            Check(t.SavedMode == Mode.Eco && t.BaselineTdp == 30 && Engine.Same(t.FactoryGpu, new byte[] { 0, 1, 0, 87 }), "settings: round trip");
            string file = Path.Combine(dir, "state.txt");
            File.WriteAllText(file, "savedMode=Performance\r\nbaselineTdp=200\r\nfactoryGpu=00-01-00-FF\r\ncurve=20,19,20,20,20,20,20\r\nfanPerformance=custom\r\nfanEco=firmware\r\n");
            t = Settings.Load(dir);
            Check(t.SavedMode == Mode.Balanced && t.BaselineTdp == -1 && t.FactoryGpu == null, "settings: out-of-range or unknown values are ignored");
            t.Save();
            string saved = File.ReadAllText(file);
            Check(saved.IndexOf("fan", StringComparison.Ordinal) < 0 && saved.IndexOf("curve", StringComparison.Ordinal) < 0,
                "settings: the fan choices and curve of an older version are dropped at the next save");
        }

        // ---------------------------------------------------------------- the window, the setup window and the tray menu

        private static void WindowTests()
        {
            SlotHistory h = new SlotHistory();
            long t0 = 1000L * 3600 * 24 * 20000;   // a wall-clock time on a slot boundary
            bool empty = h.Values().Length == 0 && double.IsNaN(h.Last);
            for (int i = 0; i < 6; i++) h.Add(t0 + i * 5000, i == 2 ? 70 : i == 4 ? double.NaN : 50);
            double[] v = h.Values();
            bool oneSlot = v.Length == 1 && v[0] == 70 && h.Last == 50;
            h.Add(t0 + 30000, double.NaN);                 // the next 30 s: a slot with no reading
            h.Add(t0 + 60000, 40);
            v = h.Values();
            bool next = v.Length == 3 && v[0] == 70 && double.IsNaN(v[1]) && v[2] == 40 && h.Last == 40;
            h.Add(t0 + 65000, 45);
            h.Add(t0 + 50000, 99);                          // the clock went back: counted in the newest slot
            v = h.Values();
            bool back = v.Length == 3 && v[2] == 99;
            h.Add(t0 + 60000 + 3 * SlotHistory.SlotMs + 1000, 60);   // three slots later: two empty ones between
            v = h.Values();
            bool holes = v.Length == 6 && v[2] == 99 && double.IsNaN(v[3]) && double.IsNaN(v[4]) && v[5] == 60;
            h.Add(t0 + 3600000 * 2L, 55);                   // two hours asleep: everything before is out of the window
            v = h.Values();
            bool slept = v.Length == SlotHistory.Keep && v[v.Length - 1] == 55 && double.IsNaN(v[0]) && double.IsNaN(v[v.Length - 2]);
            Check(empty && oneSlot && next && back && holes && slept && SlotHistory.Keep == ChipCard.SlotCount,
                "history: one slot per exactly 30 s of clock time, its highest reading; time without readings (sleep) leaves empty slots, so a slot's place says how long ago it was; as many slots as the cards show");
            Check(Th.TempLevelColor(85) == Th.Ok && Th.TempLevelColor(86) == Th.Warn && Th.TempLevelColor(95) == Th.Warn && Th.TempLevelColor(96) == Th.Danger
                && Th.RpmLevelColor(3500) == Th.Ok && Th.RpmLevelColor(3501) == Th.Warn && Th.RpmLevelColor(4200) == Th.Warn && Th.RpmLevelColor(4201) == Th.Danger
                && Th.TempLevelColor(double.NaN) == Th.NoReading && Th.RpmLevelColor(double.NaN) == Th.NoReading,
                "level colours: temperature green to 85, amber 86-95, red from 96 °C; fans green to 3500, amber 3501-4200, red from 4201 rpm; grey without a reading");
            bool hinted = true;
            // every warning the engine can raise, taken from a run of the simulator where it can, so a reworded message fails here
            Rig hr = new Rig(); hr.Core.Boot(false, 0); hr.Core.Select(Mode.Performance, 0); string charger = hr.Core.Fault;
            hr = new Rig(); hr.Core.Boot(true, 0); hr.Sim.FailAll = true; hr.Core.Select(Mode.Eco, 0); string notApplied = hr.Core.Fault;
            hr = new Rig(); hr.Core.Boot(true, 0); hr.Sim.IgnoreType = 41; hr.Core.Select(Mode.Performance, 0); string noPerf = hr.Core.Fault;
            hr = new Rig(); hr.Core.Boot(true, 0); hr.Core.FailSafe("test"); string stopped = hr.Core.Fault;
            foreach (string w in new string[] { charger, notApplied, noPerf, "Couldn't check the power", "Another app took over",
                "GPU setting not accepted", Identity.BiosWarning("F.14"), Identity.BiosWarning(""), stopped })
                if (Hints.For(w) == null) { hinted = false; Console.WriteLine("  no hint for: " + w); }
            Check(hinted && Hints.For(null) == null && charger == "Performance needs the charger" && notApplied == "Eco not applied yet"
                && noPerf == "Performance didn't start" && stopped == "Stopped after an error" && Identity.BiosWarning("F.14") == "New BIOS, not tested yet",
                "every warning the window can show is in plain words and has a 'what to do' line");
            bool fitsPill = true;
            foreach (float k in new float[] { 1.25f, 1f })
            {
                Th.Init(k);
                foreach (string w in new string[] { charger, notApplied, "Balanced not applied yet", "Performance not applied yet", noPerf, "Couldn't check the power",
                    "Another app took over", "GPU setting not accepted", Identity.BiosWarning("F.14"), Identity.BiosWarning(""), stopped, "Starting…", "All good" })
                {
                    int need = StatusPill.WidthFor(w, Hints.For(w) != null);
                    if (need > Th.D(MainForm.W - MainForm.PillLeft - 20)) { fitsPill = false; Console.WriteLine("  pill too narrow at " + k + " for: " + w + " (" + need + " px)"); }
                }
                int nameEnd = Th.HeaderNameX(20) + Th.Measure("Salva OPM", Th.Title).Width + Th.D(12);   // the window's 20 px edge; the pill's room never reaches the name
                if (Th.D(MainForm.PillLeft) < nameEnd) { fitsPill = false; Console.WriteLine("  pill room starts at " + Th.D(MainForm.PillLeft) + ", the name ends at " + nameEnd); }
            }
            Check(fitsPill, "status pill: every line fits the pill whole beside the name, no \"...\" (100 % and 125 %)");
            // the "i": a filled light disc with the letter cut out (the icon set's style), not a thin ring
            bool infoFilled;
            using (System.Drawing.Bitmap ib = new System.Drawing.Bitmap(40, 40))
            {
                using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(ib)) { gr.Clear(Th.Border); gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; StatusPill.InfoMark(gr, 4, 4, 32, Th.TextHi, Th.Border); }
                System.Drawing.Color body = ib.GetPixel(4 + 8, 4 + 16), stem = ib.GetPixel(4 + 16, 4 + 20), dotC = ib.GetPixel(4 + 16, 4 + 10), outside = ib.GetPixel(1, 1);
                infoFilled = body.ToArgb() == Th.TextHi.ToArgb() && stem.ToArgb() == Th.Border.ToArgb() && dotC.ToArgb() == Th.Border.ToArgb() && outside.ToArgb() == Th.Border.ToArgb();
            }
            Check(infoFilled, "status pill: the \"i\" is a filled light disc with the letter cut out, like the icon set");
            Check(MainForm.StatusText(false, null, false) == "Starting…" && MainForm.StatusText(true, null, false) == "Starting…" && MainForm.StatusText(true, null, true) == "All good"
                && MainForm.StatusText(true, "GPU setting not accepted", false) == "GPU setting not accepted" && MainForm.StatusText(false, null, true) == "Starting…",
                "status pill: 'Starting…' until start-up is done and the first reading is in (unless a warning comes first), then 'All good' or the warning");
            Check(App.RefusalText(App.Unreadable) == "The laptop's identity could not be read.\r\n\r\nSalva OPM only runs on the HP OMEN 16-ap0 with board 8E35 and will close now."
                && App.RefusalText("This is not the HP OMEN 16-ap0 with board 8E35 (X, Y).").EndsWith("only runs on that laptop and will close now."),
                "refusal box: an unreadable identity names the laptop it runs on; another laptop's box keeps 'that laptop'");
            Check(App.MayWrite(null, false, true) && App.MayWrite(null, true, false) && !App.MayWrite(null, false, false)
                && !App.MayWrite("This is not the HP OMEN 16-ap0 with board 8E35 (X, Y).", false, true) && !App.MayWrite(App.Unreadable, true, true),
                "firmware gate (start-up and exit): only once the laptop is known to be this one; never on another laptop or with an unknown identity");
            bool fonts = Th.LoadFonts();
            Th.Init(1f);
            Check(fonts && Th.Card.FontFamily.Name == "Plus Jakarta Sans" && Th.Card.Bold && Th.Title.FontFamily.Name == "Plus Jakarta Sans ExtraBold"
                && Th.Small.FontFamily.Name == "Plus Jakarta Sans SemiBold" && Th.Mono.FontFamily.Name == "Oswald SemiBold" && !Th.Mono.Bold,
                "theme fonts: the embedded Plus Jakarta Sans (SemiBold, Bold, ExtraBold) for the interface, Oswald SemiBold (its own weight, not a synthetic bold) for values");
            Check(Th.Sp("a b", Th.Small) == "a  b" && Th.Sp("a b", Th.Mono) == "a b",
                "theme spaces: the hair space goes before the space, so a wrapped line never starts with it; values keep plain spaces");
            bool centred = true;
            foreach (System.Drawing.Font f in new System.Drawing.Font[] { Th.Title, Th.Small, Th.Card, Th.Mono })
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(120, 100))
                {
                    using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(bmp))
                    {
                        gr.Clear(System.Drawing.Color.Black);
                        Th.TextMid(gr, "H", f, System.Drawing.Color.White, 10, 50);
                    }
                    int top = -1, bottom = -1;
                    for (int y = 0; y < bmp.Height; y++)
                        for (int x = 0; x < bmp.Width; x++)
                            if (bmp.GetPixel(x, y).G > 128) { if (top < 0) top = y; bottom = y; break; }
                    if (top < 0 || Math.Abs((top + bottom + 1) / 2.0 - 50) > 1) { centred = false; Console.WriteLine("  off centre: " + f.Name + " " + top + ".." + bottom); }
                }
            Check(centred, "one centre line: the capitals of every theme font are centred within 1 px of the line they are placed on");
            bool topped = true;
            foreach (System.Drawing.Font f in new System.Drawing.Font[] { Th.Card, Th.Stack })
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(160, 100))
                {
                    using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(bmp))
                    {
                        gr.Clear(System.Drawing.Color.Black);
                        Th.TextTop(gr, "H8", f, System.Drawing.Color.White, 10, 30);
                    }
                    int first = -1;
                    for (int y = 0; y < bmp.Height && first < 0; y++) for (int x = 0; x < bmp.Width; x++) if (bmp.GetPixel(x, y).G > 128) { first = y; break; }
                    if (Math.Abs(first - 30) > 1) { topped = false; Console.WriteLine("  text top " + f.Name + " " + f.Size + ": " + first); }
                }
            foreach (string kind in new string[] { "cpu", "gpu" })
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(60, 60))
                {
                    using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(bmp))
                    {
                        gr.Clear(System.Drawing.Color.Black);
                        Th.IconTopLeft(gr, kind, 10, 20, 26, Th.Accent);
                    }
                    int first = -1, left = 99;
                    for (int y = 0; y < bmp.Height; y++) for (int x = 0; x < bmp.Width; x++)
                        if (bmp.GetPixel(x, y).G >= Th.Accent.G / 2) { if (first < 0) first = y; left = Math.Min(left, x); }
                    if (Math.Abs(first - 20) > 1 || Math.Abs(left - 10) > 1) { topped = false; Console.WriteLine("  icon corner " + kind + ": " + left + "," + first); }
                }
            int stackGap = ChipCard.ReadingsHeight - 2 * Th.CapHeight(Th.Stack);
            Check(stackGap >= Th.D(2) && stackGap <= Th.D(6),
                "chip cards: the readings' two lines (temperature over fan speed) fill their block with a 2-6 px gap between them (never touching)");
            Check(topped, "one top edge: text placed by its top starts within 1 px of that line; chip icons placed by their top-left corner start within 1 px of it (the card's name, icon and readings)");
            bool measured = true;
            foreach (System.Drawing.Font f in new System.Drawing.Font[] { Th.Title, Th.Card, Th.Mono })
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(300, 100))
                {
                    using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(bmp))
                    {
                        gr.Clear(System.Drawing.Color.Black);
                        Th.Text(gr, "62°C", f, System.Drawing.Color.White, 10, 10);
                    }
                    int right = -1;
                    for (int x = 0; x < bmp.Width; x++) for (int y = 0; y < bmp.Height; y++) if (bmp.GetPixel(x, y).G > 100) { right = x; break; }
                    int w = Th.Measure("62°C", f).Width;   // the advance ends a few px after the ink (the C's side bearing)
                    if (right < 0 || 10 + w < right || 10 + w > right + Th.D(6)) { measured = false; Console.WriteLine("  measure " + f.Name + ": " + w + ", ink to " + (right - 10)); }
                }
            Check(measured, "text width: Measure matches the drawn text (right-aligned and centred values sit on their line)");
            bool cardFits;
            using (ChipCard cc = new ChipCard())
            {
                cc.Size = new System.Drawing.Size(MainForm.CardW, ChipCard.CardHeight);
                cc.Title = "GPU";
                cardFits = ChipCard.SlotCount == 20 && cc.LineLeft >= Th.D(16) && cc.LineRight == MainForm.CardW - Th.D(16) && ChipCard.LineHeight == Th.D(6)
                    && cc.ReadingsLeft >= cc.NameRight + Th.D(12) && cc.LineLeft == Th.D(16 + 22 + 10);
            }
            bool chipRow = true;
            string[] chipKinds = { "cpu", "gpu" };
            foreach (float scale in new float[] { 1.25f, 1f })
            {
                Th.Init(scale);
                int[] mids = new int[2];
                for (int k = 0; k < 2; k++)
                    using (ChipCard cc = new ChipCard())
                    {
                        cc.Size = new System.Drawing.Size(Th.D(MainForm.CardW), Th.D(ChipCard.CardHeight));
                        cc.IconName = chipKinds[k];
                        cc.Title = chipKinds[k].ToUpperInvariant();
                        System.Drawing.RectangleF c = cc.Chip;
                        mids[k] = cc.NameMid;
                        int nameInk = cc.NameLeft + Th.TextInk(cc.Title, Th.Card, false).Left;   // where the name's letters start
                        if (Math.Abs(c.X - Th.D(16)) > 0.5 || Math.Abs(c.Y - Th.D(16)) > 0.5 || c.Width != Th.D(24) || c.Height != Th.D(24) || nameInk != (int)Math.Round(c.Right) + Th.D(10))
                        { chipRow = false; Console.WriteLine("  chip " + chipKinds[k] + " at " + scale + ": " + c + ", name's ink at " + nameInk); }
                    }
                if (mids[0] != mids[1]) { chipRow = false; Console.WriteLine("  chip names at " + scale + ": " + mids[0] + " and " + mids[1]); }
            }
            Check(chipRow,
                "chip cards: the CPU and GPU icons' drawings are 24 x 24 in the top left padding corner; each name's letters start 10 px after its icon, centred on it, on one line across both cards (100 % and 125 %)");
            float modeW = Seg.CardWidth(MainForm.W - 40, 3);
            Check(Seg.GroupWidth("Performance") <= modeW - Th.D(20) && Seg.GroupWidth("Eco (auto)") <= modeW - Th.D(20),
                "window width: every mode card's icon and name (the icon above the name) fit inside it with at least 10 px each side");
            double shape = (double)MainForm.W / MainForm.H;
            Check(shape >= 1.0 && shape <= 1.25,
                "window shape: about as tall as wide (width / height " + shape.ToString("0.00") + ", 1.00-1.25), the CPU card over the GPU card");
            Check(MainForm.ModesTop - (MainForm.HeaderMid + 15) == 24 && MainForm.CardsTop == MainForm.ModesTop + 84 + 16,
                "window rows: the header has 24 px of room under it, the other blocks 16 px between them");
            System.Drawing.Color gr0 = Th.Ok, am = Th.Warn, no = Th.NoReading;
            List<int[]> st = ChipCard.Stretches(new System.Drawing.Color[] { gr0, gr0, am, am, am, gr0, no });
            Check(cardFits && st.Count == 4 && st[0][0] == 0 && st[0][1] == 2 && st[1][1] == 5 && st[3][0] == 6 && st[3][1] == 7
                && ChipCard.Stretches(new System.Drawing.Color[0]).Count == 0,
                "chip cards: two 6 px history lines of 20 slots (10 minutes), inside the card's 16 px padding, 10 px after their own icon column; the readings clear the name; neighbours of one colour drawn as one stretch");
            Check(Seg.ItemColor(true, false, false) == Th.TextHi && Seg.ItemColor(false, false, true) == Th.TextHi && Seg.ItemColor(false, false, false) == Th.Sub
                && Seg.ItemColor(false, true, true) == Th.Section, "mode cards: one colour for icon and name (bright when chosen or hovered, dim when disabled)");
            bool icons = true;
            foreach (string kind in new string[] { "leaf", "scale", "bolt", "thermo", "fan", "cpu", "gpu" })
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(40, 40))
                {
                    using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(bmp))
                    {
                        gr.Clear(System.Drawing.Color.Black);
                        Th.Icon(gr, kind, new System.Drawing.RectangleF(5, 5, 30, 30), Th.Accent);
                    }
                    int solid = 0, outside = 0, lighter = 0, top = 99, bottom = -1, left = 99, right = -1;
                    double wx = 0, wy = 0, w = 0;
                    for (int y = 0; y < 40; y++)
                        for (int x = 0; x < 40; x++)
                        {
                            System.Drawing.Color p = bmp.GetPixel(x, y);
                            bool inBox = x >= 5 && x < 35 && y >= 5 && y < 35;
                            if (Math.Abs(p.R - Th.Accent.R) <= 6 && Math.Abs(p.G - Th.Accent.G) <= 6 && Math.Abs(p.B - Th.Accent.B) <= 6) solid++;   // full strength, not see-through
                            if (!inBox && (p.R | p.G | p.B) != 0) outside++;
                            if (p.R > Th.Accent.R || p.G > Th.Accent.G || p.B > Th.Accent.B) lighter++;   // on black nothing can be lighter than the colour
                            if (p.G >= Th.Accent.G / 2) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); left = Math.Min(left, x); right = Math.Max(right, x); }
                            wx += p.G * (x + 0.5); wy += p.G * (y + 0.5); w += p.G;
                        }
                    // the middle the eye sees: halfway between the outline's middle and the weight's middle
                    double cx = ((left + right + 1) / 2.0 + wx / w) / 2, cy = ((top + bottom + 1) / 2.0 + wy / w) / 2;
                    if (solid < 25 || outside > 0 || lighter > 0 || Math.Abs(cx - 20) > 1 || Math.Abs(cy - 20) > 1)
                    { icons = false; Console.WriteLine("  icon " + kind + ": " + solid + " solid px, " + outside + " outside its box, " + lighter + " lighter than its colour, middle at " + cx + "," + cy); }
                }
            Check(icons, "icons: all seven are embedded and draw in the colour asked for (no edge pixel lighter, e.g. pure white), inside their box, the middle the eye sees within 1 px of the box's (in line with its text)");
            Func<System.Drawing.Color, double> lum = delegate(System.Drawing.Color c) { return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B); };
            double greyContrast = (lum(Th.NoReading) + 0.05) / (lum(Th.Surface) + 0.05);
            Check(Th.NoReading != Th.Ok && Th.Ok != Th.Warn && Th.Warn != Th.Danger && greyContrast >= 2.5,
                "theme colours: grey, green, amber and red differ, and a slot without a reading shows on a card (contrast " + greyContrast.ToString("0.00") + ":1, at least 2.5)");
            // the tray diamond: Performance in the app's own lavender, and no mode in red (red means danger in the window)
            bool trayColours = App.ModeColours.Length == 3 && App.ModeColours[2][0].ToArgb() == Th.LogoLight.ToArgb() && App.ModeColours[2][1].ToArgb() == Th.LogoDark.ToArgb();
            foreach (System.Drawing.Color[] mc in App.ModeColours)
                foreach (System.Drawing.Color c in mc) if (c.R > c.G + 60 && c.R > c.B + 60) { trayColours = false; Console.WriteLine("  tray colour is red: " + c); }
            Check(trayColours, "tray: the diamond shows the mode in green, grey and lavender (Performance in the app's own colour), never in red");
            // level colours on the card: next-door levels must stay apart (OKLab distance x100)
            double worstNormal = Math.Min(Oklab(Th.Ok, Th.Warn), Oklab(Th.Warn, Th.Danger));
            Check(worstNormal >= 15, "level colours: green, amber and red stay apart (OKLab distance " + worstNormal.ToString("0.0") + ")");
            // one colour per job: no near-twins in the theme (the palette had pairs 2.1 apart, the background and a pressed card's fill)
            double closest = double.MaxValue; string twins = "";
            System.Reflection.FieldInfo[] themed = typeof(Th).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            for (int i = 0; i < themed.Length; i++)
                for (int j = i + 1; j < themed.Length; j++)
                    if (themed[i].FieldType == typeof(System.Drawing.Color) && themed[j].FieldType == typeof(System.Drawing.Color))
                    {
                        double d = Oklab((System.Drawing.Color)themed[i].GetValue(null), (System.Drawing.Color)themed[j].GetValue(null));
                        if (d < closest) { closest = d; twins = themed[i].Name + " and " + themed[j].Name; }
                    }
            Check(closest >= 4.5, "theme colours: no near-twins, one colour per job (closest: " + twins + ", OKLab " + closest.ToString("0.0") + ")");
            Th.Init(1.25f);
            bool pixels = Th.Small.Unit == System.Drawing.GraphicsUnit.Pixel && Math.Abs(Th.Small.Size - 9.75f * 96f / 72f * 1.25f) < 0.01f
                && Th.Mono.Unit == System.Drawing.GraphicsUnit.Pixel && Th.D(100) == 125;
            Th.Init(1f);   // the window checks below use the 96 DPI layout
            Check(pixels, "window scale: fonts in pixels for the screen's own scale (125 %: 9.75 pt -> 16.25 px), layout scaled with them");
            // a paint that throws closes the window: the cards must draw any data (none yet, part filled, gaps, out of range)
            bool painted = true;
            foreach (int n in new int[] { 0, 1, 2, 9, 40 })
                using (ChipCard cc = new ChipCard())
                {
                    cc.TempDots = new double[n]; cc.RpmDots = new double[n];
                    for (int i = 0; i < n; i++) { cc.TempDots[i] = i % 7 == 3 ? double.NaN : 30 + 3 * i; cc.RpmDots[i] = i % 5 == 1 ? double.NaN : 150 * i; }
                    cc.Temp = n == 0 ? double.NaN : n * 4; cc.Rpm = n == 0 ? double.NaN : n * 150;   // up to 160 °C and 6000 rpm: past the level colours' range
                    HoverOn(cc, n % 2, (n * 7) % ChipCard.SlotCount);                                  // a hover on either line, on any slot, filled or not
                    cc.Asleep = n == 9;
                    try { Painted(cc, 352, ChipCard.CardHeight).Dispose(); }
                    catch (Exception e) { painted = false; Console.WriteLine("  paint failed: n " + n + ": " + (e.InnerException ?? e).Message); }
                }
            using (ChipCard hc = new ChipCard())
            {
                hc.Size = new System.Drawing.Size(MainForm.CardW, ChipCard.CardHeight);
                hc.TempDots = new double[] { 70, double.NaN, 96.4 };
                hc.RpmDots = new double[] { 1900 };
                string[] newest = hc.HoverText(0, ChipCard.SlotCount - 1), gap = hc.HoverText(0, ChipCard.SlotCount - 2), early = hc.HoverText(1, 0), fan = hc.HoverText(1, ChipCard.SlotCount - 1);
                Check(ChipCard.AgeText(0) == "now" && ChipCard.AgeText(1) == "30s ago" && ChipCard.AgeText(4) == "2min ago" && ChipCard.AgeText(5) == "2min 30s ago"
                    && newest[0] == "96°C" && newest[1] == "now" && gap[0] == "No reading" && gap[1] == "30s ago" && early[0] == "No reading" && early[1] == "9min 30s ago"
                    && fan[0] == "1900 RPM",
                    "history hover: the slot under the mouse shows its value ('96°C', '1900 RPM' or 'No reading') and how long ago ('2min 30s ago'), 30 s per slot");
            }
            // the hovered slot comes out of the line (no dot): its own stretch grows to 8 px, square inside the line, its outer
            // side round only on the line's two ends; drawn after its growth (hover moved there a second ago)
            Func<int, System.Drawing.Bitmap> hoverAt = delegate(int slot)
            {
                using (ChipCard bc = new ChipCard())
                {
                    bc.Title = "CPU"; bc.IconName = "cpu";
                    bc.TempDots = new double[ChipCard.SlotCount];
                    for (int i = 0; i < bc.TempDots.Length; i++) bc.TempDots[i] = 70;   // one green line
                    HoverOn(bc, 0, slot);
                    return Painted(bc, MainForm.CardW, ChipCard.CardHeight);
                }
            };
            bool barOk = true;
            using (ChipCard gc = new ChipCard())
            {
                gc.Size = new System.Drawing.Size(MainForm.CardW, ChipCard.CardHeight);
                float sw = (gc.LineRight - gc.LineLeft) / ChipCard.SlotCount;
                int yb = gc.Row1Mid - Th.D(ChipCard.HoverBarH) / 2;   // a row of the bar above the line
                // {slot, x, inked?}: the bar's own corners and the card beside it
                int last = ChipCard.SlotCount - 1;
                float m0 = gc.LineLeft + sw * 10, l0 = gc.LineLeft + sw * last;
                object[][] probes = {
                    new object[] { 10, (int)Math.Ceiling(m0), true }, new object[] { 10, (int)Math.Floor(m0 + sw) - 1, true },        // square inner corners
                    new object[] { 10, (int)Math.Floor(m0) - 2, false }, new object[] { 10, (int)Math.Ceiling(m0 + sw) + 1, false },  // nothing beside the slot
                    new object[] { last, (int)Math.Ceiling(l0), true }, new object[] { last, (int)Math.Floor(gc.LineRight) - 1, false }, // the line's right end: round outside
                    new object[] { 0, (int)Math.Floor(gc.LineLeft + sw) - 1, true }, new object[] { 0, (int)Math.Ceiling(gc.LineLeft), false } }; // the left end
                foreach (object[] pr in probes)
                    using (System.Drawing.Bitmap bmp = hoverAt((int)pr[0]))
                    {
                        System.Drawing.Color px = bmp.GetPixel((int)pr[1], yb);
                        bool inked = px.ToArgb() == Th.TempLevelColor(70).ToArgb(), clear = px.ToArgb() == Th.Surface.ToArgb();   // whole pixels: the bar's colour or the card's
                        if ((bool)pr[2] ? !inked : !clear) { barOk = false; Console.WriteLine("  hover bar: slot " + pr[0] + ", x " + pr[1] + ": " + px + (((bool)pr[2]) ? ", want the bar" : ", want the card")); }
                    }
            }
            Check(barOk, "history hover: the hovered slot comes out of the line as an 8 px stretch, square inside the line and round only on the outer side at its two ends");
            bool grows = Math.Abs(ChipCard.HoverBarHeight(0, true) - ChipCard.LineHeight) < 0.01f && Math.Abs(ChipCard.HoverBarHeight(ChipCard.HoverGrowMs, true) - Th.D(ChipCard.HoverBarH)) < 0.01f
                && Math.Abs(ChipCard.HoverBarHeight(0, false) - Th.D(ChipCard.HoverBarH)) < 0.01f && ChipCard.HoverBarHeight(ChipCard.HoverGrowMs / 2, true) > ChipCard.LineHeight;
            bool growStops;
            using (ChipCard tc = new ChipCard())
            {
                tc.TempDots = new double[] { 60 };
                System.Windows.Forms.Timer timer = tc._anim;
                HoverOn(tc, 0, 0);   // grown a second ago
                timer.Start(); typeof(System.Windows.Forms.Timer).GetMethod("OnTick", NonPublic).Invoke(timer, new object[] { EventArgs.Empty });
                growStops = !timer.Enabled;
            }
            Check(grows && growStops, "history hover: the slot grows out of the line in " + ChipCard.HoverGrowMs + " ms (at once without Windows animations), and its timer stops once it is out");
            // the hovered slot is the one drawn under the pointer, on its first and last pixel column (slot edges are whole pixels)
            bool hoverHits = true;
            foreach (float k in new float[] { 1f, 1.25f })
            {
                Th.Init(k);
                using (ChipCard hh = new ChipCard())
                {
                    hh.Size = new System.Drawing.Size(Th.D(MainForm.CardW), Th.D(ChipCard.CardHeight));
                    hh.TempDots = new double[ChipCard.SlotCount];
                    int y = hh.Row1Mid;
                    System.Reflection.MethodInfo move = typeof(System.Windows.Forms.Control).GetMethod("OnMouseMove", NonPublic);
                    for (int i = 0; i < ChipCard.SlotCount; i++)
                        foreach (int x in new int[] { (int)hh.SlotX(i), (int)hh.SlotX(i + 1) - 1 })
                        {
                            move.Invoke(hh, new object[] { new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None, 0, x, y, 0) });
                            int got = hh._hoverSlot;
                            if (got != i) { hoverHits = false; Console.WriteLine("  hover at x " + x + " (" + k + "): slot " + got + ", drawn there: " + i); }
                        }
                }
            }
            Th.Init(1f);
            Check(hoverHits, "history hover: the slot picked is the one drawn under the pointer, at every slot edge (100 % and 125 %)");
            // drawn edges sit on whole pixels (crisp): across a straight edge each pixel is one side's colour, none half-covered
            bool crisp = true;
            float scaleNow = 1f;
            bool groupsCentred = true;
            Action<string, System.Drawing.Bitmap, System.Drawing.Rectangle, int> edge = delegate(string what, System.Drawing.Bitmap b, System.Drawing.Rectangle r, int most)
            {
                System.Collections.Generic.List<int> shades = new System.Collections.Generic.List<int>();
                for (int y = r.Top; y < r.Bottom; y++) for (int x = r.Left; x < r.Right; x++) if (!shades.Contains(b.GetPixel(x, y).ToArgb())) shades.Add(b.GetPixel(x, y).ToArgb());
                if (shades.Count > most) { crisp = false; Console.WriteLine("  soft edge: " + what + " at " + scaleNow + ": " + shades.Count + " shades, want " + most); }
            };
            foreach (float k in new float[] { 1f, 1.25f })
            {
                Th.Init(k); scaleNow = k;
                using (ChipCard cc = new ChipCard())
                {
                    cc.Title = "CPU"; cc.IconName = "cpu"; cc.TempDots = new double[ChipCard.SlotCount];
                    for (int i = 0; i < ChipCard.SlotCount; i++) cc.TempDots[i] = i < 10 ? 70 : 90;   // green, then amber
                    HoverOn(cc, 0, 5);
                    using (System.Drawing.Bitmap b = Painted(cc, Th.D(MainForm.CardW), Th.D(ChipCard.CardHeight)))
                    {
                        int mid = cc.Row1Mid, d8 = Th.D(8);
                        float sw = (cc.LineRight - cc.LineLeft) / ChipCard.SlotCount;
                        edge("history line", b, new System.Drawing.Rectangle((int)(cc.LineLeft + sw * 14.5f), mid - d8, 1, 2 * d8), 2);
                        edge("hover bar", b, new System.Drawing.Rectangle((int)(cc.LineLeft + sw * 5.5f), mid - d8, 1, 2 * d8), 2);
                        edge("hover bar's side", b, new System.Drawing.Rectangle((int)(cc.LineLeft + sw * 5) - 3, mid - Th.D(ChipCard.HoverBarH) / 2, 7, 1), 2);
                    }
                }
                using (Seg sg = new Seg())
                {
                    sg.Items = new string[] { "Eco", "Balanced", "Performance" }; sg.Icons = new string[] { "leaf", "scale", "bolt" }; sg.Index = 1;
                    using (System.Drawing.Bitmap b = Painted(sg, Th.D(MainForm.W - 40), Th.D(84)))
                    {
                        float gap = Th.D(Seg.Gap), cw = Seg.CardWidth(b.Width, 3);
                        edge("selected mode card's ring", b, new System.Drawing.Rectangle(b.Width / 2, 0, 1, Th.D(6)), 2);
                        edge("first mode card's right side", b, new System.Drawing.Rectangle((int)cw - 3, b.Height / 2, 7, 1), 3);
                        edge("last mode card's left side", b, new System.Drawing.Rectangle((int)(2 * (cw + gap)) - 3, b.Height / 2, 7, 1), 3);
                        // each card's icon and name, as drawn (from the drawing's top to the name's last row), centred in the card
                        for (int i = 0; i < 3; i++)
                        {
                            System.Drawing.Color ink = i == 1 ? Th.TextHi : Th.Sub;
                            int x0 = (int)Math.Round(i * (cw + gap)) + Th.D(16), x1 = (int)Math.Round(i * (cw + gap) + cw) - Th.D(16), first = -1, last = -1;
                            for (int y = Th.D(4); y < b.Height - Th.D(4); y++)
                                for (int x = x0; x < x1; x++)
                                {
                                    System.Drawing.Color p = b.GetPixel(x, y);
                                    if (Math.Abs(p.R - ink.R) <= 40 && Math.Abs(p.G - ink.G) <= 40 && Math.Abs(p.B - ink.B) <= 40) { if (first < 0) first = y; last = y; break; }
                                }
                            double off = (first + last + 1) / 2.0 - b.Height / 2.0;   // the card's rows run 0 to Height - 1
                            if (first < 0 || Math.Abs(off) > 1) { groupsCentred = false; Console.WriteLine("  mode card " + i + " at " + k + ": icon and name rows " + first + ".." + last + ", " + off + " px off the middle"); }
                        }
                    }
                }
                using (StatusPill sp = new StatusPill())
                {
                    sp.Text = "All good";
                    using (System.Drawing.Bitmap b = Painted(sp, Th.D(MainForm.W - MainForm.PillLeft - 20), Th.D(30)))
                    {
                        edge("status pill's top", b, new System.Drawing.Rectangle(b.Width - Th.D(40), 0, Th.D(20), 2), 1);
                        edge("status pill's bottom", b, new System.Drawing.Rectangle(b.Width - Th.D(40), b.Height - 2, Th.D(20), 2), 1);
                    }
                }
            }
            Th.Init(1f);
            Check(crisp, "drawn edges sit on whole pixels: the history lines, the hover bar, the selected mode card's ring, the mode cards' sides and the status pill have no half-covered rows (100 % and 125 %)");
            Check(groupsCentred, "mode cards: each card's icon and name, as drawn, sit within 1 px of the card's middle (100 % and 125 %)");
            // the readings in their three states: awake (each line ending on the right padding, units as tall as the numbers, each starting on its
            // number's top row), asleep ("Asleep" to the right padding, no unit), nothing read yet (placeholders, units kept)
            bool stacked = false, asleepShown = false, skeleton = false;
            Func<int, ChipCard> readingsCard = delegate(int state)
            {
                ChipCard c = new ChipCard();
                c.Title = "GPU"; c.IconName = "gpu"; c.Temp = 68; c.Rpm = 2500; c.Asleep = state == 1;
                if (state != 2) { c.TempDots = new double[] { 68 }; c.RpmDots = new double[] { 2500 }; }
                return c;
            };
            foreach (int state in new int[] { 0, 1, 2 })
                using (ChipCard ac = readingsCard(state))
                using (System.Drawing.Bitmap bmp = Painted(ac, MainForm.CardW, ChipCard.CardHeight))
                {
                    // the ink box {left, top, right, bottom} of pixels at least this bright (sum of R, G, B) in a region; left -1: none
                    Func<int, int, int, int, int, int[]> inkBox = delegate(int x0, int x1, int y0, int y1, int bright)
                    {
                        int[] r = { -1, -1, -1, -1 };
                        for (int y = y0; y < y1; y++)
                            for (int x = x0; x < x1; x++)
                            {
                                System.Drawing.Color p = bmp.GetPixel(x, y);
                                if (p.R + p.G + p.B < bright) continue;
                                if (r[0] < 0 || x < r[0]) r[0] = x;
                                if (r[1] < 0) r[1] = y;
                                r[2] = Math.Max(r[2], x); r[3] = y;
                            }
                        return r;
                    };
                    int cap = Th.CapHeight(Th.Stack), from = (int)ac.ReadingsLeft, right = MainForm.CardW - Th.D(16);
                    int[] rows0 = { ac.LineTop(0) - 2, ac.LineTop(1) - 1 }, rows1 = { ac.LineTop(1) - 1, ac.LineTop(1) + cap + 3 };
                    System.Drawing.RectangleF u0 = ac.UnitBox(0), u1 = ac.UnitBox(1);
                    int s0 = (int)u0.X - Th.D(2), s1 = (int)u1.X - Th.D(2);   // between each number and its unit
                    int[] n0 = inkBox(from, s0, rows0[0], rows0[1], 250), n1 = inkBox(from, s1, rows1[0], rows1[1], 250);
                    int[] m0 = inkBox(s0, right + 2, rows0[0], rows0[1], 250), m1 = inkBox(s1, right + 2, rows1[0], rows1[1], 250);
                    if (state == 0)
                    {
                        stacked = n0[0] >= 0 && n1[0] >= 0 && m0[0] >= 0 && m1[0] >= 0
                            && m0[2] == m1[2] && Math.Abs(m0[2] - (right - 1)) <= 1                // both lines end on the right padding, where the history lines end
                            && Math.Abs((m0[3] - m0[1]) - (n0[3] - n0[1])) <= 1                    // each unit as tall as its number
                            && Math.Abs((m0[3] - m0[1]) - (m1[3] - m1[1])) <= 1                    // one height ("RPM" wider, at its own width)
                            && m0[1] == n0[1] && m1[1] == n1[1]                                     // each unit starts on its number's top row
                            && m0[0] - n0[2] == m1[0] - n1[2]                                       // one gap between number and unit on both lines
                            && n0[2] < m0[0] && m0[0] - n0[2] <= Th.D(6);                          // close to it, not touching
                        // nothing of either unit shows past the padding (where the history lines end), not even ClearType's coloured edge
                        for (int y = (int)u0.Y; y < (int)u1.Bottom + 1; y++)
                        {
                            System.Drawing.Color p = bmp.GetPixel(right, y);
                            if (p.ToArgb() != Th.Surface.ToArgb()) { stacked = false; Console.WriteLine("  readings: a unit shows past the padding at x " + right + ", y " + y + ": " + p); break; }
                        }
                        if (!stacked) Console.WriteLine("  readings: numbers " + string.Join(",", n0) + " / " + string.Join(",", n1) + ", units " + string.Join(",", m0) + " / " + string.Join(",", m1));
                    }
                    else if (state == 1)
                    {
                        int[] line0 = inkBox(from, right + 2, rows0[0], rows0[1], 250), bright0 = inkBox(from, right + 2, rows0[0], rows0[1], 600);
                        asleepShown = line0[0] >= 0 && line0[2] >= right - Th.D(3) && bright0[0] < 0 && n1[0] >= 0;   // grey "Asleep" up to the padding; the fan speed stays
                        for (int y = rows0[0]; y < rows0[1]; y++)   // and nothing of it past the padding, where the history lines end
                            if (bmp.GetPixel(right, y).ToArgb() != Th.Surface.ToArgb()) { asleepShown = false; Console.WriteLine("  asleep: shows past the padding at y " + y + ": " + bmp.GetPixel(right, y)); break; }
                        if (!asleepShown) Console.WriteLine("  asleep: line " + string.Join(",", line0) + ", bright " + string.Join(",", bright0));
                    }
                    else
                    {
                        int shimmer = 0;
                        for (int y = rows0[0]; y < rows1[1]; y++)
                            for (int x = from; x < s1; x++)
                            {
                                System.Drawing.Color p = bmp.GetPixel(x, y);
                                int sum = p.R + p.G + p.B;
                                if (sum >= Th.Border.R + Th.Border.G + Th.Border.B - 3 && sum < 250) shimmer++;
                            }
                        System.Drawing.Color lineMid = bmp.GetPixel((int)ac.LineLeft + Th.D(20), ac.Row1Mid);
                        int lm = lineMid.R + lineMid.G + lineMid.B;
                        skeleton = ac.Loading && n0[0] < 0 && n1[0] < 0 && shimmer > 2 * cap && m0[0] >= 0 && m1[0] >= 0
                            && lm >= Th.Border.R + Th.Border.G + Th.Border.B - 3 && lineMid.ToArgb() != Th.NoReading.ToArgb() && lm < 250;
                        if (!skeleton) Console.WriteLine("  skeleton: numbers " + string.Join(",", n0) + " / " + string.Join(",", n1) + ", placeholder px " + shimmer + ", units " + m0[0] + " / " + m1[0] + ", line " + lineMid);
                    }
                }
            // hovering a line before the first reading shows no pill over the placeholders (the pill's text is the only bright ink
            // right of the name on a loading card)
            bool noEarlyPill;
            ChipCard loadingCard = CpuCard(new double[0]);
            HoverOn(loadingCard, 0, ChipCard.SlotCount - 1);
            using (ChipCard pc = loadingCard)
            using (System.Drawing.Bitmap bmp = Painted(pc, MainForm.CardW, ChipCard.CardHeight))
            {
                int bright = 0;
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = (int)pc.NameRight + Th.D(4); x < bmp.Width; x++) { System.Drawing.Color p = bmp.GetPixel(x, y); if (p.R + p.G + p.B > 600) bright++; }
                noEarlyPill = pc.Loading && bright == 0;
                if (!noEarlyPill) Console.WriteLine("  hover before the first reading: " + bright + " bright px");
            }
            Check(noEarlyPill, "chip cards: hovering a history line before the first reading shows no value pill over the placeholders");
            Check(stacked, "chip cards: the temperature over the fan speed, each line ending on the right padding (where the history lines end), each unit as tall as its number and on its number's top row, one gap between number and unit");
            Check(asleepShown, "chip cards: a sleeping GPU says 'Asleep' (grey, no unit) up to the right padding and not a pixel past it; the fan speed stays");
            Check(skeleton, "chip cards: before the first reading, placeholders instead of numbers and history lines; the units stay");
            Check(painted, "chip cards: any history (none yet, part filled, gaps, very high readings) paints without an exception");
            bool rising = true;
            for (int k = 1; k <= 20; k++) if (Th.EaseOut(k / 20.0) < Th.EaseOut((k - 1) / 20.0)) rising = false;
            Check(Th.EaseOut(0) == 0 && Th.EaseOut(1) == 1 && Th.EaseOut(-1) == 0 && Th.EaseOut(2) == 1 && Th.EaseOut(0.5) > 0.9 && rising,
                "window motion: a strong ease-out from 0 to 1 (most of the way at half time), clamped, never going back");
            // the animation timers stop by themselves once the motion is over: a minimized window gets no paint, and a stop in
            // OnPaint alone kept them ticking until the window was restored
            System.Reflection.MethodInfo onTick = typeof(System.Windows.Forms.Timer).GetMethod("OnTick", NonPublic);
            bool shineRuns, shineStops, fadeStops;
            using (ChipCard tc = new ChipCard())
            {
                System.Windows.Forms.Timer timer = tc._anim;
                timer.Start(); onTick.Invoke(timer, new object[] { EventArgs.Empty });   // nothing read yet: the skeleton's light moves (when Windows animates)
                shineRuns = timer.Enabled == Th.Motion;
                tc.TempDots = new double[] { 60 };                                       // read: nothing moves any more
                timer.Start(); onTick.Invoke(timer, new object[] { EventArgs.Empty });
                shineStops = !timer.Enabled;
            }
            using (Seg sc = new Seg())
            {
                System.Windows.Forms.Timer timer = sc._anim;
                sc._from = 0;
                sc._since = Environment.TickCount - 1000;   // a fade that ended a second ago
                timer.Start(); onTick.Invoke(timer, new object[] { EventArgs.Empty });
                fadeStops = !timer.Enabled;
            }
            Check(shineRuns && shineStops && fadeStops, "window motion: the skeleton's light runs only until the first reading, the mode cards' fade until it is over; both timers stop by themselves, painted or not (minimized)");
            System.Reflection.AssemblyFileVersionAttribute fileVersion = (System.Reflection.AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(typeof(Setup).Assembly, typeof(System.Reflection.AssemblyFileVersionAttribute));
            System.Reflection.AssemblyTitleAttribute title = (System.Reflection.AssemblyTitleAttribute)Attribute.GetCustomAttribute(typeof(Setup).Assembly, typeof(System.Reflection.AssemblyTitleAttribute));
            System.Reflection.AssemblyCopyrightAttribute copyright = (System.Reflection.AssemblyCopyrightAttribute)Attribute.GetCustomAttribute(typeof(Setup).Assembly, typeof(System.Reflection.AssemblyCopyrightAttribute));
            Check(fileVersion != null && fileVersion.Version == Setup.Version && title != null && title.Title == "Salva OPM"
                && copyright != null && copyright.Copyright == "Copyright (C) 2026 127salva",
                "the exe's Properties show its name, version (Setup.Version) and copyright");
            // the setup window: the name's capitals on the logo's centre line and nothing after it (no version); every result in the list's place
            Func<System.Drawing.Bitmap, int, int, double> inkMid = delegate(System.Drawing.Bitmap b, int left, int right)
            {
                int top = -1, bottom = -1;
                for (int y = 0; y < Th.D(80); y++)
                    for (int x = left; x < right; x++)
                        if (b.GetPixel(x, y).G > 60) { if (top < 0) top = y; bottom = y; break; }
                return top < 0 ? -100 : (top + bottom + 1) / 2.0;
            };
            // the blank columns between the logo and the name, on the header's centre line
            Func<System.Drawing.Bitmap, int, int> logoGap = delegate(System.Drawing.Bitmap b, int mid)
            {
                Func<int, bool> inked = delegate(int x)
                {
                    for (int y = mid - Th.D(3); y <= mid + Th.D(3); y++) { System.Drawing.Color p = b.GetPixel(x, y); if (Math.Max(p.R, Math.Max(p.G, p.B)) > 60) return true; }
                    return false;
                };
                int x0 = 0;
                while (x0 < b.Width && !inked(x0)) x0++;   // the logo
                while (x0 < b.Width && inked(x0)) x0++;
                int x1 = x0;
                while (x1 < b.Width && !inked(x1)) x1++;   // the name
                return x1 - x0;
            };
            int windowGap;
            MainForm header = (MainForm)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MainForm));   // its OnPaint draws only the header
            GC.SuppressFinalize(header);
            using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(Th.D(MainForm.W), Th.D(70)))
            {
                using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(bmp))
                    typeof(MainForm).GetMethod("OnPaint", NonPublic).Invoke(header, new object[] { new System.Windows.Forms.PaintEventArgs(gr, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bmp.Size)) });
                windowGap = logoGap(bmp, Th.D(MainForm.HeaderMid));
            }
            double titleMid, afterName;
            int setupGap, nameX = Th.HeaderNameX(SetupForm.Edge);
            using (SetupForm sf = new SetupForm())
            using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(sf.ClientSize.Width, sf.ClientSize.Height))
            {
                using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(bmp))
                    typeof(SetupForm).GetMethod("OnPaint", NonPublic).Invoke(sf, new object[] { new System.Windows.Forms.PaintEventArgs(gr, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bmp.Size)) });
                titleMid = inkMid(bmp, nameX, nameX + Th.Measure("S", Th.Title).Width);                         // a capital: "S"
                afterName = inkMid(bmp, nameX + Th.Measure("Salva OPM", Th.Title).Width + Th.D(4), bmp.Width);    // -100: no ink at all
                setupGap = logoGap(bmp, Th.D(SetupForm.HeaderMid));
            }
            Check(Math.Abs(titleMid - Th.D(SetupForm.HeaderMid)) <= 1 && afterName == -100,
                "setup window: the name sits on the logo's centre line, with no version after it (" + titleMid + " for " + Th.D(SetupForm.HeaderMid) + ")");
            Check(setupGap == windowGap && setupGap > 0, "one header: the logo and the name are as far apart in the setup as in the window (" + setupGap + " and " + windowGap + " px)");
            string report = "OK       64-bit Windows\r\nOK       WMI service (Winmgmt) running\r\nOK       Task Scheduler service (Schedule) running\r\n"
                + "MISSING  OMEN 16-ap0 with board 8E35 (found: OMEN by HP Gaming Laptop 16-xx0xxx, 8C78)\r\nOK       Windows ACPI-WMI mapper device (ACPI\\PNP0C14)\r\n"
                + "MISSING  HP BIOS WMI interface (root\\wmi hpqBIntM)\r\n";
            string notInstalled = Setup.NotInstalledMessage(report);
            Check(notInstalled == "Not installed. Missing:\r\nOMEN 16-ap0 with board 8E35 (found: OMEN by HP Gaming Laptop 16-xx0xxx, 8C78)\r\nHP BIOS WMI interface (root\\wmi hpqBIntM)",
                "setup: 'Not installed' lists only what is missing");
            List<string> results = new List<string> { Setup.RemovedMessage, notInstalled, "Installing… (approve the admin prompt)" };
            foreach (int driver in new int[] { 0, 3010, Setup.NoDownload, 3, 1603 })
                foreach (bool ogh in new bool[] { true, false }) results.Add(Setup.ResultMessage(driver, ogh));
            System.Drawing.Rectangle box = SetupForm.StatusBox;
            bool fit = box.Bottom <= Th.D(SetupForm.ButtonsTop);
            using (System.Drawing.Bitmap one = new System.Drawing.Bitmap(1, 1))
            using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(one))   // a 96-DPI surface, as the theme measures
                foreach (string r in results)
                {
                    int tall = System.Windows.Forms.TextRenderer.MeasureText(gr, Th.Sp(r, Th.Small), Th.Small, new System.Drawing.Size(box.Width, int.MaxValue),
                        System.Windows.Forms.TextFormatFlags.WordBreak | System.Windows.Forms.TextFormatFlags.NoPrefix).Height;
                    if (tall > box.Height) { fit = false; Console.WriteLine("  setup result too tall (" + tall + " > " + box.Height + "): " + r); }
                }
            Check(fit, "setup window: every progress and result message fits the list's place, above the buttons");
            // the setup's buttons in the app's palette: the main one as the chosen mode card (its fill, a lavender ring), the other
            // as a plain card, rounded; while working both dimmed, without the ring
            bool cardButtons = true;
            foreach (int state in new int[] { 0, 1, 2 })   // main, other, main while working
            {
                CardButton made = new CardButton();
                made.Primary = state != 1; made.Enabled = state != 2; made.Text = "Update";
                using (CardButton cb = made)
                using (System.Drawing.Bitmap bmp = Painted(cb, Th.D(216), Th.D(44)))
                {
                    System.Drawing.Color top = bmp.GetPixel(bmp.Width / 2, 0), inside = bmp.GetPixel(Th.D(10), bmp.Height / 2), corner = bmp.GetPixel(0, 0);
                    System.Drawing.Color wantTop = state == 0 ? Th.Accent : Th.Border, wantInside = state == 0 ? Th.Selected : Th.Surface;
                    if (top.ToArgb() != wantTop.ToArgb() || inside.ToArgb() != wantInside.ToArgb() || corner.ToArgb() != Th.Bg.ToArgb())
                    { cardButtons = false; Console.WriteLine("  setup button " + state + ": edge " + top + ", fill " + inside + ", corner " + corner); }
                }
            }
            Check(cardButtons, "setup window: the buttons are drawn like the mode cards (the main one as the chosen card with its lavender ring, the other plain, both rounded; dimmed while working)");
            // the tray menu as Windows lays it out: the highlight of the first and the last item keeps one gap to every edge
            bool evenGap;
            using (System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip())
            {
                menu.Renderer = new TrayMenuRenderer();
                menu.ShowImageMargin = false;
                menu.ShowCheckMargin = false;
                menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Open Salva OPM"));
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Exit"));
                TrayMenuRenderer.Fit(menu);
                menu.PerformLayout();
                System.Drawing.Rectangle first = menu.Items[0].Bounds, last = menu.Items[2].Bounds;
                System.Drawing.RectangleF hf = TrayMenuRenderer.Highlight(first, menu.Size), hl = TrayMenuRenderer.Highlight(last, menu.Size);
                float gap = Th.D(TrayMenuRenderer.Inset);
                evenGap = hf.X + first.X == gap && menu.Width - (hf.Right + first.X) == gap && hf.Y + first.Y == gap
                    && menu.Height - (hl.Bottom + last.Y) == gap && hl.X + last.X == gap
                    && hf.X >= 0 && hf.Right <= first.Width && hl.Right <= last.Width;   // inside its item: an item paints only there
                if (!evenGap) Console.WriteLine("  tray highlight: menu " + menu.Size + ", first " + hf + " at " + first.Location + ", last " + hl + " at " + last.Location);
            }
            Check(evenGap && TrayMenuRenderer.OuterRadius - TrayMenuRenderer.Inset == 4,
                "tray menu: the highlight keeps one gap to every edge of the menu, its corners the menu's radius minus that gap");
            // the history lines' ends are round (anti-aliased), and two colours meet without a seam
            bool roundEnds, noSeam;
            double[] greenAmber = new double[ChipCard.SlotCount];
            for (int i = 0; i < greenAmber.Length; i++) greenAmber[i] = i < 10 ? 70 : 90;   // green, then amber
            using (ChipCard lc = CpuCard(greenAmber))
            using (System.Drawing.Bitmap bmp = Painted(lc, MainForm.CardW, ChipCard.CardHeight))
            {
                int mid = lc.Row1Mid, half = (int)Math.Ceiling(ChipCard.LineHeight / 2);
                int left = (int)lc.LineLeft, right = (int)Math.Ceiling(lc.LineRight), yTop = mid - half - 1, yBottom = mid + half + 1;
                // a round end's tip column shades off: inked over more than one row (a stair-stepped end has a single notch there)
                // and darker two rows from the middle than at the middle (a square end is one flat shade from top to bottom)
                Func<int, int, int> ink = delegate(int x, int y)
                {
                    System.Drawing.Color p = bmp.GetPixel(x, y);
                    return Math.Abs(p.R - Th.Surface.R) + Math.Abs(p.G - Th.Surface.G) + Math.Abs(p.B - Th.Surface.B);
                };
                Func<int, bool> inked = delegate(int x)
                {
                    for (int y = yTop; y <= yBottom; y++) if (ink(x, y) > 0) return true;
                    return false;
                };
                Func<int, string> profile = delegate(int x)
                {
                    string shades = "";
                    for (int y = yTop; y <= yBottom; y++) shades += ink(x, y) + " ";
                    return shades.Trim();
                };
                Func<int, bool> tapers = delegate(int x)
                {
                    int rows = 0;
                    for (int y = yTop; y <= yBottom; y++) if (ink(x, y) > 0) rows++;
                    int centre = Math.Max(ink(x, mid), Math.Max(ink(x, mid - 1), ink(x, mid + 1)));
                    return rows >= 2 && Math.Max(ink(x, mid - 2), ink(x, mid + 2)) < centre;
                };
                int tipL = left - 2, tipR = right + 1;
                while (!inked(tipL)) tipL++;
                while (!inked(tipR)) tipR--;
                roundEnds = tapers(tipL) && tapers(tipR);
                if (!roundEnds) Console.WriteLine("  line ends: tip columns " + tipL + " [" + profile(tipL) + "], " + tipR + " [" + profile(tipR) + "]");
                noSeam = true;
                for (int x = left + half; x < right - half; x++)
                {
                    System.Drawing.Color p = bmp.GetPixel(x, mid);
                    if (Math.Abs(p.R - Th.Surface.R) + Math.Abs(p.G - Th.Surface.G) + Math.Abs(p.B - Th.Surface.B) < 60) { noSeam = false; Console.WriteLine("  line seam at x " + x + ": " + p); }
                }
            }
            Check(roundEnds && noSeam, "chip cards: the history lines' ends are round (anti-aliased), and two colours meet without a seam");
            // the mode cards: a click that hides the ring also forgets the item the arrows were on
            using (Seg seg = new Seg())
            {
                seg.Items = new string[] { "Eco", "Balanced", "Performance" };
                seg.Icons = new string[] { "leaf", "scale", "bolt" };
                seg.Index = 1;
                int picked = -1;
                seg.Picked += delegate(int i) { picked = i; };
                typeof(Seg).GetMethod("OnKeyDown", NonPublic).Invoke(seg, new object[] { new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Right) });
                typeof(Seg).GetMethod("OnChangeUICues", NonPublic).Invoke(seg, new object[] { new System.Windows.Forms.UICuesEventArgs(System.Windows.Forms.UICues.ChangeFocus) });
                typeof(Seg).GetMethod("OnKeyDown", NonPublic).Invoke(seg, new object[] { new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Enter) });
                Check(picked == -1, "mode cards: after a click hides the ring, Enter does not apply the item the arrows were on");
            }
            // the hardware thread: jobs with the same key collapse into the last one, in order; an exception reaches the callback
            List<string> ran = new List<string>();
            Exception seen = null;
            bool waited;
            using (HardwareThread hw = new HardwareThread(new object(), delegate(Exception e) { seen = e; }))
            {
                ManualResetEvent hold = new ManualResetEvent(false);
                hw.Post("gate", delegate { hold.WaitOne(5000); });
                hw.Post("mode", delegate { ran.Add("mode1"); });
                hw.Post("mode", delegate { ran.Add("mode2"); });
                hw.Post("fans", delegate { ran.Add("fans"); });
                hw.Post("mode", delegate { ran.Add("mode3"); });
                hold.Set();
                hw.Post("boom", delegate { throw new InvalidOperationException("test"); });
                waited = hw.RunAndWait(delegate { ran.Add("last"); }, 5000);
            }
            Check(waited && string.Join(",", ran.ToArray()) == "mode3,fans,last" && seen != null,
                "hardware thread: fast clicks collapse into the last choice, in order; a failing job reaches the fail safe and the thread goes on");
            Exception late = null;
            bool timedOut, after;
            using (HardwareThread hw = new HardwareThread(new object(), delegate(Exception e) { late = e; }))
            {
                timedOut = !hw.RunAndWait(delegate { Thread.Sleep(300); }, 50);   // an exit job slower than its wait (a WMI hang)
                after = hw.RunAndWait(delegate { }, 5000);                         // ... which has ended by now
            }
            Check(timedOut && after && late == null, "hardware thread: a job that outlives its wait (a slow exit) ends without an error, so no fail safe");
        }
    }
}
