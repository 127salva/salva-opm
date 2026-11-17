# Salva OPM 2.4.7: what it does (and doesn't)

Everything Salva OPM touches on the HP OMEN 16-ap0 with board 8E35. Source: `src/` (plain C#, built by `build.cmd` with Windows' own compiler). The one file to run is `dist\SalvaOPM-Setup.exe`. The app works offline; the setup needs internet once, only if PawnIO is missing or older than 2.2.0.

Its logic follows Ohman (github.com/P4R1H/Ohman, GPL-3.0), except the Auto fan curve, which is the owner's quiet one. Performance keeps the values verified on this laptop (`docs/hardware/evidence.md`).

## The window
One page:
- **Mode switch:** Eco | Balanced | Performance. Performance is greyed out on battery or with a charger below the laptop's requirement.
- **Fan switch:** Auto alone in Eco and Balanced; Auto | Custom in Performance.
- **Curve:** the curve in use. Custom has 7 points at 60–90 °C that drag up and down (Shift-drag moves the whole curve); the point under the mouse shows its percentage of top speed.
- **Info line:** CPU and GPU temperature and fan speeds on the left, the version on the right.
- **Warning line:** amber, above the curve, only when something is wrong.

Closing the window keeps the app in the tray. The tray diamond shows the mode (green Eco, grey Balanced, red Performance); hovering it shows temperatures and fan speeds, or the current warning when there is one. Right-click: Open, the three modes, Exit. The OMEN key opens and hides the window.

## The modes
| | Eco | Balanced | Performance (needs the charger) |
|---|---|---|---|
| BIOS thermal profile | `0x30` | `0x30` | `0x04` (OGH's Unleashed) |
| CPU power | firmware default | firmware default | PL1/PL2 80 W, concurrent TDP 82 W |
| GPU preset [cTGP, PPAB, dState, temp target] | Base `0,0,1,75` | Boost `0,1,1,87` | Max `1,1,1,87` |
| Fans (charger in) | Auto | Auto | Auto or Custom |
| Windows power mode | Best power efficiency | Balanced | Best performance |

**Power rules:**
- Unplug → Eco (auto), firmware drives the fans. A charger below the laptop's requirement also gives Eco (auto), but the app keeps the fans.
- Plug back in → Balanced. A manually chosen Eco stays Eco.
- Shutting down in Performance → the next start is Balanced.
- After sleep everything is applied again (this laptop uses Modern Standby, which sends no wake-up event: the app notices the gap in its own timer).

**Safety in Performance (OGH's own rules):**
- **Max Battery Drain 40 %:** draining below 60 % while on the charger, with the GPU busy (load over 30 %, or unknown), lowers PL1/PL2 1 W per 5 s, down to 45 W.
- **Chassis 54 °C:** GPU target drops to 75 with PPAB off and PL1/PL2 drop 5 W per 30 s; at 49 °C or below, Max comes back.

## Fans
- **Auto** = one quiet curve, used in every mode. Both fans follow the hotter chip:

  | °C | ≤60 | 65 | 70 | 75 | 80 | 85 | 90 |
  |---|---|---|---|---|---|---|---|
  | rpm | 1900 | 2100 | 2400 | 2900 | 3400 | 4200 | 4800 |

- **Custom** (Performance only) = one curve, 7 points at 60, 65, 70, 75, 80, 85, 90 °C. Below 60 °C the 60 °C value applies, above 90 °C the 90 °C value.
  - Each fan follows it from its own chip (CPU fan from CPU, GPU fan from GPU). While the GPU sleeps, both follow the CPU.
  - Limits: 1900–4800 rpm, never falling as it gets hotter, and from 75 °C up never below 2900 rpm (OGH's own lower bound for this board).
- **Movement:** every 5 s, at most 300 rpm per step. A mode change, the 85 °C rule and the thermal guard set the new level at once. While the app drives them, the fans never stop.
- **85 °C rule** (Auto and Custom): CPU at 85 °C or more for a minute → both fans to 4800 rpm until it is under 80 °C.
- **Firmware takes the fans** (its own curve) when:
  - on battery;
  - for about two minutes after leaving Performance (so the 55 W limit written on the way out lapses instead of being held), and likewise at start when a limit from the previous session is still set (e.g. right after an update). Choosing Eco or Balanced in that time changes the Windows power mode at once; the firmware gets it when the old limit is gone, because any firmware write would keep the limit alive;
  - after an unexpected error (fail safe: nothing more is written until the app restarts; red tray icon);
  - the CPU temperature can't be read (PawnIO missing or failing);
  - the fans don't follow what is written (refused 8 times, far below the level asked for a minute, or still stalled 30 s after the thermal guard set them to maximum);
  - the app exits, crashes (within about 2 minutes) or is uninstalled.
- **Thermal guard:** CPU ≥ 95 °C, ambient ≥ 62 °C, or a warm CPU with stalled fans → fans to maximum. Released after a minute below 85 / 54 °C. In every curve, Auto or Custom, and also on battery and during the wait after Performance.

The firmware's own throttling and thermal shutdown are never touched.

## Every BIOS call it can make (anything else is refused in code before it is sent)
Through Windows' own WMI channel `root\wmi hpqBIntM`. Packet = `"SECU"` + command + type + data.

| Command | Type | Dir | Data | Used for |
|---|---|---|---|---|
| 0x20008 | 16 | read | 4 zero bytes | only right before a fan-level write: arms the firmware's hold on the levels |
| 0x20008 | 26 | write | `FF, 0x30 or 0x04, fans-by-BIOS 0/1, 00` | thermal profile; re-sent every 45 s, except during the wait after Performance |
| 0x20008 | 33 / 34 | read / write | GPU record (cTGP, PPAB, dState 0/1; target 75..87) | GPU presets, chassis rule |
| 0x20008 | 35 | read | sensor index | chassis (IR) for the 54 °C rule, ambient for the guard |
| 0x20008 | 41 | write | `PL, PL, FF, FF` (45..80 W) or `FF, FF, FF, TDP` (20..82 W) | Performance power |
| 0x20008 | 42 | read | none | read-back of power writes; checks that an old limit has lapsed |
| 0x20008 | 45 | read | 4 zero bytes | fan speeds |
| 0x20008 | 46 | write | 128 B: `cpu, gpu, 0…` (19..48) | fan levels |
| 0x20008 | 53 | read | `0, 4, 0, 0` | BIOS thermal-throttling flag |
| 1 | 15 | read | 4 zero bytes | charger status |

Power and GPU writes made while the app runs are checked by reading them back, and fan writes by reading the fan speeds (from 2500 rpm up, over a minute). The firmware has no known read for the thermal profile, and the writes on the way out (leaving Performance, fail safe, start-up TDP repair, uninstall) are not read back; the heartbeat's re-send of Performance power is checked by the next heartbeat. Out-of-range values make `HpBios` throw instead of being clamped; the fan curve keeps its own targets inside 19..48 before they get there. The graphics (MUX) mode is never read or written.

**Wrong laptop:** anything but an OMEN 16-ap0 with board `8E35` and the setup refuses to install. If the app starts and the model or board does not match (or the identity cannot be read), it only monitors and writes nothing. A BIOS other than the validated `F.13` shows a warning.

## Other things on the PC it touches
| What | Where | When |
|---|---|---|
| Program | `C:\Program Files\Salva OPM\SalvaOPM.exe` | setup |
| Logon task | `SalvaOPM` (your logon, highest privileges) | setup; removed by uninstall |
| Start-menu shortcut, Apps entry | `Salva OPM` | setup; removed by uninstall |
| Settings and log | `C:\ProgramData\SalvaOPM\state.txt`, `log.txt` (256 KB, then one older copy `log.txt.old`), `running.flag` (removed by a clean exit), `setup-result.txt` (the last setup's message). Folder writable by administrators only | changes and events only |
| PawnIO driver | its official installer 2.2.0, downloaded from its GitHub release, SHA-256 checked, run from the administrators-only folder and deleted | setup, only when missing or older than 2.2.0; uninstall removes it again (PawnIO's own uninstaller) if this setup installed it |
| OMEN Gaming Hub | **removed at install:** Store app `AD2F1837.OMENCommandCenter`, tasks `OmenInstallMonitor*` and `OmenOverlay*`, folders `Program Files\HP\OmenInstallMonitor` and `\Overlay`; service `HPOmenCap` stopped and disabled; its running helpers (`OverlayHelper`, `OMENKeyboardRemapper`, `OMENKeyboardRemapLauncher`, `OmenCap`) ended | setup |
| OGH's background, if it ever returns | ended | app start and every 45 s, except during the wait after Performance |
| Windows power mode | the AC/DC power-mode setting | on mode changes |

Salva OPM has no service of its own, no telemetry and no auto-update. The app never uses the network; the setup downloads PawnIO once, only when it is missing or older than 2.2.0.

## What each outside piece is, and how it was checked
| Piece | Source | Checked |
|---|---|---|
| Fan, profile and power calls | Linux kernel `hp-wmi.c` lists board 8E35 and drives fans with the same call (`0x2E`), with the same `0x10` trigger and 120 s timeout; Ohman lists 8E35 as owner-verified | run on this laptop (`docs/hardware/evidence.md` sections 0, 12 and 14) |
| Windows power mode | `PowerSetUserConfiguredACPowerMode` / `DCPowerMode`, documented by Microsoft; the three GUIDs are the documented ones | Windows setting follows the mode |
| PawnIO | signed by "Microsoft Windows Hardware Compatibility Publisher"; only the read call `ioctl_read_smn` is used | running on this PC with Memory Integrity (HVCI) on |
| GPU temperature | NVIDIA's own `nvml.dll` from `System32`; not polled while the GPU sleeps | 20 open/read/close cycles: no handle growth |
| OGH removal | Windows' own `Remove-AppxPackage` / `Remove-AppxProvisionedPackage` | done on this PC 2026-11-12, package gone |

## Removing it
- Open `SalvaOPM-Setup.exe` again → **Uninstall**, or Settings > Apps > Salva OPM.
- Uninstall puts the firmware back (skipped only on a laptop identified as another model): profile `0x30` with the firmware's own fan control, concurrent TDP baseline, the GPU record read at first start (`00 01 00 57`).
- It sets `HPOmenCap` to Automatic and starts it again; HP's own software may then reinstall OMEN Gaming Hub, as on a stock laptop.
- It sets the Windows power mode back to Balanced (charger and battery).
- It removes the program, task, shortcut, Apps entry and settings.
- OMEN Gaming Hub is not restored: install it from the Microsoft Store if you want it back. Keyboard RGB keeps its last setting meanwhile.
- PawnIO is removed too when this setup installed it; a PawnIO that was there before is left alone. PawnIO's own uninstaller keeps its signed driver package in Windows' driver store (stopped, started by nothing); `pnputil /enum-drivers` lists it as `pawnio.inf`.
- Your custom curve and other settings are deleted with the app.

## Known differences
- **Performance** uses OGH's Unleashed byte `0x04` + 80 W. Ohman would send `0x31` without CPU limits.
- **Auto fan curve** is the owner's quiet curve, not Ohman's (Ohman's ran 2300 rpm at idle and 4600 rpm from 75 °C).
- **Not included:** max/manual fan modes, graphics switch, keyboard lighting, benchmark, power-gain slider.
- **No guarantee:** this is a personal tool on one laptop. The firmware's protections stay in charge; a BIOS update needs the tests repeated.
