# Salva OPM

A small Windows app that replaces OMEN Gaming Hub's performance control on one laptop, the HP OMEN 16-ap0 with board 8E35. It switches between three modes and runs a fan curve. It does not control keyboard lighting, the graphics switch or other OMEN Gaming Hub features.

![CI](https://github.com/127salva/salva-opm/actions/workflows/ci.yml/badge.svg)

> **Hardware-specific.** It writes to the laptop's firmware. It was built for and tested on only one laptop (OMEN 16-ap0xxx, BIOS F.13). On any other laptop the setup refuses to install; if the identity cannot be read when the app starts, it only monitors and writes nothing. It needs no HP software or drivers (tested with them removed); a freshly installed Windows has not been tried yet. It is a personal tool, published to read, provided as is with no warranty (see [LICENSE](LICENSE)); the firmware's own thermal protections always stay in charge.

## What it does
- Three modes: Eco, Balanced and Performance. Performance is HP's "Unleashed" (80 W CPU limits, GPU at maximum power) and needs the charger.
- A quiet automatic fan curve in every mode while on the charger (on battery the firmware drives the fans, unless the thermal guard has to step in), and a custom 7-point curve in Performance.
- A thermal guard, an 85 °C rule, and battery-drain and chassis-temperature rules. When something is off, the firmware gets control back.
- Unplugging switches to Eco and plugging back in switches to Balanced (a manually chosen Eco stays Eco). Performance never survives a restart.
- One file, `SalvaOPM-Setup.exe`, installs, updates and uninstalls. The app never uses the network; the setup uses it once, to download PawnIO when it is missing or older than 2.2.0.

The full account of every firmware call and every change to the PC is in [docs/what-it-does.md](docs/what-it-does.md).

## Install
1. Download `SalvaOPM-Setup.exe` from the [latest release](https://github.com/127salva/salva-opm/releases/latest) (or build it; see below).
2. Check it: in PowerShell, `Get-FileHash .\SalvaOPM-Setup.exe` must print the SHA-256 published with the release.
3. Run it → **Install** → approve the administrator prompt.

The exe is not code-signed, so Windows SmartScreen warns about an unknown publisher ("More info" → "Run anyway"). Compare the hash first.

The setup installs the PawnIO driver (CPU temperature; downloaded once from its official release and hash-checked) and **removes OMEN Gaming Hub**. Without internet and without PawnIO it still installs; the firmware then drives the fans. Run the setup again to update or uninstall. Uninstall puts the firmware back and removes the app, and PawnIO too if this setup installed it. It does not reinstall OMEN Gaming Hub (get it from the Microsoft Store), it sets the `HPOmenCap` service to Automatic whatever it was before, and PawnIO's own uninstaller leaves its driver package in the Windows driver store; [docs/what-it-does.md](docs/what-it-does.md) lists each step.

## Build
Requirements: Windows 11, or Windows 10 1903 or later, x64. Nothing to install or download: the build uses the C# compiler that ships with Windows (.NET Framework 4.8).

```bat
build.cmd
```

| Command | Result |
|---|---|
| `build.cmd` | compiles with warnings as errors, runs the self-checks, builds `dist\SalvaOPM-Setup.exe` |
| `build.cmd selftest` | compiles and runs only the self-checks (`out\selftest.exe`) |
| `build.cmd app` | builds only the setup exe |

## Run without touching hardware
```bat
dist\SalvaOPM-Setup.exe --demo
```
This runs the whole app against a simulated firmware. It needs no administrator rights and changes no Windows setting.

Other switches: `--selftest`, `--check` (prerequisites), `--read` (every read-only value; administrator), `--dry-run [minutes]` (live readings and what the app would do; administrator).

## Repository layout
```
src/           the app (C# 5, WinForms)
res/           icon and manifest
third_party/   the PawnIO CPU module (LGPL); the PawnIO driver is downloaded by the setup, never shipped
docs/          architecture, development, testing, troubleshooting, what-it-does, user-tests, hardware/
.github/       CI workflow, Dependabot, templates
build.cmd      the build
```

## Documentation
| Document | For |
|---|---|
| [docs/what-it-does.md](docs/what-it-does.md) | everything the app touches, mode by mode |
| [docs/architecture.md](docs/architecture.md) | modules, data flow, design decisions |
| [docs/development.md](docs/development.md) | building, the demo, conventions |
| [docs/testing.md](docs/testing.md) | the self-checks and what they do not cover |
| [docs/troubleshooting.md](docs/troubleshooting.md) | warnings, logs, recovery |
| [docs/user-tests.md](docs/user-tests.md) | checks to run on the laptop after an update |
| [docs/hardware/evidence.md](docs/hardware/evidence.md) | how each firmware fact was established |
| [CLAUDE.md](CLAUDE.md) | where the project stands and how to pick it up |

## Not in this project, on purpose
No server, API, database, accounts, environment variables or containers exist, so there is no `.env.example`, API or database documentation, or Docker setup. There is no separate linter: the compiler runs with warnings as errors and `.editorconfig` sets the style.

## Credits and license
- Logic for presets, fan control, guard and OGH handling follows [Ohman](https://github.com/P4R1H/Ohman) (GPL-3.0-or-later); files with adapted code say so in their header.
- CPU temperature through [PawnIO](https://pawnio.eu) and its `AMDFamily17` module (see [third_party/README.md](third_party/README.md)).
- Firmware interface cross-checked against the Linux kernel's `hp-wmi` driver.

Copyright (C) 2026 127salva. Licensed under [GPL-3.0-or-later](LICENSE). HP and OMEN are trademarks of HP Inc.; this project is not affiliated with or endorsed by HP.
