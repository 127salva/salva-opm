# Changelog

Versions are major.minor.patch; `CONTRIBUTING.md` says how each number was used. Dates are when the build was made.

## 2.5.3 - 2026-11-19
### Fixed
- Mode cards and fan switch: after a mouse click hid the focus ring, Enter or Space could apply the item the arrow keys had been on before (out of sight since 2.5.2). A click now makes them forget it.
- Curve editor: after dragging a point with the mouse, Up/Down moved the point the keyboard had chosen earlier. They now go on from the point just dragged.
### Removed
- An unused property (`Engine.OnAc`).
### Tests
- The CPU temperature formula (`SystemSensors.Tctl`) has a self-check; before, only the laptop ran it.

## 2.5.2 - 2026-11-19
### Fixed
- Curve editor: moving a point with Up/Down and pressing Tab before releasing the key lost the move (the key release went to the next control). The move is now saved when the curve loses the focus.
- The keyboard focus ring (the white ring after Tab) stayed until the window closed. A mouse click anywhere in the window now hides it; Tab or an arrow key shows it again.

## 2.5.1 - 2026-11-18
### Changed
- The tray menu no longer changes modes: right-click gives Open and Exit; modes are chosen in the window.
- The tray menu is a small dark menu in the window's colours (no empty icon column, rounded corners on Windows 11), instead of the light grey Windows menu. A right-click on the tray icon while the menu is open closes it.

## 2.5.0 - 2026-11-18
### Added
- Fan choice **Firmware** in every mode, remembered per mode: the firmware's own curve drives the fans, with no 85 °C rule and no thermal guard from the app. The choices are Firmware | Recommended (the quiet Auto curve, renamed) | Custom (Performance only).
### Changed
- New window, larger and fixed in size: a header with the version and a status pill (green "All good", the warning, or "Starting…" at start-up); the modes as cards with icons; a panel per chip with its temperature, its fan speed as a ring and a graph of the last 5 minutes; the fan switch and curve restyled. The amber warning line and the bottom info line are gone (the pill and the panels show them).
- The pill shows an "i" with a warning; its tooltip says what to do.
- Hovering a graph shows the temperature and age of any point; a graph that is still filling up spreads over the full width; an empty one reads "Collecting…".
- Clicks show a pressed look at once; the fan switch slides and the chosen card fades in (not for keyboard choices; off when Windows animations are off); the rpm ring glides to each reading; one corner-radius scale.
- The cards, the fan switch and the custom curve work with the keyboard (Tab, arrows, Enter).
- The open window reads the fan speeds every 2 s (a read; it keeps no host state alive).
- All firmware work runs on its own thread: the window never freezes, a click shows at once, and fast clicks apply only the last choice (the 1 s guard between mode changes is gone).
### Fixed
- The setup offered Update and Uninstall in the seconds right after an uninstall, and an install started then failed; it now shows Install and waits for the old program folder to be removed.

Otherwise the same behaviour and the same firmware calls.
## 2.4.8 - 2026-11-18
### Fixed
- A mode chosen from the tray before start-up finished (the laptop's identity still unread, e.g. WMI not ready at logon) was written to the firmware. Mode choices made before start-up is done are now ignored, with a tray message to try again.
- A concurrent TDP outside 20..82 read on entering Performance was kept as the baseline; leaving Performance then failed and stopped the app (fail safe). Such a value is no longer kept, and the log says so.
- `--check` said that without PawnIO the fans use a fixed safe level; the firmware drives them.
- `--dry-run abc` (or 0) ran for no time at all; a bad value now keeps the default 5 minutes.
- `--dry-run` ran the thermal guard every 5 s; it now uses the app's 10 s, so it shows the guard when the app would engage it.
### Removed
- Unused members (`Engine.CurrentPl`, `Engine.GpuRecord`, `PowerData.Telemetry`, `Settings.FilePath`, `ModeController.PowerOk`, `NullPowerMode.Calls`) and the GPU read they needed in monitoring-only mode; the uninstall question is kept in one place.
### Docs
- Comments and docs brought in line with the evidence (dState echo, fan bounds, what refreshes host state, what survives a crash, files in the data folder, the chassis release at 49 °C or below).

## 2.4.7 - 2026-11-14
### Changed
- The 85 °C rule is back, as in 2.4.5 (the owner's choice after trying 2.4.6): CPU at 85 °C or more for a minute puts both fans at maximum until it is under 80 °C. The thermal guard at 95 °C is unchanged.

## 2.4.6 - 2026-11-14
### Removed
- The 85 °C rule (fans to maximum after a minute at 85 °C). 85 °C is a normal working temperature for this CPU, and on the laptop the rule made the fans louder while the CPU stayed at 84–85 °C. The thermal guard still puts both fans at maximum from a CPU of 95 °C (two readings, 10–20 s) until it has been under 85 °C for a minute, in every curve; below that the curve in use decides (Auto reaches 4800 rpm at 90 °C).

## 2.4.5 - 2026-11-14
Fixes from a second whole-repository review.
### Fixed
- Uninstall from Settings > Apps could leave `C:\Program Files\Salva OPM\SalvaOPM.exe` behind: the folder was deleted about 3 s after the last step, while the "removed" message (shown by that same exe) was still open. The folder is now deleted after the message is closed, with a 5 s margin.
- The setup checked only the board before installing; the app also needs the model. Another HP model with board 8E35 could install, lose OMEN Gaming Hub and get an app that only monitors. The setup now uses the same rule as the app.
### Removed
- A link check in the PawnIO step that could never trigger (the folder above it is administrators-only).
### Docs
- Uninstall sets `HPOmenCap` to Automatic and starts it; it does not remember how it was before.

## 2.4.4 - 2026-11-14
Fixes from a whole-repository review.
### Fixed
- A thermal guard that engaged in the two minutes after leaving Performance refreshed the old 55 W limit with its fan writes; the app then gave up waiting ("did not lapse") and held the limit until restart. The wait now pauses while the guard is on and starts again when it ends.
- The CPU temperature is tried again once a minute when the PawnIO driver was not ready at logon (before: firmware fans for the whole session).
- An idle GPU is read at once when it wakes up or the CPU heats up quickly, instead of up to 30 s later.
### Security
- The data folder is created with its permissions in one step and checked afterwards; a folder or link planted in between is refused and gets no new permissions.
### Removed
- Clean-up of version 1 ("OmenModes", never published) and of a result file left in TEMP by setups before 2.4.
### Docs
- On another laptop the setup refuses to install (the README said the app would only monitor).

## 2.4.3 - 2026-11-13
### Added
- The version is shown at the bottom right of the window, opposite the temperatures and fan speeds.
### Changed
- Settings > Apps shows the publisher as "127salva", as in the copyright.
### Fixed
- Exiting from the tray in Performance left the GPU record at Max until the app started again (it does not lapse by itself). Exit now puts it back to Boost, like the fail safe already did.

## 2.4.2 - 2026-11-13
### Fixed
- Uninstall left `C:\Program Files\Salva OPM\SalvaOPM.exe` behind: the full-path `cmd /c` line added in 2.4.0 lost its quotes (cmd strips the first and last quote). Found in the uninstall test (H4).
- Uninstall also deletes the result file older setups left in the user's TEMP.

## 2.4.1 - 2026-11-13
Fixes from the repetition test on the laptop (H3).
### Fixed
- Changing between Eco and Balanced in the two minutes after leaving Performance re-sent the profile, which kept the 55 W limit alive ("host limits did not lapse"); the app then held it for good. Such a change now applies the Windows power mode at once and reaches the firmware when the old limit is gone.
- Entering Performance from Eco: the firmware sets concurrent TDP to 45 about a second after the GPU record changes. TDP is now checked again on the next two 5 s ticks, instead of waiting up to 45 s for the heartbeat.
- Modern Standby gives the app no resume event: a gap of more than 30 s between ticks is now treated as a wake-up and everything is applied again.

## 2.4.0 - 2026-11-13
Fixes from the pre-release audit.
### Fixed
- A power limit left by a session that ended moments before (every update, a crash) was held for good by the fan keep-alive. It is now left to lapse at start, as after leaving Performance.
- Concurrent TDP is checked again after each GPU write in Performance and written again if the firmware changed it (seen once: 82 → 45).
- After waking while the thermal guard is on, the fans go to maximum at once instead of up to 10 s later.
- WMI queries (BIOS interface lookup, laptop identity) can no longer hang the window: they give up after 10 s.
### Security
- PawnIO is no longer shipped inside the setup: it is downloaded once from its official release, SHA-256 checked, and run from the administrators-only folder (no swap possible after the check). Uninstall removes it with PawnIO's own uninstaller.
- The elevated setup loads DLLs from System32 only, starts `cmd.exe` and `ping.exe` by full path, and no longer writes into the user's TEMP.
- The data folder is re-created when it was planted before install (a link, or not owned by administrators).
- `--read` and `--dry-run` also refuse type 16 (it arms the firmware's host fan control).
### Added
- Fail safe: an unexpected error hands fans and power to the firmware, stops all writes until restart and shows a red tray icon.
- One copy per PC (machine-wide lock), even with several Windows users signed in.
- Uninstall restores the firmware only on the validated laptop.
- Self-check: 14 simulated days with random load, unplugging, sleep, mode changes and failing BIOS calls.
### Changed
- SPDX license and copyright headers in every source file; Ohman-derived files say so.
- `--selftest` keeps its scratch files in TEMP, so it also runs from Program Files.

## 2.3.0 - 2026-11-13
### Changed
- Auto fan curve replaced by a quiet one (1900 rpm up to 60 °C, 4800 rpm at 90 °C) in all modes.
- After leaving Performance the app sends nothing to the firmware for about two minutes, so the 55 W limit lapses instead of being held (the laptop stayed hot with loud fans before).
- Uninstall also resets the Windows power mode to Balanced and removes PawnIO when this setup installed it.
### Added
- 85 °C rule: CPU at 85 °C or more for a minute pushes the fans to maximum until it is under 80 °C.
- One log line of readings (temperatures, fans, power limits) at every mode change.
- Periodic cleanup so the handle count stays flat.

## 2.2.0 - 2026-11-12
### Changed
- Fan switch: Auto only in Eco and Balanced; Auto | Custom in Performance. Custom curve is Performance-only.
- Tray icon colour shows the mode; tooltip shows temperatures and fan speeds.
### Added
- Custom curve lower bound from OGH: from 75 °C up never below 2900 rpm.
- A failed apply (BIOS not answering, e.g. after sleep) is retried at the next heartbeat.
### Fixed
- Routine fan steps no longer flood the log. Fan watch triggers when either fan lags.

## 2.1.0 - 2026-11-12
### Changed
- One-page window. Graphics switch, settings page, floor and ramp controls removed.
- Custom curve: 7 points at 60–90 °C, each fan following its own chip.
- Install removes OMEN Gaming Hub completely; uninstall is done from the setup.
### Added
- Monitoring-only mode on any other laptop; BIOS version warning; firmware fans when the CPU temperature is unknown; settings folder writable by administrators only.

## 2.0.0 - 2026-11-12
- Rebuilt on Ohman's logic as "Salva OPM": software fan curve, GPU presets, thermal guard, PawnIO CPU temperature, OGH take-over, single-file setup.

## 1.0.0 - 2026-11-11
- "OmenModes": three-button tray app (Eco, Balanced, Unleashed) with the battery-drain and chassis-temperature rules.
