# Architecture

One Windows process, one thread for all hardware work. The app never uses the network; the setup downloads the pinned PawnIO installer once when PawnIO is missing or older than 2.2.0. It is the installer, the app and its diagnostic tools in a single exe.

## Stack
| Choice | Why |
|---|---|
| C# 5 on .NET Framework 4.8, WinForms | Both ship with Windows 10/11, and so does the compiler (`csc.exe`). Nothing to install to build or run. |
| HP BIOS WMI interface (`root\wmi`, class `hpqBIntM`) | The same channel OMEN Gaming Hub uses; served by Windows' own ACPI-WMI driver. |
| PawnIO + `AMDFamily17` module | The only way to read the CPU die temperature on this laptop without a driver of our own. |
| NVIDIA `nvml.dll` | GPU temperature and load without starting a process. |
| Task Scheduler logon task, highest privileges | The BIOS interface needs administrator rights; a task avoids a prompt at every logon. |

## Modules (`src/`)
```
Program.cs       entry point, command switches, Logger, Preflight (--check), Tools (--read, --dry-run)
App.cs           the running app: tray icon, 5 s timer, power events, OMEN key, window lifetime; Identity check
Ui.cs            MainForm: the one-page window
Controls.cs      Th (theme), Seg (segmented switch), CurveView (curve editor)
Setup.cs         install / update / uninstall and the setup window
Ogh.cs           Tasks (Task Scheduler COM), Ogh (OMEN Gaming Hub removal)

Engine.cs        orchestration: applies modes, drives fans, runs guard, heartbeat and governor
StateMachine.cs  ModeController: which mode is in effect (pure)
FanCurve.cs      FanCurve, HeatPush (85 °C rule), ThermalGuard (pure)
Governor.cs      Performance safety rules: battery drain, chassis temperature (pure)

Firmware.cs      FirmwareRules (allow-list), HpBios (typed, range-checked calls), WmiFirmware (transport)
SimFirmware.cs   simulator of the firmware for self-checks and --demo
Sensors.cs       ISensors; PawnIO CPU temperature; NVML GPU temperature
PowerWatcher.cs  AC/battery state, charger check, Windows power mode
Settings.cs      state.txt: load with validation, atomic save
SelfCheck.cs     the self-checks
```

## Layers
```
UI (Ui, Controls, App)
        │ calls                       ▲ Changed event
        ▼                             │
Engine ──► pure rules (ModeController, FanCurve, HeatPush, ThermalGuard, Governor)
        │
        ▼
HpBios ──► IFirmware ──► WmiFirmware (real)  │  SimFirmware (tests, demo)
ISensors ─► SystemSensors (real)             │  SimSensors
IPowerSource / IPowerModeSetter ─► real      │  simulated
```
The Engine never touches WMI, drivers or Windows directly; everything goes through an interface with a simulated twin, so the whole engine runs in the self-checks.

## Timing (Engine.Tick, called every 5 s on the UI thread)
| Every | Work |
|---|---|
| 5 s | read temperatures; one fan-curve step; governor step in Performance |
| 10 s | thermal guard: read fan speeds and ambient sensor; fan watch |
| 45 s (not during the wait after Performance) | heartbeat: re-send profile (and Performance power); end OGH's background if it returned; retry a failed apply |

## Key design decisions
- **Allow-list at the transport.** `FirmwareRules.IsAllowed` names every (command, type, input size, output size) the app may send. Both transports enforce it.
- **Read-back, not return codes.** The firmware answers `0` to writes it ignores (measured). Power and GPU writes made while running are verified by a read; fan writes are checked against the measured fan speeds (from 2500 rpm up, over a minute). The firmware has no known read for the thermal profile, and the writes on the way out (leaving Performance, fail safe, start-up TDP repair, uninstall) are not read back.
- **The firmware is the fallback.** The firmware forgets host state (power limits, profile, fan levels) about two minutes after the last refresh, so after a crash or a hang it takes them back by itself. The GPU record is the exception: it stays until the app starts again (see below).
- **Type 16 only before a fan write.** That query arms the firmware's "host controls the fans" state; sending it otherwise can hold the fans.
- **Lapse after Performance.** Power limits cannot be cleared by a write, only by letting them lapse. After Performance the app stays silent with the firmware driving the fans: at least 125 s, until PL reads 0/0 (at most 180 s); if the thermal guard has to act in that time, the wait starts again when it releases.
- **Lapse at start-up too.** A host power limit found at start (the previous session ended moments ago: update, crash) is left to lapse the same way.
- **Fail safe.** An unexpected error hands fans and power to the firmware and stops every write until the app restarts.
- **Fail closed on identity.** On anything but a 16-ap0 with board `8E35` the setup refuses to install, and the app only monitors. The uninstall's firmware restore is skipped only on a laptop positively identified as another model; when the identity cannot be read it still restores, because the GPU record does not lapse by itself.
- **One copy per PC.** A machine-wide lock (`Global\SalvaOPM`) keeps a second Windows session from running a second copy.
- **Saved mode is never Performance.** A restart always comes back in Eco or Balanced.
- **The GPU record is put back by the app.** Unlike power limits it does not lapse: exit and fail safe write Boost, the next start writes the preset of its mode; uninstall writes the record read at first start.
- **Sleep is detected from the timer.** This laptop has Modern Standby, which sends no resume event to the app: a gap of over 30 s between 5 s ticks counts as a wake-up.
- **PawnIO is downloaded, not shipped.** Its installer has no stated redistribution terms, so the setup downloads the pinned release once, checks its SHA-256 and runs it from the administrators-only folder.
- **Unsigned on purpose.** No code-signing certificate; each release publishes the SHA-256 of the setup instead (README explains SmartScreen).
- **Uninstall leaves Windows in Balanced** on both AC and DC, the Windows default, whatever mode was last used.
- **License GPL-3.0-or-later**, as Ohman, whose logic it follows; adapted files say so in their header.

## State on disk
`C:\ProgramData\SalvaOPM\state.txt` (key=value): saved mode, the concurrent-TDP baseline, the GPU record read before the first write, Performance's fan choice, the custom curve. `log.txt` beside it holds events (256 KB, one rotation).

## Source of the firmware facts
`docs/hardware/evidence.md` records how each call, value and behaviour was established: decoded from OGH, taken from Ohman or the Linux `hp-wmi` driver, or measured on the laptop.
