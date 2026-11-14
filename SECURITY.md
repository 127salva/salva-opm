# Security

## Reporting a problem
Use GitHub's private reporting on this repository: **Security → Report a vulnerability**. Please do not open a public issue for something that could harm a machine. Include the version, what you observed and, if you can, `C:\ProgramData\SalvaOPM\log.txt`.

## What the app does with its privileges
The app runs elevated (a logon task with highest privileges) because the HP BIOS interface and the PawnIO driver require it.

| Area | Practice |
|---|---|
| Firmware calls | Only the pairs in `FirmwareRules` (`src/Firmware.cs`) can be sent; a self-check proves nothing else passes. Values are range-checked; power and GPU writes are read back, and fan writes are checked against the measured fan speeds. |
| Wrong hardware | Anything but an OMEN 16-ap0 with board `8E35`: the setup refuses to install. If the identity does not match when the app starts (or cannot be read): monitoring only, no writes. |
| Kernel driver | PawnIO's official signed build. The app loads one signed module and calls one read function (`ioctl_read_smn`). No port, MSR or EC access. |
| PawnIO installer | Not shipped. The setup downloads the pinned release over HTTPS into memory, checks its SHA-256 (`Setup.PawnIoSha256`), writes it to the administrators-only data folder and runs it from there, so no user-level process can swap it after the check. |
| Elevated setup | Once its own code runs, DLLs load from System32 only (`SetDefaultDllDirectories`; the .NET runtime itself loads before that, see Known limits); helper programs are started by full path; nothing elevated is written to the user's TEMP; the install stops if the data folder cannot be made administrators-only. |
| Settings | `C:\ProgramData\SalvaOPM` is writable by administrators and SYSTEM only. Every value is validated on load. |
| Native libraries | `nvml.dll` is loaded by full path from `System32`, never from the app folder. |
| Network | The app: none. The setup: one HTTPS download of the pinned PawnIO release, only when PawnIO is missing or older than 2.2.0. No update check, no telemetry. |
| Single instance | One copy per PC (`Global\SalvaOPM`), so two Windows sessions never write to the firmware at once. |
| Unexpected errors | Fail safe: fans and power go back to the firmware and nothing more is written until the app restarts. |
| Other processes | Ends OMEN Gaming Hub's background processes by name; removes its Store package, tasks and helper folders at install. |
| Secrets | The project has none: no keys, tokens or accounts. |

## Known limits
- The setup and the app are **not code-signed**. Windows SmartScreen may warn; compare the file's SHA-256 (`Get-FileHash`) with the one published with the release.
- The setup run from Downloads loads the .NET runtime before its own code can restrict DLL loading; keep that folder free of files you do not trust.
- The window runs elevated together with the engine (no separate unprivileged UI).
- Firmware behaviour was verified on BIOS `F.13` only. After a BIOS update the app warns, and the hardware checks should be repeated.
- Removing OMEN Gaming Hub is not reversible offline.
