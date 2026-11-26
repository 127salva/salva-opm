# Troubleshooting

Log: `C:\ProgramData\SalvaOPM\log.txt` (events only; the previous file is `log.txt.old`).

## The warning in the window's status pill
Hover the pill (the "i") to see what to do; it is the last column below.
| Message | Meaning | What to do |
|---|---|---|
| `Performance needs the charger` | Performance was chosen on battery, or with a charger below the laptop's requirement. | Use the original charger, or one strong enough for this laptop. |
| `Eco not applied yet` (or Balanced, Performance) | A BIOS call failed, often right after waking (log: `the BIOS did not answer`, once however long it lasts). It is tried again at the next 45 s heartbeat (after the wait that follows Performance if one is running). | The laptop didn't answer; it tries again within a minute. If this stays, restart the PC. |
| `Performance didn't start` | The power data could not be read, a power write failed, or the values did not read back as PL 80/80 and TDP 82 (the log line `Performance did not start:` says which). | Back to Balanced. Try Performance again. If it keeps happening, close other tuning apps and send log.txt. |
| `Couldn't check the power` | In Performance, the heartbeat could not read the power data. Performance keeps running and the heartbeat keeps trying; the line stays until the next mode change or wake from sleep. | Performance keeps running. Switch mode once; send log.txt if it repeats. |
| `Another app took over` | Something else overwrote Performance's limits four times (log: `another tool keeps changing the firmware`). | Back to Balanced. Close other tuning apps. If OMEN Gaming Hub came back, run the setup → Update. |
| `GPU setting not accepted` | The GPU record read back differently. | Switch mode once. Send log.txt if it repeats. |
| `New BIOS, not tested yet` (or `Unknown BIOS, not tested yet`) | A BIOS other than the validated F.13 (the log line `identity:` names it), or one whose version could not be read. | Salva OPM was tested on BIOS F.13 only. If a mode misbehaves, choose Balanced or uninstall. |
| `Stopped after an error` (red) | Something failed that the app did not expect (the fail safe). | The laptop's own settings are in charge. Exit from the tray, open Salva OPM from the Start menu, and send log.txt if it happens again. |

## Common questions
**Performance is greyed out.** On battery, or the charger is weaker than the laptop requires.

**A mode chosen right after leaving Performance (or right after an update) takes about two minutes to reach the firmware.** Expected: the app sends nothing while the old power limit lapses, because any write would keep it alive. The Windows power mode changes at once; the log line `chosen while the old limit lapses` marks the choice and `host limits lapsed` the end.

**The CPU temperature shows `--`.** PawnIO is missing (e.g. the setup ran without internet), not started yet, or failing. Restart Windows once after installing, then run the setup → Update while online. The modes work without it.

**The fans are loud, or the CPU runs near 95 °C in Performance.** The fans always run on the laptop's own curve; the app does not change them. That is the firmware's choice under heavy load; Balanced or Eco runs cooler.

**Salva OPM says it only runs on the OMEN 16-ap0 and closes.** The model or board did not match, or the identity could not be read at start-up. Nothing was written. On the right laptop, sign out and in; if it repeats, send `log.txt` (line `refused to run`).

**`Stopped after an error`, red tray icon.** Something failed that the app did not expect. It put profile `0x30` back and writes nothing more to the firmware. Exit from the tray, open Salva OPM from the Start menu, and send `log.txt` if it happens again.

**The window closed by itself, with a tray message.** The window hit an error (logged as `window error:`). The app and its power modes keep running; open the window again from the tray. Send `log.txt` if it repeats.

**The window does not open.** Use the tray icon or the Start-menu entry. If the app is not running, sign out and in, or run the setup → Update.

**Salva OPM is gone from the tray after a sign-out or shutdown you cancelled.** Windows ends the apps one at a time; the ones it reached before you cancelled stay closed. The log shows `exit: firmware back to profile 0x30` for that moment. Start it again from the Start menu.

**OMEN Gaming Hub came back.** Windows or HP software reinstalled it. Run the setup → Update to remove it again.

**Keyboard lighting cannot be changed.** OMEN Gaming Hub controlled it and is gone; the keyboard keeps its last setting. Uninstall Salva OPM and install OMEN Gaming Hub from the Microsoft Store to change it.

## Reading values without changing anything
PowerShell as administrator:
```powershell
& 'C:\Program Files\Salva OPM\SalvaOPM.exe' --read | Out-Host
```

## Recovery
- **Any doubt about power or heat:** choose Balanced, or run the setup → **Uninstall**. Uninstall puts the GPU record back at once; a power limit left by Performance lapses within about two minutes. The fans are the firmware's in every case.
- **The app will not start or uninstall:** restart Windows. Without the app, power limits and the profile lapse within about two minutes (measured). The GPU record and concurrent TDP are put back by the app at its next start, or by Uninstall. Then delete the task `SalvaOPM` in Task Scheduler, and the folders `C:\Program Files\Salva OPM` and `C:\ProgramData\SalvaOPM`.
- Safe Mode does not start the app.
