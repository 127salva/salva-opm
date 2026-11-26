// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;

namespace SalvaOPM
{
    /// <summary>
    /// Constants from OGH for board 8E35 (docs/firmware.md): platform JSON Khalilah_DRG_N22X4X6 and PlatformSettings defaults,
    /// plus the user's Unleashed settings (Max Battery Drain 40 %, Chassis 54 C, SPPT 80 W).
    /// </summary>
    public sealed class GovernorConfig
    {
        public int Pl1Max = 80;                 // UnleashedModePowerLimit1 (SPPT)
        public int Pl1Floor = 45;               // max(BatteryLowerBound 45, NbPL1LowerBound 45)
        public double BatteryThreshold = 0.60;  // (100 - 40) / 100
        public double BatteryBuffer = 0.03;     // BatteryDrainBuffer
        public int GamingGpuUsage = 30;         // battery rule only acts while the GPU is busy
        public int IrOverheat = 54;             // chassis limit: overheat = GPS = PL1 threshold in Unleashed
        public int IrRelease = 49;              // 54 - UnleashedModeIrReleaseThresholdOffset (5)
        public int IrOverheatReduce = 5;        // IrOverheatPl1Reduce
        public int GpsMin = 75;                 // GpsMinTemperature
        public int DecreaseStep = 1;            // OGH lowers PL1 by 1 W per cycle on battery drain
        public int IncreaseStep = 1;            // conservative: restore slowly
        public int IrCycleTicks = 6;            // chassis rule runs every 6th tick (30 s at a 5 s tick), like OGH's IrCycle 30
    }

    public struct GovernorInput
    {
        public bool OnAc;
        public bool BatteryDrain;       // OGH's IsBatteryDrain (percentage fell since the last power event while on AC)
        public double BatteryPercent;   // 0..1
        public int GpuUsage;            // percent
        public int IrTemp;              // BIOS chassis sensor, C
        public bool Throttling;         // BIOS thermal-throttling flag (type 53)
    }

    public sealed class GovernorState
    {
        public int Pl1;
        public int Gps;
        public bool PpabOn;
        public bool IrCut;              // chassis rule currently holding the GPU back
        public int Tick;
        public int GpsBaseline;         // GPU target before the first reduction (the Max preset's), restored on release
        public bool PpabBaseline;
    }

    public struct GovernorOutput
    {
        public int Pl1;
        public int Gps;
        public bool PpabOn;
    }

    /// <summary>
    /// Behaviour-equivalent port of OGH's two safety rules. No I/O. Differences from OGH (no CPU power reading): the increase
    /// gate uses Throttling (the BIOS flag, or a CPU at 90 °C or more: Engine.GovernorInputs) and the chassis state, and the GPS "tune band"
    /// is empty because OGH sets irGpsThreshold = irPl1Threshold = overheat (54) in Unleashed.
    /// </summary>
    public static class Governor
    {
        public static GovernorState NewState(GovernorConfig c, int gpsBaseline, bool ppabBaseline)
        {
            GovernorState s = new GovernorState();
            s.Pl1 = c.Pl1Max;
            s.Gps = gpsBaseline;
            s.PpabOn = ppabBaseline;
            s.GpsBaseline = gpsBaseline;
            s.PpabBaseline = ppabBaseline;
            return s;
        }

        public static GovernorOutput Step(GovernorConfig c, GovernorState s, GovernorInput i)
        {
            s.Tick++;

            // Chassis (IR) rule, every IrCycleTicks ticks, starting with the first tick.
            if ((s.Tick - 1) % c.IrCycleTicks == 0)
            {
                if (i.IrTemp >= c.IrOverheat)
                {
                    s.IrCut = true;
                    s.Gps = c.GpsMin;
                    s.PpabOn = false;                                   // OGH: PpabOffWhenIrOverheat
                    s.Pl1 = Math.Max(c.Pl1Floor, s.Pl1 - c.IrOverheatReduce);
                }
                else if (i.IrTemp <= c.IrRelease && s.IrCut)
                {
                    s.IrCut = false;
                    s.Gps = s.GpsBaseline;                              // restore the GPU target from before the reduction
                    s.PpabOn = s.PpabBaseline;
                }
                // between release and overheat: hold
            }

            // Battery rule (OGH ProcessBatteryDrain_DecreasePL1): only while on AC, draining and the GPU is busy.
            bool increaseAllowed = true;
            if (i.OnAc && i.BatteryDrain && i.GpuUsage > c.GamingGpuUsage)
            {
                if (i.BatteryPercent < c.BatteryThreshold)
                {
                    s.Pl1 = Math.Max(c.Pl1Floor, s.Pl1 - c.DecreaseStep);
                    increaseAllowed = false;
                }
                else if (i.BatteryPercent < c.BatteryThreshold + c.BatteryBuffer)
                {
                    increaseAllowed = false;                            // hold inside the 3 % band
                }
            }

            if (increaseAllowed && !s.IrCut && !i.Throttling) s.Pl1 = Math.Min(c.Pl1Max, s.Pl1 + c.IncreaseStep);

            GovernorOutput o = new GovernorOutput();
            o.Pl1 = s.Pl1;
            o.Gps = s.Gps;
            o.PpabOn = s.PpabOn;
            return o;
        }
    }
}
