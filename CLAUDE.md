# Salva OPM: notes for whoever picks this up

A Windows tray app that replaces OMEN Gaming Hub's performance control on one laptop, the HP OMEN 16-ap0 with board 8E35 (validated BIOS F.13). It writes to the laptop's firmware. Start with `README.md`, then `docs/what-it-does.md`.

## Where it stands (2.5.4, 2026-11-19)
- Feature-complete for its owner. Every check in `docs/user-tests.md` (H1–H7) passed on the laptop; H8 (a fresh Windows install) is not done yet: planned for the owner's next clean Windows install. H9 (the Firmware fan choice of 2.5.0) passed on 2026-11-18, and so did the 2.5.0 window and hardware-thread checks (results table in `docs/user-tests.md`).
- 2.5.1 (published) adds the small dark tray menu (Open, Exit; a second right-click closes it). 2.5.2 (published) saves a keyboard curve move when the focus leaves the curve, and a mouse click hides the keyboard focus ring. Laptop check 2026-11-19 (`docs/user-tests.md`): tray menu, Exit and the focus ring pass; the curve save through Tab is simulator only. 2.5.3 (published): keyboard and mouse fixes from a full audit and a self-check for the CPU temperature formula; checked on the laptop 2026-11-19 23:29–23:30. 2.5.4 (built, not published): the "fans handed to the firmware" warning now shows before "Thermal guard: fans at maximum" (simulator only: never seen on the laptop).
- `build.cmd`: 210 self-checks, 0 failures.
- The table "Every feature: how it is checked" in `docs/testing.md` shows, for each feature, whether it was seen on the laptop, only in the simulator, or never.
- Releases: GitHub releases of `127salva/salva-opm`; the first public one was v2.4.3; each release has a tag `vX.Y.Z` and its SHA-256 in the release notes. If the tag of the version in `src/Setup.cs` is missing, that version has not been published yet.
- Open points, none blocking:
  - never observed on the laptop: battery-drain rule, chassis 54 °C rule, fail safe, behaviour after a BIOS update (the feature table in `docs/testing.md` is the full list);
  - private memory of the running app grows ~0.3 MB/h while idle (accepted: it starts fresh at every Windows start);
  - PawnIO's own uninstaller leaves its signed driver package in the Windows driver store;
  - the exe is not code-signed (SmartScreen warns; the release publishes the SHA-256).
- Not in scope, on purpose: graphics (MUX) switch, keyboard lighting, other laptops, max/manual fan modes.

## Commands
```powershell
.\build.cmd                                      # compile (warnings are errors), self-checks, dist\SalvaOPM-Setup.exe
.\build.cmd selftest                             # self-checks only
& .\dist\SalvaOPM-Setup.exe --selftest | Out-Host # the exe is a GUI program: pipe to Out-Host to keep output and exit code
Start-Process .\dist\SalvaOPM-Setup.exe -ArgumentList --demo   # the window against the simulator (state in dist\demo-state)
```
New PC (git, `gh auth login`, commit identity, optional Ohman clone): `docs/development.md`, "Set up on a new PC". Nothing else is needed to build.

## Rules
- **Never run anything elevated and never write to the firmware yourself.** Hardware tests are done by the owner on the laptop. You build, run `--demo` / `--selftest`, and read logs.
- **No claim without evidence.** "Works on the laptop" needs log lines from `C:\ProgramData\SalvaOPM\log.txt` (with times). Otherwise say "simulator only" or "not observed".
- **A new firmware call** needs, in this order: evidence in `docs/hardware/evidence.md`, an entry in `FirmwareRules.IsAllowed` (`src/Firmware.cs`), a typed method in `HpBios`, the behaviour in `SimFirmware`, a self-check, and a row in `docs/what-it-does.md`. Anything not on the allow-list is refused before it is sent.
- **Power and GPU writes made while running are read back; fan writes are checked against the measured fan speeds** (from 2500 rpm up, over a minute: `WatchFans`). The firmware answers 0 to writes it ignores. The thermal profile has no known read, and the writes on the way out (leaving Performance, fail safe, start-up TDP repair, uninstall) are not read back; the heartbeat's re-send of Performance power is checked by the next heartbeat.
- **Host state lapses.** The firmware drops host power limits, profile and fan levels about two minutes after the last refresh (measured: PL still set at 106 s, gone at 117 s; the simulator uses 110 s). The app waits 125 s, then checks every 5 s until PL reads 0/0 (at most 180 s; a thermal guard in that time restarts the wait); on the laptop that took 125–131 s. A profile and GPU re-send during the lapse kept it alive (measured); fan writes and type 16 refresh it by design (Linux `hp-wmi`). So nothing is sent during a lapse: see `StartLapse`, `EndLapse` and the lapse checks in `Engine.Apply` and `Engine.Tick` (`src/Engine.cs`), and `docs/architecture.md`.
- **Pushing, tagging, releasing and anything on GitHub** only with the owner's explicit OK each time. `.claude/settings.json` makes Claude Code ask before `git push`, `git tag`, `gh release` and `gh repo edit`, and refuses elevated launches (`-Verb RunAs`, `runas`). It is a guard, not a security boundary; the rules still apply.
- **One hardware thread.** Every `Engine` call and firmware read runs as a job on `App.Hw` (`HardwareThread`); the window thread never calls the engine directly and reads it only with `Monitor.TryEnter`.
- **C# 5 only** (`csc.exe` that ships with Windows, .NET Framework 4.8): no `$"..."`, `?.`, `nameof`, `=>` members, tuples, pattern matching. Warnings are errors.
- **Every source file** starts with `// SPDX-License-Identifier: GPL-3.0-or-later` and `// Copyright (C) 2026 127salva`; files adapted from Ohman say so.
- **Nothing private in the repository:** no serial numbers, product SKU, PC name, Windows user paths, emails or personal settings. Commits are by `127salva` with the GitHub no-reply address (check `git log -1 --format='%an <%ae>'`; on a new PC the local git name may differ, so pass `-c user.name=127salva` or set it as in `docs/development.md`).
- Repo text, code and commit messages are in English. Plain words, short sentences.

## How the work goes
1. Change code; add or adjust a check in `src/SelfCheck.cs` (a new check must fail without the change).
2. `build.cmd` (or `build.cmd selftest` for the checks alone); look at the window with `dist\SalvaOPM-Setup.exe --demo`.
3. Bump `Setup.Version` (`src/Setup.cs`) and add a `CHANGELOG.md` entry.
4. The owner installs it (setup → **Update**) and runs the matching check from `docs/user-tests.md`, then says which check and the time. Read the log for that window of time (directly, when the session runs on the laptop; otherwise the owner pastes it); record the result in `docs/user-tests.md` (results table) and `docs/hardware/evidence.md`.
   - Uninstall deletes `C:\ProgramData\SalvaOPM`, log included. Split a laptop test in two rounds: first everything judged from the log, then read the log, and only then a round with uninstall or reinstall.
5. Release: push to `main` (CI builds and runs the checks), check the CI artifact's SHA-256, tag `vX.Y.Z`, publish a GitHub release with the CI's `SalvaOPM-Setup.exe` (`docs/development.md`, "Releasing", has every command and the notes template).

After a BIOS update the window warns ("not the validated F.13"): repeat H1 and H3 before trusting it.

## Map
| File | What |
|---|---|
| `docs/what-it-does.md` | every firmware call, every change to the PC, mode by mode |
| `docs/architecture.md` | modules, timing, key design decisions |
| `docs/development.md` | build, demo, C# limits, where each kind of change goes, releasing |
| `docs/testing.md` | the self-checks and the feature table |
| `docs/user-tests.md` | checks on the laptop (H1–H8) and their results |
| `docs/hardware/evidence.md` | how each firmware fact was established (history, oldest first) |
| `docs/troubleshooting.md` | warnings, logs, recovery |
| `src/Engine.cs` | modes, lapse, fans, guard, heartbeat: the core |
| `src/Firmware.cs` | WMI transport, allow-list, `HpBios` |
| `src/Setup.cs` | install, update, uninstall, PawnIO download; `Setup.Version` |
| `src/App.cs` | tray, timers, power events, `HardwareThread` |
| `src/Ui.cs`, `src/Controls.cs` | the window and its owner-drawn controls |
| `src/SelfCheck.cs` | every self-check; scenario tests use a `Rig` (simulator) |
| `.claude/settings.json` | shared Claude Code permissions (personal ones: `settings.local.json`, ignored) |

Gotchas: files are CRLF and UTF-8 without BOM; scripted edits have two traps (`docs/development.md`, "Files"). Changes to the window's look need the owner's approval, one by one.

Outside sources: Ohman (github.com/P4R1H/Ohman, read at the commit named in `evidence.md` section 11), the Linux kernel's `hp-wmi.c`, PawnIO (pawnio.eu).
