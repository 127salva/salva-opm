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
| `CurveTests` | Auto and custom curves, clamping, stepping, smoothing, the 85 °C rule |
| `GuardTests` | thermal guard engage, hold, release |
| `IdentityTests` | which laptop may be written to |
| `FirmwareTests` | the allow-list for every type 0–255 and several sizes; value ranges; simulator behaviour |
| `CoreTests` | the Engine end to end against the simulator: apply order, read-back and rollback, Performance kept for 10 minutes, lapse after Performance and at start-up, TDP changed after a GPU write, fans to firmware on battery or without a temperature, fans not following, crash, exit, conflicts, fail safe, monitoring-only writes nothing |
| `SettingsTests` | round trip, bad values ignored, older curves raised to the lower bound |
| `EnduranceTests` | 14 simulated days (one session per day, sometimes restarted within seconds like an update) with random load, unplugging, sleep, mode changes, missing temperatures and 1 % failing BIOS calls. Every 5 s: PL within 45..80, never Performance on battery or saved, every lapse over within 180 s (time with the thermal guard on does not count: the guard restarts the wait), profile `0x04` never left behind, fans at maximum after 90 s at 85 °C, and no memory growth from day 1 to day 14. Fixed seed, so a failure repeats |

The simulator (`SimFirmware`) encodes behaviour measured on the laptop: concurrent TDP only applies under profile `0x04`; host state lapses about 110 s after the last refresh; fan levels are ignored while the firmware has the fans.

## What the self-checks cannot cover
| Not covered | How it is checked instead |
|---|---|
| The real firmware, driver and sensors | `docs/user-tests.md`, run on the laptop; results recorded in `docs/hardware/evidence.md` |
| Install, update, uninstall, the PawnIO download | by hand on the laptop (`docs/user-tests.md` H4, H6) |
| Memory and handles of the real process over hours | sampling the installed app for 8 hours (`docs/user-tests.md` H5) |
| The window (drawing, dragging) | by eye in `--demo` |
| OMEN key, sleep and resume | by hand on the laptop |
| Other BIOS versions | none: only `F.13` is validated; the app warns on any other |

## Every feature: how it is checked
Status of 2.5.1 (2026-11-18). "Laptop" cites `C:\ProgramData\SalvaOPM\log.txt` lines recorded in `docs/user-tests.md` and `docs/hardware/evidence.md`. Self-check names are quoted from `src/SelfCheck.cs`.

| Feature | Self-checks | On the laptop |
|---|---|---|
| Mode rules: unplug → Eco, replug → Balanced, manual Eco kept, Performance never on battery or after a restart | `ModeTests` (27 rows), "Performance refused on battery", "replug: Balanced, software fans again" | unplug/replug several times (2.0–2.2); ~11 times in H3; start on battery → Eco |
| Performance power PL1/PL2 80 W, TDP 82 W, read back | "Performance order: profile -> fans -> PL -> TDP -> GPU", "Performance held for 10 min" | `PL 80/80, TDP 82 verified` on every entry (H1, H3, H5, H7) |
| TDP reset by the firmware after a GPU write | "TDP changed after the GPU write: written again at once", "… a second after …" | H3 found it; H3b, H1 (caught 9 s later) and H7 (`written again`) |
| Auto curve and fan movement | `CurveTests`, "rising temperature: both fans climb …", "Auto curve written at boot …" | levels 19–48 accepted (2.0); Balanced 75–76 °C while gaming on the quiet curve (H1) |
| Firmware fan choice (any mode, remembered per mode; no guard, no 85 °C rule) | "Balanced on Firmware …", "Eco keeps its own choice …", "the per-mode fan choice survives a restart", "Firmware chosen: … no thermal guard …", "Performance on Firmware: PL 80/80 …" | H9 (2026-11-18): Performance on Firmware 57 min while gaming, PL 80/80 held, no drift; switch logged; lapse 131 s afterwards. CPU reached 98 °C on the firmware's curve (no app rule acts, by design) |
| Custom curve (Performance only) | custom rows in `CurveTests`; "Custom in Performance …", "the choice and the custom curve survive a restart" | in daily use on 2.1–2.3; not re-tested on its own on 2.4 |
| 85 °C rule | four rows in `CurveTests`; "85 C for a minute: fans to 4800 rpm"; endurance invariant | H1 (engaged 14:35:00, released 14:46:45) and H5 (22:44:43); removed in 2.4.6, back in 2.4.7 |
| Thermal guard (CPU ≥ 95 °C, ambient ≥ 62 °C, stalled fans) | `GuardTests`; "thermal guard: CPU >= 95 …", "… on battery takes the fans …", "the thermal guard still takes the fans during the lapse", "a guard during the lapse …: the lapse starts again after it …" | **observed on 2.4.6** (2026-11-14): engaged 04:32:04 at CPU 95 °C while gaming in Performance on a custom curve topping out at 4300 rpm; released 04:37:25, a minute after the game ended |
| Battery-drain rule (40 %) | `GovernorTests` battery rows | **never observed** |
| Chassis 54 °C rule | `GovernorTests` chassis rows; "governor (chassis 54) …" | **never observed** |
| Fans not following → firmware | "fans far below the level written for a minute …", "fan writes refused 8 times …" | **never observed** (the fans always followed) |
| No CPU temperature → firmware fans | "three missing readings in a row: fans handed to the firmware", "CPU temperature back …" | **never observed** (PawnIO always answered) |
| Lapse after Performance and at start | "leaving Performance: PL 55 written …", "boot with a host limit still set …"; endurance invariant (≤ 180 s) | `host limits lapsed … PL 0/0` 125–131 s later in H2, H6, H3b, H1, H5, H7 |
| Mode change during the lapse is deferred | "a mode change while the old limit lapses …" | H3b; H7 (23:34:12) |
| Wake from Modern Standby (no resume event) | none (timer in `App`) | H3b: `woke from standby (106 s)`, Performance re-applied |
| Fail safe after an unexpected error | "fail safe: 10 min and every later event without one write …" | **never observed** (no unexpected error so far) |
| One copy per PC (`Global\SalvaOPM`) | none | code review only; not tried with two Windows users signed in |
| Other laptop → setup refuses, app only monitors | `IdentityTests`; four "monitoring only" rows | cannot be tried here (one laptop) |
| Allow-list, value ranges, read-only mode | `FirmwareTests` (all types 0–255, sizes, ranges, type 16 refused in `--read`) | every write in the logs inside the ranges; no refusal |
| Exit, crash, shutdown hand back to the firmware | "crash: …", "exit: fans parked …", "exit in Performance: …" (power, profile, fans and GPU record) | shutdown: fans parked and handed back each time (2.0–2.2) |
| Install, update, uninstall, restore | none | H4 (everything removed and restored, factory GPU record read back), H6, H2, 2.4.3 update |
| PawnIO download and SHA-256 | none | H4: 3,410,960 bytes, hash matched, exit 0, staging folder deleted |
| OMEN Gaming Hub removal | none | 2.0 install; every install logs `OMEN Gaming Hub package is not installed any more` |
| Without HP's drivers (fresh Windows) | none | H7: everything above worked with `HP Omen Driver` and `HP Application Driver` removed |
| No NVIDIA driver (`nvml.dll` missing) | "Auto: GPU only (CPU unknown) drives both", "custom: with the GPU asleep or unknown both fans follow the CPU (safe)" | not tried (the driver is always present here) |
| Firmware work on its own thread: clicks show at once, fast clicks apply only the last, the fan choice goes to the mode it was clicked in | "hardware thread: fast clicks collapse …", "a fan choice for another mode is saved for that mode …" | 2026-11-18 19:44–19:56 and 20:12–20:14: fast mode and fan clicks all applied and verified, clean tray Exit in Performance, wake re-applied |
| Window 2.5.0: graph hover, a filling graph, ring glide, slide and fade, keyboard | "graph hover: the age …", "graph: the span …", "ring glide …", "window motion …" | looked at by the owner on 2026-11-18; keyboard use not reported yet |
| Setup right after an uninstall (shows Install, waits for the old folder) | none | 2026-11-18 20:15:16: `install: waited 2 s …`, `installed 2.5.0` at the first try |
| OMEN key | none | pass on 2.4.0 and in H7 |
| Tray menu 2.5.1: small dark menu (Open, Exit), a second right-click closes it | none | **not reported yet** (looked at in `--demo` only; check "Tray menu" in `docs/user-tests.md`) |

## Adding a check
New logic gets a check in the matching group. A scenario test builds a `Rig` (simulated firmware, sensors and settings folder), drives the Engine with `RunFor`, and asserts on the simulator's state and its call log.
