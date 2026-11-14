# Checks to run on the laptop

The self-checks run against a simulator. These are the checks only the real laptop can give.

## Already proven on this laptop (from `C:\ProgramData\SalvaOPM\log.txt`, 2026-11-12/13, versions 2.0–2.2)
| What | Evidence |
|---|---|
| Install, PawnIO driver | installer exit 0; driver running with Memory Integrity on |
| CPU and GPU temperature | hundreds of readings, never unknown |
| Fan-level writes | accepted at every level 19–48; no hand-back, no failure |
| GPU presets | read back as `00-00-01-4B`, `00-01-01-57`, `01-01-01-57` |
| Performance power | "PL 80/80, TDP 82 verified" on every entry |
| Unplug → Eco (auto) → replug → Balanced | several times |
| Start on battery → Eco (auto) | yes |
| Shutdown | fans parked and handed to the firmware each time |
| OGH removal | package, tasks and folders gone; `HPOmenCap` disabled |
| Stale TDP after a crash | "start-up: concurrent TDP restored to 30" |

## Results so far (2026-11-13)
| Check | Version | Result |
|---|---|---|
| H2 update in Performance | 2.4.0 | **pass** 09:57: start-up lapse, `host limits lapsed … PL 0/0` 125 s after leaving Performance |
| H6 offline update | 2.4.0 | **pass** 13:23: no download; start-up lapse again ended after 125 s |
| OMEN key | 2.4.0 | pass |
| H3 repetition | 2.4.0 | **found 3 faults, fixed in 2.4.1:** a mode change during the lapse kept PL 55 (`did not lapse`); TDP 45 about 1 s after entering Performance from Eco (6 of 6 times; never from Balanced); no wake-up event under Modern Standby. 45 mode changes, ~13 standby cycles, ~11 unplug/replug otherwise fine |
| H1 FiveM FFA, Eco → Performance → Balanced | 2.4.2 | **pass** (14:32–14:51): entering Performance from Eco, TDP 45 found 9 s later by the tick re-check and rewritten; 85 °C rule engaged 14:35:00 (fans 4800 rpm), CPU held at 84–85 °C for ~12 min, released 14:46:45 under 80 °C; Balanced 14:46:55 (PL 55), `host limits lapsed … PL 0/0` 14:49:00 (CPU 79 °C, firmware fans ~2000 rpm during the lapse), then the quiet curve: CPU 75–76 °C while playing (owner's reading) |
| H4 uninstall / install | 2.4.1 → 2.4.2 | **pass on 2.4.2** (14:20–14:26): 2.4.1 left `Program Files\Salva OPM\SalvaOPM.exe` (cmd quoting, fixed in 2.4.2); 2.4.2 uninstall removed everything, Windows power mode Balanced on AC and DC, `HPOmenCap` re-enabled, PawnIO removed; reinstall downloaded PawnIO (3,410,960 bytes, exit 0, staging folder deleted), first GPU read `00-01-00-57` (the uninstall's restore worked), data folder owned by Administrators, Users read-only. PawnIO's uninstaller leaves its driver package in the Windows driver store (stopped) |
| H5 8 h in Performance, window closed | 2.4.2 | **pass** (14:56–22:56, 97 samples every 5 min, PC kept awake, no standby): working set never over 44 MB with the window closed (46.9 MB in the first sample, window just closed); handles 401–929 with no trend, threads 9–12, GDI 33–37; no warnings in the log. Private memory 53.4 → 56.8 MB (flat ~54 MB for 4 h, a 1 MB step at 18:56, then about 0.3 MB/h while idle); accepted by the owner, since it starts fresh at every Windows start. Gaming at the end: 85 °C rule 22:44:43; left Performance 22:47:41, `host limits lapsed` 22:49:48 (CPU 77 °C); Performance → Balanced → Performance in 5 s (22:54:34–39) stayed Performance with TDP 82 (H1c) |
| H7 without HP's drivers | 2.4.3 | **pass** (23:30–23:35): `HP Omen Driver` and `HP Application Driver` removed in Device Manager (they cannot be disabled; a restart brings them back), then setup → **Update**: start without a warning, start-up lapse ended after 131 s, Eco, Balanced (deferred during the lapse) and Performance (`PL 80/80, TDP 82 verified`, TDP 45 rewritten), CPU temperature read, OMEN key opened and hid the window. The BIOS interface `hpqBIntM` is served by Windows' own `WmiAcpi` driver, not by HP's |
| Uninstall from Settings > Apps, then install | 2.4.5 | **pass** 03:51–03:56 (2026-11-14): uninstalled through Settings > Apps with the final "removed" message left open for 10 s; afterwards `Program FilesSalva OPM`, `ProgramDataSalvaOPM`, the Apps entry, task, shortcut and PawnIO were gone and `HPOmenCap` was Automatic and running. Install 03:53:55: PawnIO downloaded (3,410,960 bytes, exit 0, staging folder deleted), data folder created from scratch owned by Administrators with only SYSTEM, Administrators (full) and Users (read), `installed 2.4.5`, first GPU read `00-01-00-57` (the uninstall restore worked), Performance verified at 03:56:01 |
| Update to 2.4.4 | 2.4.4 | **pass** 00:45:50 (2026-11-14): installed over 2.4.3, no warning; data folder still owned by Administrators with only SYSTEM, Administrators (full) and Users (read); start-up lapse began, CPU temperature read, Performance verified |
| Exit in Performance (2.4.3 fix) | 2.4.3 | **pass** 00:03:43–00:03:57 (2026-11-14): tray **Exit** in Performance logged `left Performance`, `GPU record -> 00-01-01-57`, `parked fans at 35`, `fans handed to the firmware's own curve`; the next start found PL 55/55 and let it lapse (`host limits lapsed … PL 0/0` 00:06:03, 126 s). Publisher in Settings > Apps reads `127salva` |
| H3b repetition of the faults | 2.4.1 | **pass** 14:10–14:18: four mode changes during the lapse deferred, `host limits lapsed` 128 s after leaving Performance, then Balanced applied; TDP 45 rewritten at once twice (a third time Performance was left within 5 s); `woke from standby (106 s)` and Performance re-applied |

**H3b (what was repeated on 2.4.1, ~10 min):** Performance → Balanced → within 2 min Eco → Balanced → wait 3 min: log shows `chosen while the old limit lapses`, then `host limits lapsed … PL 0/0`. Then Eco → Performance three times: no `drift detected`. Then standby for over a minute and wake: log shows `woke from standby`.

## Release checks for 2.4
Install first: run `SalvaOPM-Setup.exe` → **Update**. After each check, send `C:\ProgramData\SalvaOPM\log.txt`: every mode change logs temperatures, fan speeds and power limits. Never try to reach 95 °C on purpose: the thermal guard is only observed, not provoked.

| # | Check | How | Pass when |
|---|---|---|---|
| H1a | **Leaving Performance under load** | FiveM (or the CPU load below) in Performance ≥ 10 min → Balanced → keep playing 5 min | log: `host limits lapsed … PL 0/0` within 3 min of the switch; temperatures fall |
| H1b | 85 °C rule | load until the CPU sits at 85 °C for over a minute | log: `CPU above 85 C for a minute: fans to maximum`, fans 4800 rpm, then back to the curve under 80 °C |
| H1c | Quick return to Performance | Performance → Balanced → Performance within 15 s | stays Performance; no `switched to Balanced`; a `written again` line is fine |
| H2 | **Update while in Performance** | Performance → run the setup → **Update** | log after the restart: `start-up: host power limit … letting it lapse`, then `host limits lapsed … PL 0/0` within 3 min |
| H3 | Repetition (~30 min) | 20 × sleep/wake (some in Performance), 20 × unplug/replug, 30 quick mode changes | no lasting amber line; every mode follows the power rules; no `drift detected` |
| H4 | Uninstall and install (~15 min) | setup → **Uninstall**; check Settings > Apps (Salva OPM and PawnIO gone), Task Scheduler (`SalvaOPM` gone), `C:\Program Files\Salva OPM` and `C:\ProgramData\SalvaOPM` gone, Windows power mode Balanced → setup → **Install** (downloads PawnIO) → setup → **Update** | everything removed, then back and running; CPU temperature shown after the restart PawnIO may ask for |
| H5 | Long run | 8 hours in any mode with the window closed, sampling memory and handles every 5 min (the 3 days of normal use first planned were dropped by the owner) | no warnings; the memory sample stays flat |
| H6 | Install offline with PawnIO present | Wi-Fi off → setup → **Update** | installs without asking for the network |
| H7 | Without HP's drivers | Device Manager → System devices → **Uninstall device** on `HP Omen Driver` and `HP Application Driver` (do not tick removing the driver) → setup → **Update** (no restart) → Eco, Balanced, Performance 1 min, Balanced, OMEN key twice → restart (the drivers come back) | modes applied and verified, CPU temperature read, OMEN key works |
| H8 | **Fresh Windows (not done yet)** | after a clean Windows install, before installing anything from HP: run the setup → **Install** with internet, then `--check` and H1c | installs (PawnIO downloaded), modes applied and verified. Only Windows' own .NET Framework 4.8 is needed (part of Windows 10 1903+ and 11) |
| — | OMEN key | press twice | window opens, then hides |

**CPU load without downloading anything** (normal PowerShell). Start:
```powershell
1..12 | ForEach-Object { Start-Job { while ($true) { } } } | Out-Null
```
Stop:
```powershell
Get-Job | Stop-Job; Get-Job | Remove-Job
```

**Read-only check of every value** (PowerShell as administrator):
```powershell
& 'C:\Program Files\Salva OPM\SalvaOPM.exe' --read | Out-Host
```

## Never observed so far
Thermal guard engaging, the battery-drain rule, the chassis 54 °C rule, the fail safe, behaviour after a BIOS update. Full list: the feature table in `docs/testing.md`.

## After a BIOS update
The window shows "BIOS … is not the validated F.13". Repeat H1 and H3.
