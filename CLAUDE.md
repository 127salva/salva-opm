# Salva OPM: notes for whoever picks this up

A Windows tray app that replaces OMEN Gaming Hub's performance control on **one laptop**: HP OMEN 16-ap0 with board **8E35** (validated BIOS F.13). It writes to the laptop's firmware. Start with `README.md`, then `docs/what-it-does.md`.

## Where it stands (2.4.5, 2026-11-14)
- Feature-complete for its owner. Every check in `docs/user-tests.md` (H1–H7) passed on the laptop; H8 (a fresh Windows install) is not done yet: planned for the owner's next clean Windows install.
- `build.cmd`: 189 self-checks, 0 failures.
- What is proven on hardware, what only in the simulator, what never happened: the table "Every feature: how it is checked" in `docs/testing.md`.
- Releases: GitHub releases of `127salva/salva-opm`; the first public one was v2.4.3; each release has a tag `vX.Y.Z` and its SHA-256 in the release notes. If the tag of the version in `src/Setup.cs` is missing, that version has not been published yet.
- Open points, none blocking:
  - never observed on the laptop: thermal guard, battery-drain rule, chassis 54 °C rule, fail safe, behaviour after a BIOS update (the feature table in `docs/testing.md` is the full list);
  - private memory of the running app grows ~0.3 MB/h while idle (accepted: it starts fresh at every Windows start);
  - PawnIO's own uninstaller leaves its signed driver package in the Windows driver store;
  - the exe is not code-signed (SmartScreen warns; the release publishes the SHA-256).
- Not in scope, on purpose: graphics (MUX) switch, keyboard lighting, other laptops, max/manual fan modes.

## Rules
- **Never run anything elevated and never write to the firmware yourself.** Hardware tests are done by the owner on the laptop. You build, run `--demo` / `--selftest`, and read logs.
- **No claim without evidence.** "Works on the laptop" needs log lines from `C:\ProgramData\SalvaOPM\log.txt` (with times). Otherwise say "simulator only" or "not observed".
- **A new firmware call** needs, in this order: evidence in `docs/hardware/evidence.md`, an entry in `FirmwareRules.IsAllowed` (`src/Firmware.cs`), a typed method in `HpBios`, the behaviour in `SimFirmware`, a self-check, and a row in `docs/what-it-does.md`. Anything not on the allow-list is refused before it is sent.
- **Every write is verified by reading back.** The firmware answers 0 to writes it ignores.
- **Host state lapses.** The firmware drops host power limits, profile and fan levels about two minutes after the last refresh (measured: PL still set at 106 s, gone at 117 s; the simulator uses 110 s). The app waits 125 s, then checks every 5 s until PL reads 0/0 (at most 180 s); on the laptop that took 125–131 s. A profile and GPU re-send during the lapse kept it alive (measured); fan writes and type 16 refresh it by design (Linux `hp-wmi`). So nothing is sent during a lapse: see `StartLapse`, `EndLapse` and the lapse checks in `Engine.Apply` and `Engine.Tick` (`src/Engine.cs`), and `docs/architecture.md`.
- **Pushing, tagging, releasing and anything on GitHub** only with the owner's explicit OK each time.
- **C# 5 only** (`csc.exe` that ships with Windows, .NET Framework 4.8): no `$"..."`, `?.`, `nameof`, `=>` members, tuples, pattern matching. Warnings are errors.
- **Every source file** starts with `// SPDX-License-Identifier: GPL-3.0-or-later` and `// Copyright (C) 2026 127salva`; files adapted from Ohman say so.
- **Nothing private in the repository:** no serial numbers, product SKU, PC name, Windows user paths, emails or personal settings. Commits use the GitHub no-reply address.
- Repo text, code and commit messages are in English. Plain words, short sentences.

## How the work goes
1. Change code; add or adjust a check in `src/SelfCheck.cs` (a new check must fail without the change).
2. `build.cmd` (or `build.cmd selftest` for the checks alone); look at the window with `dist\SalvaOPM-Setup.exe --demo`.
3. Bump `Setup.Version` (`src/Setup.cs`) and add a `CHANGELOG.md` entry.
4. The owner installs it (setup → **Update**) and runs the matching check from `docs/user-tests.md`, then says which check and the time. Read the log for that window of time (directly, when the session runs on the laptop; otherwise the owner pastes it); record the result in `docs/user-tests.md` (results table) and `docs/hardware/evidence.md`.
5. Release: push to `main` (CI builds and runs the checks), tag `vX.Y.Z`, publish a GitHub release with the CI's `SalvaOPM-Setup.exe` and its SHA-256 (`docs/development.md`, "Releasing").

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
| `src/Setup.cs` | install, update, uninstall, PawnIO download |

Outside sources: Ohman (github.com/P4R1H/Ohman, read at the commit named in `evidence.md` section 11), the Linux kernel's `hp-wmi.c`, PawnIO (pawnio.eu).
