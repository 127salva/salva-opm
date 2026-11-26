# Architecture

One Windows process, one thread for all hardware work. The app never uses the network; the setup downloads the pinned PawnIO installer once when PawnIO is missing or older than 2.2.0. It is the installer, the app and its diagnostic tools in a single exe.

## Stack
| Choice | Why |
|---|---|
| C# 5 on .NET Framework 4.8, WinForms | Both ship with Windows 11 and Windows 10 1903 or later, and so does the compiler (`csc.exe`). Nothing to install to build or run. |
| HP BIOS WMI interface (`root\wmi`, class `hpqBIntM`) | The BIOS interface OMEN Gaming Hub also talks to (OGH goes through HP's driver or the `HPOmenCap` service; this app calls the WMI class directly), served by Windows' own ACPI-WMI driver. |
| PawnIO + `AMDFamily17` module | The only way to read the CPU die temperature on this laptop without a driver of our own. |
| NVIDIA `nvml.dll` | GPU temperature and load without starting a process. |
| Task Scheduler logon task, highest privileges | The BIOS interface needs administrator rights; a task avoids a prompt at every logon. |

## Modules (`src/`)
```
Program.cs       entry point, command switches, Logger, Preflight (--check), Tools (--read, --dry-run)
App.cs           the running app: tray icon and menu, 5 s timer, power events, OMEN key, window lifetime;
                 HardwareThread (the one thread for firmware work); Identity check (another laptop: refuse to run)
Ui.cs            MainForm: the one-page window; SlotHistory (a reading as 30 s slots, each its highest value);
                 Hints (warning help)
Controls.cs      Th (theme, fonts, text and icons placed by their ink, level colours), Seg (mode cards), ChipCard
                 (readings, start-up skeleton, 10-minute history lines, hover), StatusPill, TrayMenuRenderer (the dark tray menu)
                 Icons: white masks in res/icons, embedded and tinted when drawn
Setup.cs         install / update / uninstall and the setup window
Ogh.cs           Tasks (Task Scheduler COM), Ogh (OMEN Gaming Hub removal)

Engine.cs        orchestration: applies modes, lapse, heartbeat and governor; reads fan speeds for the window
StateMachine.cs  ModeController: which mode is in effect (pure)
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
        │ posts jobs                  ▲ Changed event
        ▼                             │
HardwareThread (one thread, lock on the engine)
        │                             │
        ▼                             │
Engine ──► pure rules (ModeController, Governor)
        │
        ▼
HpBios ──► IFirmware ──► WmiFirmware (real)  │  SimFirmware (tests, demo)
ISensors ─► SystemSensors (real)             │  SimSensors
IPowerSource / IPowerModeSetter ─► real      │  simulated
```
The Engine never touches WMI, drivers or Windows directly; everything goes through an interface with a simulated twin, so the whole engine runs in the self-checks.

## Timing (Engine.Tick, posted every 5 s to the hardware thread)
All hardware work (every `Engine` call, the charger read, the identity check) runs as a job on one background thread (`HardwareThread`, `App.cs`), inside `lock (engine)`. The window thread only posts jobs and reads the engine with `Monitor.TryEnter`, so it never waits for a WMI call; a click shows the new choice at once, and fast clicks collapse into the last one.

| Every | Work |
|---|---|
| 5 s | read temperatures and fan speeds, window open or not (the window's history and the governor; a read does not keep host state alive, `docs/firmware.md`); governor step in Performance; end of a lapse checked |
| 45 s (not during the wait after Performance) | heartbeat: re-send profile (and Performance power); end OGH's background if it returned; retry a failed apply |

## Key design decisions
- **The fans are the firmware's.** No fan command is on the allow-list (fan levels, type 46; their trigger, type 16; max fan, type 39), and every profile write sets byte 2 = 1, which hands the fans to the firmware's own curve (measured, `docs/firmware.md`). The app keeps to power modes and readings.
- **Allow-list at the transport.** `FirmwareRules.IsAllowed` names every (command, type, input size, output size) the app may send. Both transports enforce it.
- **Read-back, not return codes.** The firmware answers `0` to writes it ignores (measured), so power and GPU writes made while running are verified by a read (the exceptions: `docs/what-it-does.md`).
- **The firmware is the fallback.** The firmware forgets host state (power limits, profile) about two minutes after the last refresh, so after a crash or a hang it takes them back by itself. The GPU record and concurrent TDP are the exceptions: they stay until the app starts again (see below).
- **Lapse after Performance.** Power limits cannot be cleared by a write, only by letting them lapse. After Performance the app stays silent: at least 125 s, until PL reads 0/0 (at most 180 s). Mode choices in that time only change the Windows power mode; the firmware gets them when the limit is gone.
- **Lapse at start-up too.** A host power limit found at start (the previous session ended moments ago: update, crash) is left to lapse the same way. Only the session's first profile write is still sent, once.
- **Fail safe.** An unexpected error in the firmware work (the hardware thread), or at start-up, puts profile `0x30` back (leaving Performance first) and stops every write until the app restarts. An error on the window thread only closes the window (logged as `window error:`): the window never touches the firmware.
- **Fail closed on identity.** On anything but a 16-ap0 with board `8E35` the setup refuses to install, and the app refuses to run: it checks the identity before its first firmware call and closes when it does not match or cannot be read. An Exit during start-up, before the identity is known, writes nothing either, and OMEN Gaming Hub's background is left alone until then. One function (`App.MayWrite`) is that gate for start-up and exit. The setup runs its checks before the administrator prompt, so another PC gets no prompt and no data folder (unless the setup was started with "Run as administrator": then the folder holds its message).
- **Uninstall restores unless it knows better.** The uninstall's firmware restore is skipped only on a laptop positively identified as another model; when the identity cannot be read it still restores, because the GPU record does not lapse by itself.
- **Exit when Windows asks, back to work if the session goes on.** Sign-out and shutdown run the exit writes on `SessionEnding` (Windows asks whether the session may end); at a plain sign-out `SessionEnded` comes too late (seen on the laptop). If the session is still there a minute later, the sign-out was cancelled and the app goes back to work (`App.GoOn`). A forced sign-out can end the app before its exit writes: that is the crash path, and the next start repairs it.
- **Text placed by its ink.** Fonts are sized in pixels for each screen's scale, and rows of text and icons line up by their drawn ink, not by font boxes (`Th.TextInk`, `Th.Box`).
- **One copy per PC.** A machine-wide lock (`Global\SalvaOPM`) keeps a second Windows session from running a second copy.
- **Saved mode is never Performance.** A restart always comes back in Eco or Balanced.
- **The GPU record is put back by the app.** Unlike power limits it does not lapse: exit writes Boost (from Eco or Performance), the fail safe writes Boost when it leaves Performance (in Eco the mode's own preset stays until the app starts again), the next start writes the preset of its mode; uninstall writes the record read at first start.
- **Sleep is detected from the timer.** This laptop has Modern Standby, which sends no resume event to the app: a gap of over 30 s between 5 s ticks counts as a wake-up.
- **PawnIO is downloaded, not shipped.** Its installer has no stated redistribution terms, so the setup downloads the pinned release once, checks its SHA-256 and runs it from the administrators-only folder.
- **Unsigned on purpose.** No code-signing certificate, so SmartScreen warns (README).
- **Uninstall leaves Windows in Balanced** on both AC and DC, the Windows default, whatever mode was last used.
- **License GPL-3.0-or-later**, as Ohman, whose logic it follows; adapted files say so in their header.

## State on disk
`C:\ProgramData\SalvaOPM\state.txt` (key=value): saved mode, the concurrent-TDP baseline, the GPU record read before the first write. `log.txt` beside it holds events (256 KB, one rotation to `log.txt.old`); `running.flag` is there while the app runs (left behind by a crash) and `setup-result.txt` holds the last setup's message.

## Source of the firmware facts
`docs/firmware.md` says how each call, value and behaviour was established: decoded from OGH, taken from Ohman or the Linux `hp-wmi` driver, or measured on the laptop.
