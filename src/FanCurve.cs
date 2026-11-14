// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva
// Portions adapted from Ohman (https://github.com/P4R1H/Ohman), GPL-3.0-or-later.

using System;

namespace SalvaOPM
{
    /// <summary>
    /// Ohman's software fan curve (Platform.cs FanCurve, Engine.cs CustomCurve/Smooth), re-implemented. No I/O.
    /// Levels are fan speed in units of 100 rpm; this board takes 19..48 (docs/hardware/evidence.md section 11).
    /// </summary>
    public sealed class FanCurve
    {
        public int[] CpuTemps, CpuLevels, GpuTemps, GpuLevels;
        public bool Linked = true;                 // linked: both fans follow the hotter chip
        public int Floor = HpBios.FanMin, Ceiling = HpBios.FanMax;
        public int StepPerTick = 3;                // OGH moves ~3 levels per 5 s update
        public int Fallback = 35;                  // used when no temperature is known at all

        /// <summary>The seven fixed temperatures of the custom curve. Below 60 the 60 value applies, above 90 the 90 value.</summary>
        public static readonly int[] CustomTemps = { 60, 65, 70, 75, 80, 85, 90 };
        /// <summary>The custom curve until the owner changes it: a moderate curve above CustomFloor.</summary>
        public static readonly int[] DefaultCustom = { 24, 24, 24, 29, 31, 34, 34 };
        /// <summary>OGH's own lower bound for a custom curve on this board (its CPU/GPU_Fan_Speed_Lower_Bound_List,
        /// docs/hardware/evidence.md section 3): from 75 C up a fan may not be set below 2900 rpm.</summary>
        public static readonly int[] CustomFloor = { 19, 19, 19, 29, 29, 29, 29 };

        /// <summary>The one Auto curve, used in every mode: the owner's quiet curve (2026-11-13; Ohman's own table was too loud
        /// for the owner). 1900 rpm up to 60 C, rising to 4800 at 90 C; both fans follow the hotter chip. HeatPush backs it up.</summary>
        public static FanCurve Auto()
        {
            FanCurve c = new FanCurve();
            c.CpuTemps = CustomTemps;
            c.CpuLevels = new int[] { 19, 21, 24, 29, 34, 42, 48 };
            c.GpuTemps = CustomTemps;
            c.GpuLevels = c.CpuLevels;
            return c;
        }

        /// <summary>The one custom curve: the same seven levels for both fans, each fan following its own chip.</summary>
        public static FanCurve Custom(int[] levels)
        {
            FanCurve c = new FanCurve();
            c.CpuTemps = CustomTemps;
            c.CpuLevels = levels;
            c.GpuTemps = CustomTemps;
            c.GpuLevels = levels;
            c.Linked = false;
            return c;
        }

        private static int Interp(int[] xs, int[] ys, double x)
        {
            if (xs.Length == 0) return 0;
            if (x <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i]) { double t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]); return (int)Math.Round(ys[i - 1] + t * (ys[i] - ys[i - 1])); }
            return ys[ys.Length - 1];
        }

        /// <summary>Target {cpu fan, gpu fan} for the temperatures (NaN = unknown). Without any chip temperature: the fallback.
        /// Unlinked, each fan follows its own chip, but only when both readings exist; with one missing (e.g. the GPU asleep)
        /// both fans follow the reading that exists (Ohman: the safe answer).</summary>
        public int[] Target(double cpu, double gpu)
        {
            bool silicon = !double.IsNaN(cpu) || !double.IsNaN(gpu);
            int c = double.IsNaN(cpu) ? 0 : Interp(CpuTemps, CpuLevels, cpu);
            int g = double.IsNaN(gpu) ? 0 : Interp(GpuTemps, GpuLevels, gpu);
            if (!Linked && !double.IsNaN(cpu) && !double.IsNaN(gpu)) return new int[] { Clamp(c), Clamp(g) };
            int lvl = silicon ? Math.Max(c, g) : Fallback;
            return new int[] { Clamp(lvl), Clamp(lvl) };
        }

        /// <summary>One step from the current level towards the target (OGH's smoothing). From unknown (-1): reach the floor quickly.</summary>
        public int Step(int current, int target)
        {
            if (current < Floor) return Math.Max(Floor, Math.Min(target, Floor + StepPerTick * 2));
            int d = target - current;
            if (Math.Abs(d) <= StepPerTick) return target;
            return current + Math.Sign(d) * StepPerTick;
        }

        public int Clamp(int level) { return Math.Max(Floor, Math.Min(Ceiling, level)); }

        public const double Smoothing = 0.4;   // per 5 s tick: ~4 ticks to travel 85 % of a drop
        public const int Deadband = 2;         // 200 rpm

        /// <summary>Ohman's temperature smoothing: rising readings are followed at once, falling ones slowly; three missing
        /// readings in a row make the value unknown again.</summary>
        public static double Smooth(double now, double was, ref int gone)
        {
            if (double.IsNaN(now)) { gone++; return gone >= 3 ? double.NaN : was; }
            gone = 0;
            if (double.IsNaN(was)) return now;
            if (now > was) return now;
            return was + (now - was) * Smoothing;
        }
    }

    /// <summary>Keeps the CPU from sitting hot under a quiet curve: at Hot or more for HoldMs the fans go to maximum until the
    /// CPU is under Cool. No I/O. An unknown temperature neither starts nor ends it.</summary>
    public sealed class HeatPush
    {
        public int Hot = 85, Cool = 80;
        public long HoldMs = 60000;
        public bool Active;
        private long _since = -1, _last = long.MinValue / 2;

        public bool Tick(double cpu, long nowMs)
        {
            if (nowMs - _last > 15000) _since = -1;   // not asked for a while (firmware had the fans): the minute starts over
            _last = nowMs;
            if (double.IsNaN(cpu)) return Active;
            if (Active) { if (cpu < Cool) { Active = false; _since = -1; } }
            else if (cpu < Hot) _since = -1;
            else if (_since < 0) _since = nowMs;
            else if (nowMs - _since >= HoldMs) Active = true;
            return Active;
        }
    }

    /// <summary>
    /// Ohman's thermal guard (Engine.cs GuardTick, Platform.cs GuardLimits), re-implemented. No I/O; called every 10 s.
    /// Engages after two consecutive hot (CPU >= 95 C or chassis >= 62 C) or stalled (CPU >= 75 C with both fans under
    /// ~500 rpm) readings; releases after 60 s of safe readings (CPU < 85 C, chassis < 54 C); one warm sample does not
    /// restart the minute, two in a row do.
    /// </summary>
    public sealed class ThermalGuard
    {
        public int CpuHot = 95, ChassisHot = 62, CpuSafe = 85, ChassisSafe = 54;
        public long SafeMs = 60000;
        public int StallCpu = 75, StallLevelSum = 10;

        public bool Active;
        public bool Stalled;
        public int IgnoredTicks;        // engaged, but the fans still read stalled: the command is not reaching them
        private int _hotTicks, _warmTicks;
        private long _safeSince = -1;

        /// <summary>Returns true when Active changed.</summary>
        public bool Tick(double cpu, int chassis, int[] fans, long nowMs)
        {
            bool cpuKnown = !double.IsNaN(cpu);
            bool chassisKnown = chassis >= 0;
            bool fansKnown = fans != null && fans.Length >= 2 && fans[0] >= 0 && fans[1] >= 0;
            bool hot = (cpuKnown && cpu >= CpuHot) || (chassisKnown && chassis >= ChassisHot);
            bool stalled = cpuKnown && cpu >= StallCpu && fansKnown && fans[0] + fans[1] < StallLevelSum;
            if (hot || stalled) _hotTicks++; else _hotTicks = 0;
            if (!Active)
            {
                if ((hot || stalled) && _hotTicks >= 2) { Active = true; Stalled = stalled; _safeSince = -1; _warmTicks = 0; IgnoredTicks = 0; return true; }
                return false;
            }
            if (fansKnown && fans[0] + fans[1] < StallLevelSum) IgnoredTicks++; else IgnoredTicks = 0;
            bool safe = (!cpuKnown || cpu < CpuSafe) && (!chassisKnown || chassis < ChassisSafe);
            if (safe) _warmTicks = 0; else _warmTicks++;
            if (_warmTicks >= 2) { _safeSince = -1; return false; }
            if (!safe) return false;
            if (_safeSince < 0) { _safeSince = nowMs; return false; }
            if (nowMs - _safeSince < SafeMs) return false;
            Active = false; Stalled = false; IgnoredTicks = 0; _hotTicks = 0; _safeSince = -1;
            return true;
        }
    }
}
