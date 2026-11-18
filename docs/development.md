# Development

## Requirements
- Windows 11, or Windows 10 1903 or later, x64 (they include .NET Framework 4.8). The compiler is `%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (part of Windows).
- No SDK, no package manager, no environment variables, no download.

## First build
```bat
build.cmd
```
Expected end of output:
```
self-check: N passed, 0 failed
built dist\SalvaOPM-Setup.exe
```

## Everyday loop
| Step | Command |
|---|---|
| Edit | any editor; `.editorconfig` sets the style |
| Check logic | `build.cmd selftest` |
| Try the app | `build.cmd app` then `dist\SalvaOPM-Setup.exe --demo` |
| Full build | `build.cmd` |

`--demo` runs the real window and engine against `SimFirmware` and `SimSensors`. It needs no administrator rights and changes no Windows setting; it keeps its state and log in `dist\demo-state`.

The exe is a GUI program that attaches to the console it was started from. In a `cmd` window its output does appear, but when typed at the prompt `cmd` does not wait for it, so the prompt can come back first and the exit code (the number of failed checks for `--selftest`) is lost. PowerShell with a pipe waits and keeps it:
```powershell
& .\dist\SalvaOPM-Setup.exe --selftest | Out-Host
```

## Language limits
The in-box compiler is C# 5. Not available: string interpolation (`$"..."`), `?.`, `nameof`, expression-bodied members, `out var`, tuples, pattern matching. The build fails on any warning.

## Where things go
| Change | Place |
|---|---|
| A new firmware call | in this order: evidence in `docs/hardware/evidence.md`, `FirmwareRules.IsAllowed`, a typed method in `HpBios`, the simulator, a self-check, `docs/what-it-does.md` |
| A rule about modes | `StateMachine.cs` + `ModeTests` |
| Fan behaviour | `FanCurve.cs` (pure) and `Engine.FanTick` + `CurveTests` / `CoreTests` |
| What install or uninstall does | `Setup.cs`, `Ogh.cs`, and the tables in `docs/what-it-does.md` |
| Window | `Ui.cs`, `Controls.cs` |
| Anything that calls the engine or the firmware | a job on `App.Hw` (`HardwareThread`); never from the window thread |
| A new warning | `Engine.Fault`, its line in `Hints` (`Ui.cs`) and the table in `docs/troubleshooting.md` |

## Running on the real laptop
Only through the setup (`dist\SalvaOPM-Setup.exe` → Install/Update): the app must run elevated from `C:\Program Files\Salva OPM`. Read-only tools on an installed copy, from an administrator PowerShell:
```powershell
& 'C:\Program Files\Salva OPM\SalvaOPM.exe' --read | Out-Host
& 'C:\Program Files\Salva OPM\SalvaOPM.exe' --dry-run 5 | Out-Host
```
Both refuse every write before it is sent.

## Releasing
1. Set `Setup.Version` (`src/Setup.cs`) and add the entry to `CHANGELOG.md`.
2. `build.cmd`; run the checks in `docs/user-tests.md` on the laptop.
3. Push to `main`; CI builds, runs the checks and prints the SHA-256 of the setup.
4. Tag `vX.Y.Z` on that commit and push the tag.
5. Download the CI artifact of that commit and publish it: `gh release create vX.Y.Z SalvaOPM-Setup.exe`, with the CHANGELOG entry, the SHA-256 and how to check it (`Get-FileHash`) in the notes. Never attach a locally built exe.
