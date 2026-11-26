# Testing

## Self-checks
One assert-based suite in `src/SelfCheck.cs`, with no test framework (the project has no dependencies). It runs in two ways:

```bat
build.cmd selftest
```
```powershell
& .\dist\SalvaOPM-Setup.exe --selftest | Out-Host
```
Both print `self-check: N passed, 0 failed` and return the number of failures as the exit code. `build.cmd` stops when a check fails, and so does CI.

| Group | Covers |
|---|---|
| `ModeTests` | every row of the plug / unplug / boot / resume rules |
| `GovernorTests` | battery-drain and chassis rules, floors, recovery, 3000 random steps staying in range |
| `PowerRuleTests` | drain detection, charger classification, flap guard |
| `IdentityTests` | which laptop may be written to |
| `FirmwareTests` | the allow-list for every type 0–255 and several sizes, with no fan command (types 16, 39, 46) at any size; the profile write always leaves the fans to the firmware; value ranges; simulator behaviour (host limits held, then dropped; also without its call log and on the real clock, as `--demo` runs it); read-only mode; the CPU temperature formula |
| `CoreTests` | the Engine end to end against the simulator: apply order, read-back and rollback, Performance held 10 minutes by the heartbeat alone, the Windows power mode slot after plugging in or out, lapse after Performance and at start-up (one profile write, then silence), TDP changed after a GPU write, a failed GPU read-back or heartbeat power read, a lasting BIOS failure logged once, no CPU temperature, crash, exit (from Balanced, Eco and Performance), the uninstall's restore, concurrent TDP put back even when the power read fails, conflicts, fail safe |
| `SettingsTests` | round trip, bad values ignored, fan keys of older versions dropped |
| `WindowTests` | the window and setup window (history slots, hover, level colours, layout and text alignment, fonts, icons, animations, a hint for every warning, the exe's Properties) and the hardware thread (fast clicks collapse, a failing job reaches the fail safe) |
| `EnduranceTests` | 14 simulated days (one session per day, sometimes restarted within seconds like an update) with random load, unplugging, sleep, mode changes, missing temperatures and 1 % failing BIOS calls. Every 5 s: PL within 45..80, never Performance on battery or saved, every lapse over within 180 s, profile `0x04` never left behind, every profile write with the fans left to the firmware, and no memory growth from day 1 to day 14. Fixed seed, so a failure repeats |

The simulator (`SimFirmware`) encodes behaviour measured on the laptop (`docs/firmware.md`): concurrent TDP only applies under profile `0x04`; host state lapses about 110 s after the last refresh; fan reads give the firmware's own levels.

## What the self-checks cannot cover
| Not covered | How it is checked instead |
|---|---|
| The real firmware, driver and sensors | by hand on the laptop, read from the app's log (table below) |
| Install, update, uninstall, the PawnIO download | by hand on the laptop |
| Memory and handles of the real process over hours | the installed app sampled every 5 minutes for 8 hours |
| The window (drawing, alignment) | by eye in `--demo`, offscreen renders, and on the laptop's two screens |
| OMEN key, sleep and resume | by hand on the laptop |
| Refusing to run on another laptop | code review (the identity read comes before any firmware call); cannot be tried (one laptop) |
| Other BIOS versions | none: only `F.13` is validated; the app warns on any other |

## Every feature: how it is checked
"Seen" means watched on the laptop (HP OMEN 16-ap0, board 8E35, BIOS F.13) and read from the app's log; "simulator only" means only the self-checks cover it.

| Feature | Self-checks | On the laptop |
|---|---|---|
| Mode rules (unplug → Eco, replug → Balanced, manual Eco kept, no Performance on battery or after a restart) | `ModeTests`, `CoreTests` | seen, many times |
| Performance: PL1/PL2 80 W and concurrent TDP 82 W, read back | `CoreTests` | seen on every entry; 57 minutes of gaming held by the heartbeat alone |
| TDP reset by the firmware after a GPU write | `CoreTests` | seen, and written again each time |
| Fans always on the firmware's curve (no fan command) | `FirmwareTests`, `EnduranceTests` | seen: only profile writes with byte 2 = 1, the firmware's own fan speeds in the log |
| Lapse after Performance and at start-up; mode changes deferred during it | `CoreTests`, `EnduranceTests` | seen: the limit gone 125–133 s after leaving Performance |
| Wake from Modern Standby (no resume event) | none (timer in `App`) | seen |
| Battery-drain rule (40 %) | `GovernorTests` | simulator only (never triggered on the laptop) |
| Chassis 54 °C rule | `GovernorTests`, `CoreTests` | simulator only (never triggered on the laptop) |
| No CPU temperature (PawnIO missing) | `CoreTests` | simulator only (PawnIO always answered) |
| A BIOS call that fails (warning, retry, logged once) | `CoreTests`, `EnduranceTests` | simulator only (no call failed on the laptop) |
| Fail safe after an unexpected error | `CoreTests` | seen once (a window drawing error in an earlier version; a window error now only closes the window) |
| Exit, sign-out, shutdown end on Balanced | `CoreTests`, `WindowTests` (hardware thread) | seen (tray Exit, plain sign-out); going back to work after a cancelled sign-out never seen |
| Install, update, uninstall and restore; the PawnIO download and its hash | `CoreTests` (the restore) | seen, also uninstall from Settings > Apps |
| OMEN Gaming Hub removal; working without HP's drivers | none | seen |
| Another laptop: the setup refuses, the app refuses to run | `IdentityTests`, `WindowTests` (firmware gate, refusal box) | cannot be tried (one laptop) |
| One copy per PC | none | not tried with two users signed in |
| Firmware work on its own thread (fast clicks collapse) | `WindowTests` (hardware thread) | seen |
| The window: layout, colours, history lines, hover, readings, start-up skeleton, keyboard, both screen scales | `WindowTests` | seen on both screens; a sleeping GPU's "Asleep" never seen |
| Tray menu and the tray diamond | `WindowTests` | the menu seen; the diamond's colours in renders |
| OMEN key | none | seen |
| Hours of running: memory and handles | `EnduranceTests` (14 simulated days) | seen over 8 hours: flat, private memory about +0.3 MB an hour while idle |

## Adding a check
New logic gets a check in the matching group. A scenario test builds a `Rig` (simulated firmware, sensors and settings folder), drives the Engine with `RunFor`, and asserts on the simulator's state and its call log.
