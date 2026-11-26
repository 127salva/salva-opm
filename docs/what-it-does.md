# Salva OPM: what it does (and doesn't)

Everything Salva OPM touches on the HP OMEN 16-ap0 with board 8E35. Source: `src/` (plain C#, built by `build.cmd` with Windows' own compiler). The one file to run is `dist\SalvaOPM-Setup.exe`. The app works offline; the setup needs internet once, only if PawnIO is missing or older than 2.2.0.

It does two things: it switches the **power modes** and it **shows the chip temperatures and fan speeds**. The fans always run on the firmware's own curve; the app never writes them. Its mode logic follows Ohman (github.com/P4R1H/Ohman, GPL-3.0); Performance keeps the values verified on this laptop (`docs/firmware.md`).

## The window
One page, 490 × 450 px.
- **Header:** the logo and the name (the version is in Settings > Apps, not in the window); on the right a status pill: "Starting…" until start-up is done and the first reading is in (about 5 s), then green "All good" or the current warning (amber; red for the fail safe). With a warning an "i" appears; hovering the pill shows the whole warning and what to do.
- **Mode cards:** Eco, Balanced, Performance, each with an icon above its name, in its name's colour (light when chosen or hovered, grey otherwise); the active card has a lavender border. "Eco (auto)" when the app chose Eco itself. Performance is greyed out on battery or with a charger below the laptop's requirement.
- **CPU and GPU cards** (the CPU card over the GPU card): the chip's icon and name; on the right, the temperature over the fan speed. A sleeping GPU says "Asleep". Until the first reading after start-up (about 5 s), placeholders stand in. Under them two history lines, temperature and fan speed: 10 minutes in 20 slots of 30 s, each slot the highest reading in it, grey where there is no reading (not filled yet, or the laptop was asleep). Readings are taken every 5 s, also while the window is closed. Hovering a line shows that slot's value and how long ago it was.
- **Level colours** (history lines, the status dot): green up to 85 °C or 3500 rpm, amber up to 95 °C or 4200 rpm, red above, grey for no reading.
- **Feel:** a click shows a pressed look at once and applies on release; the chosen card fades in (150 ms), also when the app changes the mode itself (unplugging). Not for keyboard choices, and not when Windows animations are off.
- **Look:** lavender (`#B8A3F5`) on near-black, surfaces with a thin border; Plus Jakarta Sans for words and Oswald SemiBold for values, both embedded in the exe (SIL OFL); Segoe UI if they cannot be loaded. Red, amber and green only mean danger, warning and all good.
- **Screens:** the window is drawn at the scale of the screen it is on (it is built again when moved to a screen with another scale), so it stays sharp on both a 100 % and a 125 % screen.
- **Keyboard:** Tab reaches the mode cards (a light ring shows where); arrow keys pick, Enter or Space applies. A mouse click anywhere in the window hides the ring until the next Tab or arrow key.

Closing the window keeps the app in the tray. The tray diamond shows the mode (green Eco, grey Balanced, lavender Performance); its tooltip is "Salva OPM". Right-click: a small dark menu with Open and Exit (modes are chosen in the window). Exit, sign-out and shutdown leave the laptop on Balanced: profile `0x30`, the GPU on Boost, the Windows power mode Balanced (an Exit before start-up has identified the laptop writes nothing). A sign-out you cancel may still close Salva OPM: Windows ends the apps one at a time, and those it reached before the cancel stay closed (start it again from the Start menu). The OMEN key opens and hides the window.

## The modes
| | Eco | Balanced | Performance (needs the charger) |
|---|---|---|---|
| BIOS thermal profile | `0x30` | `0x30` | `0x04` (OGH's Unleashed) |
| CPU power | firmware default | firmware default | PL1/PL2 80 W, concurrent TDP 82 W |
| GPU preset [cTGP, PPAB, dState, temp target] | Base `0,0,1,75` | Boost `0,1,1,87` | Max `1,1,1,87` |
| Fans | the firmware's own curve | the firmware's own curve | the firmware's own curve |
| Windows power mode | Best power efficiency | Balanced | Best performance |

**Power rules:**
- Unplug → Eco (auto). A charger below the laptop's requirement also gives Eco (auto).
- Plug back in → Balanced. A manually chosen Eco stays Eco.
- Shutting down in Performance → the next start is Balanced.
- After sleep everything is applied again (this laptop uses Modern Standby, which sends no wake-up event: the app notices the gap in its own timer).
- **After Performance**, and at start when a limit from the previous session is still set (e.g. right after an update): for about two minutes nothing is sent, so the 55 W limit written on the way out lapses instead of being held. Choosing Eco or Balanced in that time changes the Windows power mode at once; the firmware gets it when the old limit is gone, because any firmware write would keep the limit alive.

**Safety in Performance (OGH's own rules):**
- **Max Battery Drain 40 %:** draining below 60 % while on the charger, with the GPU busy (load over 30 %, or unknown), lowers PL1/PL2 1 W per 5 s, down to 45 W.
- **Chassis 54 °C:** GPU target drops to 75 with PPAB off and PL1/PL2 drop 5 W per 30 s; at 49 °C or below, Max comes back.
- After a cut, the limit only climbs back while the CPU is not throttling (the firmware's throttle flag is off and the CPU is under 90 °C).

**Heat:** the firmware's own fan curve, throttling and thermal shutdown are always in charge; the app has no heat rule of its own. In Performance gaming the CPU can sit near 95–98 °C on the firmware's curve (seen on the laptop while gaming).

**Unexpected error:** the app leaves Performance, puts profile `0x30` back and writes nothing more to the firmware until it restarts (fail safe; red tray icon). Exit, sign-out and shutdown still set the Windows power mode back to Balanced.

## Every BIOS call it can make (anything else is refused in code before it is sent)
Through Windows' own WMI channel `root\wmi hpqBIntM`. Packet = `"SECU"` + command + type + data.

| Command | Type | Dir | Data | Used for |
|---|---|---|---|---|
| 0x20008 | 26 | write | `FF, 0x30 or 0x04, 01, 00` (byte 2 = 1: the firmware drives the fans) | thermal profile; re-sent every 45 s, except during the wait after Performance |
| 0x20008 | 33 / 34 | read / write | GPU record (cTGP, PPAB, dState 0/1; target 75..87) | GPU presets, chassis rule |
| 0x20008 | 35 | read | sensor index | chassis (IR) for the 54 °C rule |
| 0x20008 | 41 | write | `PL, PL, FF, FF` (45..80 W) or `FF, FF, FF, TDP` (20..82 W) | Performance power |
| 0x20008 | 42 | read | none | read-back of power writes; checks that an old limit has lapsed |
| 0x20008 | 45 | read | 4 zero bytes | fan speeds (window and log only) |
| 0x20008 | 53 | read | `0, 4, 0, 0` | BIOS thermal-throttling flag |
| 1 | 15 | read | 4 zero bytes | charger status |

No fan command is on the list: fan levels (type 46), their trigger (type 16) and max fan (type 39) are refused before they are sent. Power and GPU writes made while the app runs are checked by reading them back. The firmware has no known read for the thermal profile, and the writes on the way out (leaving Performance, fail safe, start-up TDP repair, uninstall) are not read back; the heartbeat's re-send of Performance power is checked by the next heartbeat. Out-of-range values make `HpBios` throw instead of being clamped. The graphics (MUX) mode is never read or written.

**Wrong laptop:** anything but an OMEN 16-ap0 with board `8E35` and the setup refuses to install (it checks before the administrator prompt and, started normally, leaves nothing on that PC; started with "Run as administrator", `C:\ProgramData\SalvaOPM` keeps its message). If the app starts there anyway, or the identity cannot be read at start-up (after retries), it says so and closes without a single firmware call. A BIOS other than the validated `F.13` shows a warning.

## Other things on the PC it touches
| What | Where | When |
|---|---|---|
| Program | `C:\Program Files\Salva OPM\SalvaOPM.exe` | setup |
| Logon task | `SalvaOPM` (your logon, highest privileges) | setup; removed by uninstall |
| Start-menu shortcut, Apps entry | `Salva OPM` | setup; removed by uninstall |
| Settings and log | `C:\ProgramData\SalvaOPM\state.txt`, `log.txt` (256 KB, then one older copy `log.txt.old`), `running.flag` (removed by a clean exit), `setup-result.txt` (the last setup's message). Folder writable by administrators only | changes and events only |
| PawnIO driver | its official installer 2.2.0, downloaded from its GitHub release, SHA-256 checked, run from the administrators-only folder and deleted. It gives the CPU temperature (shown in the window, and used by the Performance governor) | setup, only when missing or older than 2.2.0; uninstall removes it again (PawnIO's own uninstaller) if this setup installed it |
| OMEN Gaming Hub | **removed at install:** Store app `AD2F1837.OMENCommandCenter`, tasks `OmenInstallMonitor*` and `OmenOverlay*`, folders `Program Files\HP\OmenInstallMonitor` and `\Overlay`; service `HPOmenCap` stopped and disabled; its running helpers (`OverlayHelper`, `OMENKeyboardRemapper`, `OMENKeyboardRemapLauncher`, `OmenCap`) ended | setup |
| OGH's background, if it ever returns | ended | app start (once the laptop is identified) and every 45 s, except during the wait after Performance |
| Windows power mode | the AC/DC power-mode setting (the one for the power source in use) | on mode changes, and when plugging in or unplugging |

Salva OPM has no service of its own, no telemetry and no auto-update. The app never uses the network; the setup downloads PawnIO once, only when it is missing or older than 2.2.0.

## What each outside piece is, and how it was checked
| Piece | Source | Checked |
|---|---|---|
| Profile and power calls | Linux kernel `hp-wmi.c` lists board 8E35 and uses the same thermal-profile call; Ohman lists 8E35 as owner-verified | run on this laptop (`docs/firmware.md`) |
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

## Known differences
- **Performance** uses OGH's Unleashed byte `0x04` + 80 W. Ohman would send `0x31` without CPU limits.
- **Not included:** fan control of any kind (the firmware's curve always runs), graphics switch, keyboard lighting, benchmark, power-gain slider.
- **No guarantee:** this is a personal tool on one laptop. The firmware's protections stay in charge; a BIOS update needs the tests repeated.
