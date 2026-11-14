## What and why

## Firmware impact
- [ ] No change to what is sent to the BIOS
- [ ] Changes a BIOS call, range or timing (describe; update `docs/what-it-does.md`, `docs/hardware/evidence.md`, the allow-list, `SimFirmware` and their self-checks)

## Checks
- [ ] `build.cmd` passes (warnings as errors, all self-checks)
- [ ] New logic has a self-check in `src/SelfCheck.cs`
- [ ] Tested on the laptop (say what was run), or simulator only (say so)
- [ ] `CHANGELOG.md` updated
