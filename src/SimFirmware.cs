// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.Collections.Generic;

namespace SalvaOPM
{
    /// <summary>
    /// Test and --demo simulator of the laptop's firmware, encoding what the hardware tests showed (docs/hardware/evidence.md)
    /// plus the second-source behaviour from Ohman / Linux hp-wmi (section 11, marked there as not yet verified here):
    ///  - concurrent TDP writes apply only while profile 0x04 is active (steps 2, 2b, 5); the value itself never expires;
    ///  - host state (profile, PL1/PL2, held fan levels) lapses ~110 s after the last refresh; type 16, profile and PL
    ///    writes refresh it (steps 6-8 for 16; writes = assumption);
    ///  - with "fans by BIOS" set in the profile write the firmware drives the fans and ignores levels;
    ///  - the GPU record persists until changed (GPU step 3).
    /// It enforces the same allow-list as the real transport and has a virtual clock driven by the caller.
    /// </summary>
    public sealed class SimFirmware : IFirmware
    {
        public const long ExpiryMs = 110000;

        public long NowMs;
        public byte Profile = HpBios.ProfileBalanced;
        public bool FansByBios = true;
        public byte HostPl1, HostPl2;            // 0 = no host limit
        public byte Pl4;
        public byte Tdp = 30;
        public byte Telemetry = 52;
        public byte[] Gpu = { 0, 1, 0, 87 };     // as read on this laptop
        public int OwnFanCpu = 21, OwnFanGpu = 19; // the firmware's own idle levels (as read on this laptop)
        public int HeldCpu = -1, HeldGpu = -1;   // levels written by the host, -1 = none
        public int IrTemp = 35, AmbientTemp = 35;
        public bool Throttling;
        public byte[] Charger = { 1, 0xAE, 0, 9 };  // MeetsRequirement, as read on this laptop
        public long KeepAliveMs = -1;             // last refresh of host state, -1 = none held
        public bool EchoDState = true;            // false: the firmware keeps dState 0 whatever is written (unverified here)
        public int TdpAfterGpuWrite = -1;         // once: the next GPU write sets concurrent TDP to this (seen on the laptop: 45)
        public long TdpResetDelayMs;              // ... that long after the write (on the laptop about 1 s)
        private long _tdpResetAt = -1; private byte _tdpResetTo;

        // fault injection
        public bool FailAll;                      // transport failure (e.g. WMI not ready)
        public int IgnoreType = -1;               // rc 0 but the write has no effect
        public int ErrorType = -1;                // rc 1

        public readonly List<string> Log = new List<string>();

        public void Advance(long ms) { NowMs += ms; Expire(); }

        private void Expire()
        {
            if (_tdpResetAt >= 0 && NowMs >= _tdpResetAt) { Tdp = _tdpResetTo; _tdpResetAt = -1; Log.Add(NowMs + " firmware changed TDP to " + _tdpResetTo); }
            if (KeepAliveMs >= 0 && NowMs - KeepAliveMs > ExpiryMs)
            {
                HostPl1 = 0; HostPl2 = 0; Profile = HpBios.ProfileBalanced; HeldCpu = HeldGpu = -1; KeepAliveMs = -1;
                Log.Add(NowMs + " EXPIRED: host limits, profile and fan levels dropped");
            }
        }

        public int[] FansNow()
        {
            bool held = !FansByBios && HeldCpu > 0;
            return new int[] { held ? HeldCpu : OwnFanCpu, held ? HeldGpu : OwnFanGpu };
        }

        public FwReply Execute(int command, int type, byte[] input, int outSize)
        {
            FirmwareRules.Check(command, type, input, outSize);
            Expire();
            FwReply r = new FwReply();
            Log.Add(string.Format("{0} cmd 0x{1:X} type {2} in {3} (profile 0x{4:X2})", NowMs, command, type,
                BitConverter.ToString(input, 0, Math.Min(input.Length, 4)), Profile));
            if (FailAll) { r.Error = "simulated transport failure"; return r; }
            r.Data = new byte[outSize];
            if (type == ErrorType) { r.Rc = 1; return r; }
            r.Ok = true;
            if (type == IgnoreType) return r;

            if (command == FirmwareRules.BiosRead) { Array.Copy(Charger, r.Data, 4); return r; }

            switch (type)
            {
                case 16:
                    r.Data[0] = 2;
                    KeepAliveMs = NowMs;
                    break;
                case 26:
                    Profile = input[1];
                    FansByBios = input[2] == 1;
                    KeepAliveMs = NowMs;
                    break;
                case 33:
                    Array.Copy(Gpu, r.Data, 4);
                    break;
                case 34:
                    Gpu = new byte[] { input[0], input[1], EchoDState ? input[2] : (byte)0, input[3] };
                    if (TdpAfterGpuWrite >= 0)
                    {
                        if (TdpResetDelayMs <= 0) Tdp = (byte)TdpAfterGpuWrite;
                        else { _tdpResetAt = NowMs + TdpResetDelayMs; _tdpResetTo = (byte)TdpAfterGpuWrite; }
                        TdpAfterGpuWrite = -1;
                    }
                    break;
                case 35:
                    r.Data[0] = (byte)(input[0] == 1 ? AmbientTemp : IrTemp);
                    break;
                case 41:
                    if (input[0] != 0xFF) HostPl2 = input[0];
                    if (input[1] != 0xFF) HostPl1 = input[1];
                    if (input[2] != 0xFF) Pl4 = input[2];
                    if (input[3] != 0xFF && Profile == HpBios.ProfilePerformance) Tdp = input[3];
                    if (input[0] != 0xFF || input[1] != 0xFF) KeepAliveMs = NowMs;
                    break;
                case 42:
                    r.Data[0] = Telemetry; r.Data[3] = HostPl1; r.Data[4] = HostPl2; r.Data[6] = Pl4; r.Data[7] = Tdp;
                    break;
                case 45:
                    int[] f = FansNow();
                    r.Data[0] = (byte)f[0]; r.Data[1] = (byte)f[1];
                    break;
                case 46:
                    if (!FansByBios) { HeldCpu = input[0]; HeldGpu = input[1]; }
                    break;
                case 53:
                    r.Data[1] = 4; r.Data[2] = (byte)(Throttling ? 1 : 0);
                    break;
            }
            return r;
        }
    }
}
