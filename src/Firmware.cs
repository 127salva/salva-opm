// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.Management;

namespace SalvaOPM
{
    /// <summary>Result of one HP BIOS WMI call. Ok means the call went through AND the firmware return code was 0.
    /// Ok alone never proves a setting took effect: callers verify by reading back (docs/hardware/evidence.md, step 2).</summary>
    public sealed class FwReply
    {
        public bool Ok;
        public int Rc;
        public byte[] Data = new byte[0];
        public string Error;
    }

    /// <summary>Raw transport for HP BIOS packets ("SECU" + command + type + data). Every implementation must call FirmwareRules.Check first.</summary>
    public interface IFirmware
    {
        FwReply Execute(int command, int type, byte[] input, int outSize);
    }

    /// <summary>The allow-list: exactly the command/type pairs this app sends, with their exact input and output sizes
    /// (docs/hardware/evidence.md sections 0, 8 and 11).</summary>
    public static class FirmwareRules
    {
        public const int Omen = 0x20008;   // OMEN BIOS command
        public const int BiosRead = 1;     // legacy mailbox read: charger status only (type 15)

        public static bool IsAllowed(int command, int type, int inLen, int outSize)
        {
            if (command == BiosRead) return type == 15 && inLen == 4 && outSize == 4;
            if (command != Omen) return false;
            switch (type)
            {
                case 16: return inLen == 4 && outSize == 4;     // fan-count read = the firmware's user-defined-state keep-alive; sent only right before a fan-level write
                case 26: return inLen == 4 && outSize == 4;     // thermal profile write
                case 33: return inLen == 4 && outSize == 4;     // GPU record read
                case 34: return inLen == 4 && outSize == 4;     // GPU record write
                case 35: return inLen == 4 && outSize == 4;     // sensor read
                case 41: return inLen == 4 && outSize == 4;     // power record write
                case 42: return inLen == 0 && outSize == 128;   // power data read
                case 45: return inLen == 4 && outSize == 128;   // fan levels read
                case 46: return inLen == 128 && outSize == 4;   // fan levels write
                case 53: return inLen == 4 && outSize == 128;   // thermal-throttling read
                default: return false;
            }
        }

        /// <summary>Writes, plus type 16: a read that arms and refreshes the firmware's host fan state. Read-only mode refuses all of them.</summary>
        public static bool ChangesState(int command, int type)
        {
            return command == Omen && (type == 16 || type == 26 || type == 34 || type == 41 || type == 46);
        }

        public static void Check(int command, int type, byte[] input, int outSize)
        {
            if (input == null || !IsAllowed(command, type, input.Length, outSize))
                throw new InvalidOperationException(string.Format("firmware call not in the allow-list: command 0x{0:X} type {1}", command, type));
        }
    }

    public sealed class PowerData
    {
        public int Telemetry;   // byte 0: mirrors the ACPI THRM zone, not used for decisions
        public int Pl1;         // byte 3 (host-set limit, 0 = none)
        public int Pl2;         // byte 4
        public int Pl4;         // byte 6
        public int Tdp;         // byte 7 concurrent TDP
    }

    /// <summary>Typed, range-checked operations on top of IFirmware. Values outside the allowed ranges throw; nothing is clamped.</summary>
    public sealed class HpBios
    {
        public const byte ProfileBalanced = 0x30;   // Eco and Balanced (OGH and Ohman both map them to 0x30)
        public const byte ProfilePerformance = 0x04; // OGH's Unleashed byte on this board (verified, steps 5-8)
        public const int PlMin = 45, PlMax = 80;
        public const int TdpMin = 20, TdpMax = 82;  // 82 = SPG 27 W + TppMinValue 55, accepted under 0x04 (step 5)
        public const int GpsMin = 75, GpsMax = 87;
        public const int FanMin = 19, FanMax = 48;  // this board's own fan table (type 47 top = 48) and OGH's bounds; never 0..18

        // Ohman's GPU presets [cTGP, PPAB, dState, GPS] (Platform.cs)
        public static readonly byte[] GpuBase = { 0, 0, 1, 75 };
        public static readonly byte[] GpuBoost = { 0, 1, 1, 87 };
        public static readonly byte[] GpuMax = { 1, 1, 1, 87 };

        private readonly IFirmware _fw;
        public HpBios(IFirmware fw) { _fw = fw; }

        public bool SetProfile(byte profile, bool fansByBios)
        {
            if (profile != ProfileBalanced && profile != ProfilePerformance) throw new ArgumentOutOfRangeException("profile");
            return _fw.Execute(FirmwareRules.Omen, 26, new byte[] { 0xFF, profile, (byte)(fansByBios ? 1 : 0), 0x00 }, 4).Ok;
        }

        /// <summary>Fan levels, Ohman's way: the type-16 query first (it arms/refreshes the firmware's user-defined state),
        /// then the 128-byte level write. Both levels must be 19..48.</summary>
        public bool WriteFanLevels(int cpu, int gpu)
        {
            if (cpu < FanMin || cpu > FanMax || gpu < FanMin || gpu > FanMax) throw new ArgumentOutOfRangeException("level");
            if (!_fw.Execute(FirmwareRules.Omen, 16, new byte[4], 4).Ok) return false;   // as Ohman: no level write without the trigger
            byte[] d = new byte[128];
            d[0] = (byte)cpu; d[1] = (byte)gpu;
            return _fw.Execute(FirmwareRules.Omen, 46, d, 4).Ok;
        }

        public PowerData ReadPower()
        {
            FwReply r = _fw.Execute(FirmwareRules.Omen, 42, new byte[0], 128);
            if (!r.Ok || r.Data.Length < 8) return null;
            PowerData p = new PowerData();
            p.Telemetry = r.Data[0]; p.Pl1 = r.Data[3]; p.Pl2 = r.Data[4]; p.Pl4 = r.Data[6]; p.Tdp = r.Data[7];
            return p;
        }

        /// <summary>PL1 = PL2 = watts (AMD: OGH sends PL2 = PL1). PL4 and concurrent TDP unchanged (0xFF).</summary>
        public bool WritePowerLimits(int watts)
        {
            if (watts < PlMin || watts > PlMax) throw new ArgumentOutOfRangeException("watts");
            return _fw.Execute(FirmwareRules.Omen, 41, new byte[] { (byte)watts, (byte)watts, 0xFF, 0xFF }, 4).Ok;
        }

        /// <summary>Concurrent TDP only. Ignored by the firmware unless profile 0x04 is active.</summary>
        public bool WriteTdp(int watts)
        {
            if (watts < TdpMin || watts > TdpMax) throw new ArgumentOutOfRangeException("watts");
            return _fw.Execute(FirmwareRules.Omen, 41, new byte[] { 0xFF, 0xFF, 0xFF, (byte)watts }, 4).Ok;
        }

        /// <summary>GPU record [cTGP, PPAB, dState, GPS].</summary>
        public byte[] ReadGpu()
        {
            FwReply r = _fw.Execute(FirmwareRules.Omen, 33, new byte[4], 4);
            if (!r.Ok || r.Data.Length < 4) return null;
            return new byte[] { r.Data[0], r.Data[1], r.Data[2], r.Data[3] };
        }

        public bool WriteGpu(byte[] record)
        {
            if (record == null || record.Length != 4) throw new ArgumentException("record");
            if (record[0] > 1 || record[1] > 1 || record[2] > 1) throw new ArgumentOutOfRangeException("record", "cTGP, PPAB and dState must be 0 or 1");
            if (record[3] < GpsMin || record[3] > GpsMax) throw new ArgumentOutOfRangeException("record", "GPS outside 75..87");
            return _fw.Execute(FirmwareRules.Omen, 34, new byte[] { record[0], record[1], record[2], record[3] }, 4).Ok;
        }

        /// <summary>BIOS sensor, idx 0 = IR (chassis), 1 = ambient, 2 = PCH, 3 = VR. Returns -1 on failure.</summary>
        public int ReadSensor(int idx)
        {
            if (idx < 0 || idx > 3) throw new ArgumentOutOfRangeException("idx");
            FwReply r = _fw.Execute(FirmwareRules.Omen, 35, new byte[] { (byte)idx, 0, 0, 0 }, 4);
            return r.Ok && r.Data.Length >= 1 ? r.Data[0] : -1;
        }

        /// <summary>Current fan speeds in units of 100 rpm, {cpu, gpu}; null on failure.</summary>
        public int[] ReadFanLevels()
        {
            FwReply r = _fw.Execute(FirmwareRules.Omen, 45, new byte[4], 128);
            if (!r.Ok || r.Data.Length < 2) return null;
            return new int[] { r.Data[0], r.Data[1] };
        }

        /// <summary>1 = throttling, 0 = not, -1 = read failed.</summary>
        public int ReadThrottling()
        {
            FwReply r = _fw.Execute(FirmwareRules.Omen, 53, new byte[] { 0, 4, 0, 0 }, 128);
            if (!r.Ok || r.Data.Length < 3) return -1;
            return r.Data[2] != 0 ? 1 : 0;
        }

        /// <summary>Charger status (command 1, type 15): [status, flags, usbcDesign/5, rating/5]. Null on failure.</summary>
        public byte[] ReadCharger()
        {
            FwReply r = _fw.Execute(FirmwareRules.BiosRead, 15, new byte[4], 4);
            if (!r.Ok || r.Data.Length < 4) return null;
            return new byte[] { r.Data[0], r.Data[1], r.Data[2], r.Data[3] };
        }
    }

    /// <summary>The real transport: root\wmi hpqBIntM (Windows' own ACPI-WMI channel). Needs administrator rights.
    /// In read-only mode (--dry-run, --read) writes and type 16 are refused before anything is sent.</summary>
    public sealed class WmiFirmware : IFirmware
    {
        private readonly bool _readOnly;
        private readonly Action<string> _log;
        private ManagementObject _instance;
        private readonly object _sync = new object();

        public WmiFirmware(bool readOnly, Action<string> log)
        {
            _readOnly = readOnly;
            _log = log;
        }

        /// <summary>A WMI query that gives up after 10 s instead of hanging the UI thread that does all hardware work.</summary>
        public static EnumerationOptions Bounded()
        {
            EnumerationOptions o = new EnumerationOptions();
            o.Timeout = TimeSpan.FromSeconds(10);
            return o;
        }

        private ManagementObject Instance()
        {
            if (_instance != null) return _instance;
            ManagementScope scope = new ManagementScope(@"root\wmi");
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM hpqBIntM"), Bounded()))
            {
                foreach (ManagementObject o in s.Get())
                {
                    object active = o["Active"];
                    if (active is bool && (bool)active) { _instance = o; return o; }
                    o.Dispose();
                }
            }
            throw new InvalidOperationException("no active hpqBIntM instance");
        }

        public FwReply Execute(int command, int type, byte[] input, int outSize)
        {
            FirmwareRules.Check(command, type, input, outSize);
            FwReply r = new FwReply();
            if (_readOnly && FirmwareRules.ChangesState(command, type))
            {
                r.Error = "read-only mode: write not sent";
                if (_log != null) _log(string.Format("NOT SENT (read-only): command {0} type {1}", command, type));
                return r;
            }
            lock (_sync)
            {
                try
                {
                    ManagementObject inst = Instance();
                    string method = outSize <= 4 ? "hpqBIOSInt4" : "hpqBIOSInt128";
                    using (ManagementClass cls = new ManagementClass(new ManagementScope(@"root\wmi"), new ManagementPath("hpqBDataIn"), null))
                    using (ManagementObject data = cls.CreateInstance())
                    using (ManagementBaseObject inParams = inst.GetMethodParameters(method))
                    {
                        data["Sign"] = new byte[] { 0x53, 0x45, 0x43, 0x55 };   // "SECU"
                        data["Command"] = (uint)command;
                        data["CommandType"] = (uint)type;
                        data["Size"] = (uint)input.Length;
                        data["hpqBData"] = input;
                        inParams["InData"] = data;
                        using (ManagementBaseObject outParams = inst.InvokeMethod(method, inParams, new InvokeMethodOptions(null, TimeSpan.FromSeconds(5))))
                        {
                            // disposed at once: left to the garbage collector, each call holds a few handles until it runs
                            using (ManagementBaseObject outData = (ManagementBaseObject)outParams["OutData"])
                            {
                                r.Rc = Convert.ToInt32(outData["rwReturnCode"]);
                                byte[] d = outData["Data"] as byte[];
                                r.Data = d != null ? d : new byte[0];
                                r.Ok = r.Rc == 0;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    r.Ok = false;
                    r.Error = e.Message;
                    _instance = null;   // re-resolve next time (e.g. WMI not ready at early logon)
                }
            }
            return r;
        }
    }
}
