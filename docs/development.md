# Development

## Requirements
- Windows 11, or Windows 10 1903 or later, x64 (they include .NET Framework 4.8). The compiler is `%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (part of Windows).
- No SDK, no package manager, no environment variables, no download.

## Set up on a new PC
Building needs nothing beyond Windows. Committing and releasing also need:
1. Git, and the GitHub CLI signed in to the owner's account: `gh auth login`.
2. Clone: `git clone https://github.com/127salva/salva-opm.git`.
3. Commit identity, in the clone: `git config user.name 127salva`, and `git config user.email` set to the GitHub no-reply address that the existing commits use (`git log -1 --format=%ae`).
4. Optional, to compare with Ohman: clone <https://github.com/P4R1H/Ohman> into `ref/` (ignored by git) and check out the commit named in `docs/hardware/evidence.md` section 11.
5. `build.cmd` (below).

Claude Code reads `CLAUDE.md` and the shared permissions in `.claude/settings.json`. Personal permissions go in `.claude/settings.local.json`, which git ignores.

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

## Files
CRLF line ends and UTF-8 without BOM (`.editorconfig`, `.gitattributes`). Two traps when editing from a script: PowerShell's `[IO.File]::WriteAllText` with `[Text.Encoding]::UTF8` writes a BOM (use `New-Object Text.UTF8Encoding $false`), and perl replacement strings eat the backslashes of Windows paths (check docs after a perl edit).

## Releasing
Pushing, tagging and publishing happen only with the owner's OK.
1. Set `Setup.Version` (`src/Setup.cs`) and add the entry to `CHANGELOG.md`. Update the version lines at the top of `CLAUDE.md`, `docs/testing.md` and `docs/what-it-does.md`. When the window changed, retake `docs/images/window.png` from `--demo`.
2. `build.cmd`; run the checks in `docs/user-tests.md` on the laptop.
3. Commit and push to `main`. The owner's push bypasses the branch protection, so GitHub prints "Required status check ... is expected": that is normal. CI builds, runs the checks and prints the SHA-256 of the setup.
4. Wait for the run (`gh run watch <id> --exit-status`), download its artifact (`gh run download <id>`), and check that `Get-FileHash` of the downloaded exe matches the hash in the CI log.
5. Tag that commit and push the tag: `git tag -a vX.Y.Z -m "Salva OPM X.Y.Z" <commit>`, `git push origin vX.Y.Z`.
6. Publish the CI exe, never a locally built one: `gh release create vX.Y.Z <path>\SalvaOPM-Setup.exe --title "Salva OPM X.Y.Z" --notes-file notes.md --latest`. Then check that `gh release view vX.Y.Z --json assets` shows the same SHA-256 as its `digest`.

Release notes (`notes.md`), with the hash and the CHANGELOG entry filled in:
````markdown
Salva OPM replaces OMEN Gaming Hub's performance control: Eco, Balanced and Performance, a quiet fan curve (custom curve in Performance), and the firmware's own safety in charge.

> **Only for the HP OMEN 16-ap0 with board 8E35** (tested on BIOS F.13). On any other laptop the setup refuses to install. It writes to the laptop's firmware; it is a personal tool, provided as is, with no warranty. Read [what it does](https://github.com/127salva/salva-opm/blob/main/docs/what-it-does.md) first.

## Install
1. Download `SalvaOPM-Setup.exe` below.
2. Check it in PowerShell:
   ```powershell
   (Get-FileHash .\SalvaOPM-Setup.exe -Algorithm SHA256).Hash
   ```
   It must print:
   ```
   <SHA-256 from the CI log>
   ```
3. Run it → **Install** → approve the administrator prompt. The exe is not code-signed, so SmartScreen warns about an unknown publisher: compare the hash first, then "More info" → "Run anyway".

The setup downloads the PawnIO driver once (for the CPU temperature, hash-checked) and removes OMEN Gaming Hub. Run it again to update or uninstall; uninstall puts the firmware back.

This exe was built by the repository's CI from the tagged commit.

### X.Y.Z - YYYY-MM-DD
<the CHANGELOG entry>
Earlier changes: [CHANGELOG.md](https://github.com/127salva/salva-opm/blob/main/CHANGELOG.md).
````
