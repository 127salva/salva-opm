# How the firmware facts were found

Salva OPM talks to the laptop's firmware through one channel and sends only the calls on its allow-list
(`FirmwareRules` in `src/Firmware.cs`). This page says where each call and value comes from. Three kinds of source:
- **Decoded:** read statically from OMEN Gaming Hub's .NET assemblies and its platform data for this board (nothing was run).
- **Second source:** [Ohman](https://github.com/P4R1H/Ohman) (an open-source OMEN tool, GPL-3.0) and the Linux kernel's
  `hp-wmi` driver, which lists board 8E35.
- **Measured:** tried on the laptop (HP OMEN 16-ap0, board 8E35, BIOS F.13): first single test writes with read-backs,
  then the app itself, through its log.

## The channel
- WMI class `hpqBIntM` in `root\wmi`, served by Windows' own ACPI-WMI driver. No HP driver is needed (measured with HP's
  drivers removed). Calls need administrator rights.
- Packet: `"SECU"` + command + type + size + data. Command `0x20008` for everything except the charger read (command 1).
- The firmware answers `0` even to writes it ignores (measured), so every write that matters is read back.

## The calls
| Type | What | Source | Facts that shaped the app |
|---|---|---|---|
| 26 | thermal profile `[FF, profile, fans by firmware, 00]` | decoded, Ohman, Linux | `0x30` = Eco and Balanced, `0x04` = Performance (OGH's Unleashed). Byte 2 = 1 hands the fans to the firmware's own curve at once (measured: their levels changed within 5 s). There is no read for the profile. |
| 41 / 42 | power limits write / power data read | decoded, measured | PL1/PL2 and concurrent TDP (`0xFF` = leave unchanged); type 42 reads them back. Concurrent TDP is taken only under profile `0x04`, and 82 W is accepted there. |
| 33 / 34 | GPU record read / write `[cTGP, PPAB, dState, GPS]` | decoded, Ohman, measured | The Base, Boost and Max presets are Ohman's. The record does not lapse, so the app puts it back itself. About a second after Base → Max the firmware sets concurrent TDP to 45, so Performance checks its power again. |
| 35 | sensor read (index 0 = chassis) | decoded | input of the 54 °C chassis rule |
| 45 | fan speeds (× 100 rpm) | Ohman, measured | a read; it keeps no host state alive |
| 53 | thermal-throttling flag | decoded | gate for raising the power limit again |
| 1 / 15 | charger status | decoded, measured | meets / below the laptop's requirement, and the USB-C ratings |

Fan levels (type 46), their trigger (type 16) and max fan (type 39) are known but not on the allow-list.

## How the firmware behaves over time (measured)
- **Host state lapses.** Host-set power limits and the profile drop about two minutes after the last refresh (in a test
  still set at 106 s, gone at 117 s; the app saw 125–133 s). After a crash the laptop returns to its own settings by itself.
- **Writes keep it alive.** Any profile or power write refreshes it. A write during the lapse after leaving Performance kept
  the 55 W limit alive (still set 3 minutes later), so the app sends nothing until the limit reads 0/0.
- **What does not lapse:** the GPU record and concurrent TDP stay until changed, so exit, start-up and uninstall put them back.
- **Performance holds on the heartbeat alone:** re-sending the profile and power every 45 s held 80 W for 57 minutes of
  gaming, with the fans on the firmware's curve.
- **Modern Standby:** this laptop sleeps in S0 low-power idle and the app gets no resume event; a gap in its own 5 s timer
  marks the wake.

## Other readings
- **CPU temperature:** the AMD die sensor through the signed PawnIO driver and its `AMDFamily17` module (Ohman's method),
  median of three readings.
- **GPU temperature and load:** NVIDIA's own `nvml.dll`, not read while the GPU sleeps (reading would wake it).

## OMEN Gaming Hub's safety rules
The two Performance rules (battery drain 40 %, chassis 54 °C) and their constants come from OMEN Gaming Hub's own code and
platform data for this board; the app's version is `src/Governor.cs`. There is no CPU power reading, so the rule that
raises the limit again uses the firmware's throttling flag or a CPU at 90 °C or more, and the chassis state.
