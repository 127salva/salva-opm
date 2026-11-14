# Troubleshooting

Log: `C:\ProgramData\SalvaOPM\log.txt` (events only; the previous file is `log.txt.old`).

## The amber line in the window
| Message | Meaning | What to do |
|---|---|---|
| `Monitoring only: …` | The laptop is not an OMEN 16-ap0 with board 8E35, or its identity could not be read. Nothing is written. | Nothing: by design. |
| `could not apply … (the BIOS did not answer)` | A BIOS call failed, often right after waking. | Wait: it retries at the next 45 s heartbeat (after the wait that follows Performance, if one is running). If it stays, restart the PC. |
| `Thermal guard: fans at maximum` | CPU ≥ 95 °C, ambient ≥ 62 °C, or fans stalled while warm. | Let it cool. It releases after a minute below 85 / 54 °C. Check vents and load. |
| `Fans handed to the firmware: …` | Fan writes were refused, or the fans did not follow them. The firmware's own curve is in charge for this session. | Safe. Restart the app to try again; send the log if it repeats. |
| `No CPU temperature: the firmware drives the fans` | PawnIO is missing (e.g. the setup ran without internet), not started yet, or failing. | Restart Windows once after installing. Then run the setup → Update while online. |
| `Fan writes failing; retrying once a minute` | Temporary write failures. | Usually clears by itself. |
| `GPU power preset not accepted by the firmware` | The GPU record read back differently. | Switch mode once. Send the log if it repeats. |
| `BIOS … is not the validated F.13` | A different BIOS version. | Repeat the checks in `docs/user-tests.md`. |
| `another tool keeps changing the firmware …; switched to Balanced` | Something else overwrote Performance's limits four times. | Close other tuning tools; check that OMEN Gaming Hub is not back. |
| `Performance needs the charger connected` | Performance was chosen on battery, or with a charger below the laptop's requirement. | Plug in the original charger. |

## Common questions
**Performance is greyed out.** On battery, or the charger is weaker than the laptop requires.

**The fans behave differently for about two minutes after leaving Performance (or right after an update).** Expected: the laptop's own fan control is in charge while the old power limit lapses. When the laptop is cool the fans may stop completely; under load they speed up by themselves. The log line `host limits lapsed` marks the end. Choosing Eco or Balanced in that time is logged as `chosen while the old limit lapses` and reaches the firmware at the end.

**`Stopped after an unexpected error`, red tray icon.** Something failed that the app did not expect. It handed fans and power to the firmware and writes nothing more. Restart the app (sign out and in, or the Start-menu entry after ending it) and send `log.txt` if it repeats.

**The window does not open.** Use the tray icon or the Start-menu entry. If the app is not running, sign out and in, or run the setup → Update.

**OMEN Gaming Hub came back.** Windows or HP software reinstalled it. Run the setup → Update to remove it again.

**Keyboard lighting cannot be changed.** OMEN Gaming Hub controlled it and is gone; the keyboard keeps its last setting. Uninstall Salva OPM and install OMEN Gaming Hub from the Microsoft Store to change it.

## Reading values without changing anything
PowerShell as administrator:
```powershell
& 'C:\Program Files\Salva OPM\SalvaOPM.exe' --read | Out-Host
```

## Recovery
- **Any doubt about heat or fans:** run the setup → **Uninstall**. The firmware takes the fans and the GPU back at once; a power limit left by Performance lapses within about two minutes.
- **The app will not start or uninstall:** restart Windows. Without the app, power limits, profile and fan control lapse within about two minutes (measured). The GPU record and concurrent TDP are put back by the app at its next start, or by Uninstall. Then delete the task `SalvaOPM` in Task Scheduler, and the folders `C:\Program Files\Salva OPM` and `C:\ProgramData\SalvaOPM`.
- Safe Mode does not start the app.
