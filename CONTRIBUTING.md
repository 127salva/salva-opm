# Contributing

## Ground rules
1. **No unverified firmware writes.** A new BIOS call, value or range needs evidence: where it is documented or how it was measured on the board. Record it in `docs/hardware/evidence.md`. The full list of places a new call touches is in `docs/development.md`, "Where things go".
2. **Every write is range-checked and read back.** Out-of-range values throw; nothing is clamped silently. A `0` return code alone proves nothing.
3. **Fail towards the firmware.** When a reading is missing or a write fails, the firmware gets the fans back.
4. **Keep it small.** No new dependency, service, network call or setting without a clear need.

## Workflow
1. Branch from `main`.
2. Make the change with its self-check (`src/SelfCheck.cs`).
3. Run `build.cmd`. It must end with `0 failed` and `built dist\SalvaOPM-Setup.exe`.
4. Try it: `dist\SalvaOPM-Setup.exe --demo`.
5. Update `CHANGELOG.md` and, when behaviour changed, `docs/what-it-does.md`.
6. Open a pull request using the template. CI must pass.

## Code conventions
- **C# 5 only** (the in-box compiler): no string interpolation, `?.`, `nameof`, expression-bodied members or `out var`.
- References are limited to `System`, `System.Windows.Forms`, `System.Drawing`, `System.Management`.
- Pure logic (`StateMachine`, `Governor`, `FanCurve`) does no I/O, so it can be checked without hardware.
- All BIOS packets are built in `HpBios` (`src/Firmware.cs`) and must pass `FirmwareRules`.
- Comments say why, not what. Log events, not routine ticks.
- Names: `PascalCase` for types and members, `_camelCase` for private fields.

## Commits and versions
- Short imperative subject; explain the why in the body when it is not obvious.
- Versions follow semantic versioning (`Setup.Version`): major for a redesign, minor for behaviour changes, patch for fixes. Releases are tagged `vX.Y.Z`.

## Testing on the laptop
Changes that affect what is sent to the firmware should be run on the hardware before release; `docs/user-tests.md` lists the checks. Say in the pull request what was run.
