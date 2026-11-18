# Evidence: what OGH does on this laptop (HP OMEN 16-ap0xxx, board 8E35, BIOS F.13)

Status of this file: **the record of the investigation, oldest first.** Sections 0–8 are the read-only and write-test phase of 2026-11-11 (with OGH still installed), 10–14 the app on the laptop. Where an early section and a later one disagree, the later one wins; "your spec", "the user" and "you" mean the owner. The probe logs (`docs/*.txt`) and scripts (`tools/*.ps1`) cited below were machine-specific and are not in the repository: every value they produced is quoted here. Test names in sections 10–13 (H1, H2 …, "check 3") follow an older numbering, and `app/`, `docs/USER-TESTS.md` and `docs/WHAT-IT-DOES.md` are the layout of version 1; today's checks are in `docs/user-tests.md`. Sections 8–8b record the decisions of the first version (custom curve dropped, fans by BIOS on AC, a 30 s type-16 heartbeat, PL 55 in Eco and Balanced, no restore on exit): all were replaced, see sections 11–14 and `docs/what-it-does.md`.

**Legend**
- **D** = decoded statically from OGH's own .NET code or its embedded platform JSON (reflection-only, nothing executed).
- **L** = read from OGH's own logs / cache on this PC.
- **H** = confirmed on real hardware. Section 0 lists the results so far (all reads; no write has been tested).
- **U** = unknown / not decoded yet. Anything U stays out of the app (spec: "never send guessed commands").

Sources: OGH 1101.2611.2.0 assemblies `HP.Omen.Background.PerformanceControl`, `HP.Omen.Core.Model.Device` (`PerformanceControlHelper`), `HP.Omen.Core.Common` (`OmenHsaClient`, embedded `Khalilah_DRG_N22X4X6.json`), `PerformanceControl` (HP Bridge SDK); OGH logs under `%LOCALAPPDATA%\Packages\AD2F1837.OMENCommandCenter_*\LocalCache\Local\HPOMEN`. OGH calls this laptop's platform Khalilah / DRG (log: `8E35::ThisSystemID`, `Postfix = Khalilah_DRG_N22X4X6`).

---

## 0. Hardware results (H): read-only probe, 2026-11-11 19:08 (`docs/probe-output.txt`)

| Check | Result | Meaning |
|---|---|---|
| Transport | All 9 calls through `hpqBIntM` instance `ACPI\PNP0C14\0_0` (admin, `Sign="SECU"`, command `0x20008`) returned `rc=0`. | Plain WMI works; **no HP driver call from our side**. Instance is hosted by the in-box ACPI-WMI mapper `ACPI\PNP0C14\0`. Whether HP's installed drivers/services are still required is untested (fresh-Windows test). |
| Type 40 SystemDesignData | `E6 00 2D 01 0B 00 01 07 2D 00 ...` | Identical to OGH's cached copy. Byte[3] = 1 -> **thermal policy V1 confirmed on hardware.** |
| Type 44 fan types | `21 00 00 00 ...` | Low nibble 1 = Cpu, high nibble 2 = Gpu (FanType enum). |
| Type 16 | `02 00 00 00` | byte[0] = 2 matches 2 fans; byte[1] (OGH's protection flags) = 0: no OCP/OTP active. |
| Fan count | **2 fans: CPU + GPU.** | Answers the spec question; interface has separate CPU and GPU levels. |
| Type 45 current fan levels | `15 13` = CPU 21, GPU 19 (raw) | Inside the 19..48 range; fan levels are readable, so **fan writes can be verified by read-back.** |
| Type 47 fan table (first 32 B) | `02 0C`, then triplets rising `13 10 16`, `15 13 19` ... up to `2C 2E 30` | Firmware's own level table; top value `0x30` = 48 matches the max raw level. Column meaning U. |
| Type 35 sensors idx 0..3 | all `24` = 36 C | Same value for every index; no CPU data. U whether shared sensor or index ignored. |
| ACPI zones (Windows counters) | `_TZ.THRM` 70.9 C, `_TZ.TZ01` 74.9 C (THRM was 57.9 C earlier today) | Both move; **not yet matched to the CPU temperature OGH uses.** |

**Write test, step 1 (19:41, `docs/write-log.txt`): PASS.** Type 42 read: `43 00 00 37 37 00 00 1E 00 ...` -> PL4 = 0, **concurrent TDP = 30 W (0x1E)**. Wrote the same value with type 41 `FF FF FF 1E` through `hpqBIOSInt4`: `rc=0`, read-back unchanged (PL4 0, TDP 30). **First hardware-confirmed write: the write transport works and the `0xFF` "unchanged" bytes are respected.** Observations: (a) the current concurrent TDP is **30 W, not the 45 W** that SystemDesignData byte[8] suggested, so the restore value must always be the one read before the first write; (b) bytes [3] and [4] of the type 42 reply are both `0x37` = 55, which may be the current PL1/PL2 (OGH's Balanced default). **Unverified hypothesis**; if true, PL1/PL2 would be readable and step 3 can verify them.

**Write test, step 2 (19:46 and again 19:49, identical): the firmware did NOT apply concurrent TDP 82.** `FF FF FF 52` returned `rc=0`, but the read-back stayed 30 both immediately and after 35 s (not clamped, not changed); the restore to 30 succeeded both times. So **`rc=0` does not prove a write took effect, and read-back is mandatory.** Candidate causes, untested: (a) 82 is above the platform TPP maximum 72 and is silently ignored; (b) the firmware only applies concurrent TDP while the thermal profile is a performance/Unleashed one (OGH only writes it in performance mode). Step 2b (value 55) is designed to tell (a) from (b). Also: type 42 byte[0] read `0x43`, `0x40`, `0x3F` (67, 64, 63) across the three reads while the PC idled, i.e. live telemetry of unknown meaning (possibly a temperature); **unverified, not relied on**.

**Write test, step 2b (19:53): 55 W was also NOT applied** (`rc=0`, read-back 30 immediately and after 35 s, restore OK; the first attempt was cancelled at the prompt, nothing sent). **So the value is not the cause: with the thermal profile as it is now, the firmware ignores concurrent-TDP writes.** Leading explanation: it only applies concurrent TDP under a performance/Unleashed profile, which is also the only time OGH writes it (`IsPerformanceMode`: mode 4 or any odd mode value). Type 42 byte[0] read `0x35`, `0x3B` (53, 59) seconds apart: fast-moving telemetry, meaning still unknown.

**Write test, step 4 (19:58, OGH background stopped since 19:56:55): PASS.** Thermal profile `[FF,30,01,00]` (type 26): `rc=0`. Fan levels (type 45) **CPU 19 / GPU 16 before -> CPU 23 / GPU 21 within 5 s, then steady for 30 s.** This is the first **observable effect of a mode write** and doubles as an indirect read-back: the firmware took over fan control and holds its own idle levels (23/21, above what OGH's software loop had left, 19/16). Concurrent TDP stayed 30 and type 42 bytes 3-4 stayed 55/55 (expected: not a performance profile). The profile byte itself still has no direct read-back; the **fan levels are the witness**. Laptop left in "Balanced + firmware fan control" with OGH stopped.

**Write test, step 5 (20:01): PASS, and it answers the SPG question.** Under thermal profile `[FF,04,01,00]` (Unleashed; `rc=0`; fan levels unchanged 23/21 at idle) concurrent TDP **55 read back 55 (APPLIED)** and **82 read back 82 (APPLIED)**. Under Balanced the same writes were ignored (steps 2/2b). **Mode gating confirmed: concurrent TDP is applied only under the Unleashed profile, and the firmware accepts 82 W (your SPG 27 W).** The automatic restore returned TDP to 30 and the profile to Balanced (fans 23/21). **New observation:** type 42 bytes 3-4 read `0x37 0x37` (55/55) while OGH was running and still at 19:58:41, but **`0x00 0x00` at 20:01:00 with no write from us in between** (OGH stopped 19:56:55). Hypothesis, untested: these are host-set power limits that the firmware drops when the host stops refreshing them (OGH's 30 s heartbeat), which would mean **the app needs a periodic refresh** while a mode is active. Step 6 tests it.

**Write test, step 6 (20:06-20:12, OGH stopped): PL1/PL2 are readable, and they expire.** Sequence, all `rc=0`: profile `[FF,04,01,00]`, PL1/PL2 `50 50 FF FF` (80/80), concurrent TDP 82. (1) **Type 42 bytes 3 and 4 read back 80 and 80: PL1/PL2 ARE readable** (the "no PL1/PL2 read-back" gap is closed; which of the two bytes is PL1 vs PL2 is not distinguished yet because both were equal). (2) **Host-set PL1/PL2 expire:** still 80/80 at 106 s after the write, `0/0` at 117 s, and 0/0 until the end (330 s). (3) **Concurrent TDP did not expire:** 82 for the full 330 s. (4) Fan levels stayed 21/19 at idle; type 42 byte[0] kept drifting 52-59 (unknown telemetry). The restore wrote TDP 30 first (while still Unleashed), skipped PL1/PL2 (already 0/0), then Balanced. Final: TDP 30, PL unset, profile Balanced, fans 21/19. **Consequence: Unleashed needs a refresh of PL1/PL2 at least every ~100 s; OGH's own interval is 30 s (`IntervalHeartbeat 30000`).** The refresh can be "read type 42, write only if bytes 3-4 differ", which also detects overrides.

**Write test, step 7, first attempt (20:24-20:40): inconclusive.** Profile `0x04` written; 8 min of reads only (fans 21/19, nothing else changed); the final concurrent-TDP prompt then sat unanswered for 7 min 47 s and was cancelled, so the gating test never ran. Restore OK (TDP 30, Balanced). The Unleashed profile was active ~16 min without a refresh, which is consistent with persistence but proves nothing. The script now sends that last write automatically after 10 min so the test can run unattended.

**Write test, step 7, second attempt (20:45-20:55): NOT APPLIED -> the Unleashed profile also expires without a refresh.** Profile `0x04` written, 10 min of reads only, then the automatic concurrent-TDP 55 write returned `rc=0` but read back 30: the firmware was no longer in the Unleashed profile (gating, steps 2/2b/5). Restore OK. (Step 6 cannot tell when the profile dropped: its TDP *value* stayed 82, which is only a stored number.) **So the profile, PL1/PL2 all need a refresh; Balanced/Eco may not** (the firmware's default profile is probably the same `0x30`), still unproven.

**OGH's heartbeat decoded (D):** `PowerControlBase.RunHeartbeatLoop` / `OnTimedEventHearbeat` / `StartHearbeatTimer` call **`GetNumOfFan()` = BIOS type 16 (a read)** every `IntervalHeartbeat` = 30 000 ms; there is no separate heartbeat write. **Hypothesis (step 8):** the firmware treats this periodic type-16 read as "host alive" and keeps host-set state while it arrives; steps 6 and 7 issued no type 16 reads, which would explain why everything expired. If true, the Unleashed timer only needs `read type 16` every 30 s (plus read type 42 to detect overrides), not repeated writes.

**Write test, step 8 (21:08-21:15): HEARTBEAT CONFIRMED.** Same sequence as step 6 plus a **type-16 read every 30 s** (reply always `02 00 00 00`, rc 0): PL1/PL2 stayed **80/80 for the whole 360 s** (without the heartbeat they expired at ~110 s), concurrent TDP stayed 82, and the final gated write (TDP 55) was **APPLIED**, so the Unleashed profile was still active after 6 min with only the heartbeat. Restore OK (PL1/PL2 55/55 written once, TDP 30, Balanced, fans 21/19; the 55/55 expires by itself ~2 min later). **Conclusion (H): a type-16 read every 30 s keeps profile + PL1/PL2 alive. No repeated writes are needed.** Side note: type 42 byte[0] fell 66 -> 51 over the first 3 min then flattened at 51-52, like a temperature cooling to idle; **unverified, not used**.

**Probe B (22:04, read-only, `docs/probe-b-output.txt`): PASS.** (1) **Command 1 / type 15 works through WMI**: the charger reply `01 AE 00 09` three times = **status 1 MeetsRequirement**, barrel supported, USB-C design rating 0 W, power rating 45 W (identical to OGH's own log), so the weak-charger rule does **not** fire on this charger. (2) **GPU status (type 33) = `00 01 00 57`: cTGP 0, PPAB 1 (on), dState 0, GPS 87** (= `GpsMaxTemperature`: GPU power sharing at its maximum target). (3) Throttling (type 53) byte[2] = 0 (not throttling). (4) Sensors idx 0 and 1 both 35 C, steady over 10 s at idle (the earlier probe read 36 on all four). OGH's GPS change is a read-modify-write of the 4-byte GPU record (its log prints `CurrentGpu: [cTGP, PPAB, dState, GPS]` before changing GPS), so the exact write arguments must be re-read unfiltered before any GPU write test.

**b0 correlation (22:09-22:11, read-only + 30 s half-core load): type 42 byte[0] is NOT a CPU temperature.** It equals the ACPI `_TZ.THRM` zone at every sample (61 -> 67 -> 68 -> 62 -> 63 -> 79 -> 73 -> 77), a zone that *fell* under sustained load (68 -> 62) and spiked to 79 after the load ended. The four BIOS sensors stayed 35 C the whole run; fans 21/19 until 23/21 at t=68 s. **Conclusion: no driverless CPU temperature exists; the governor stays CPU-temperature-free.** Note: OGH's background process had been running again since 21:24:23 (user restarted OGH after step 8), during both probe B and this test. Reads are unaffected, but the GPU record seen in probe B (`00 01 00 57`) may be OGH-managed; a clean baseline with OGH stopped for 3+ minutes is planned.

**Probe B rerun with OGH stopped (22:18, OGH background last active 22:14:55 = 3 min 41 s before): identical.** GPU record still `00 01 00 57` (cTGP 0, PPAB 1, dState 0, GPS 87), charger `MeetsRequirement`, throttling 0, sensors 34 C. **So the GPU record is the firmware's resting state (or a value that does not lapse), not something OGH was holding.** Whether a *host-written* GPS lapses without a heartbeat is untested (gated GPU test).

**GPU write patterns in OGH (D, unfiltered):** `GetGpuStatus` result -> locals `cTGP, PPAB, dState, GPS`. Overheat: `SetGpuStatus(0, 0, dState(+1 in one path), GpsMin 75)` (**PPAB off** because `PpabOffWhenIrOverheat` is true), then `SetGpuStatus(0, 0, 1, 75)`, then `SetGpuStatus(cTGP, 0, 1, GpsMax)` in one branch; release: `SetGpuStatus(cTGP, PPAB, 1, GpsMax 87)`. So OGH also writes **dState = 1** and turns **PPAB off**; the meaning of dState/PPAB for the GPU is not known. **Test design: change only the GPS byte, leave cTGP/PPAB/dState exactly as read, restore after** (`tools/gpu-step.ps1`).

**GPU write test, step 1 (22:32, OGH stopped, `docs/gpu-log.txt`): PASS.** Type 34 `00 01 00 57` (the record just read, no change) -> `rc=0`, read-back `00 01 00 57`. **GPU write path confirmed; state unchanged.** Restore point saved (`docs/gpu-state.json`). Step 2 (GPS byte only, 87 -> 85, read-back, 20 s watch, restore) is next.

**GPU write test, step 2 (22:33): PASS.** Type 34 `00 01 00 55` (GPS 87 -> 85, cTGP/PPAB/dState unchanged): `rc=0`, **read-back `00 01 00 55` immediately and after 20 s**, others unchanged; automatic restore `00 01 00 57` verified. **A GPS-only change is applied by the firmware with dState left at 0 (no gating, unlike concurrent TDP).** Not yet known: whether a host-written GPS lapses without a heartbeat, and what PPAB off does (OGH turns it off on overheat); both are in `gpu-step.ps1 -Step 3`.

**GPU write test, step 3 (22:45-22:50): PASS, and the GPU record does NOT lapse.** (a) **PPAB off** `00 00 00 57`: `rc=0`, read-back identical, others unchanged, still set after 20 s; restore verified. (b) **GPS 85 held for the full 270 s with no type-16 heartbeat** (reads of type 33 only); restore verified. **Unlike PL1/PL2 and the Unleashed profile, a host-written GPU record persists until someone changes it.** Consequences for the app: the governor must **restore the GPU baseline itself** (when the chassis releases, on leaving Unleashed, on exit); **a crash can leave the GPU in its reduced state** (cooler and calmer, so the safe direction, but silently slower) until the app restarts, so the app must persist the baseline record (`00 01 00 57` today) and **re-apply it at start-up whenever no governor reduction is active**. Whether a reboot clears it is untested.

**Unleashed mode byte decoded (D):** `CurrentMappedDefaultValue`: 0 -> 0x30, **4 -> 4**, 256 -> 256 (then `SetFanMode` V1 maps 256 -> 0x30), everything else -> 0x31. So **Unleashed = mode byte `0x04`**. Not yet tested on hardware.

**CPU-temperature correlation (19:12, `docs/cpu-temp-correlation.txt`; 16 workers, CPU utility 3 % -> ~80-90 %):** `_TZ.THRM` was flat/stepwise (62.9 -> 70.9 C before the load, then *fell* to 65.9 C during sustained load, 77.9 C afterwards). `_TZ.TZ01` jumped +10 C at load start, then *fell* to ~61 C under steady load, and peaked at 83.9 C about 5 s *after* the load ended. **Neither behaves like a CPU die sensor**; both look like slow board/chassis-type zones. Verdict: **the ACPI zones are not a usable CPU temperature source** (OGH's two CPU readings, which would settle it, are pending from the user). Last driverless lead, HP's `root\HP\InstrumentedBIOS` numeric sensors (read as admin, `docs/hp-bios-sensors.txt`): **empty, no sensors exposed.** **Result: no driverless CPU temperature source exists on this laptop.** For comparison, `AMDRyzenMasterDriverV31` (AMD Ryzen Master SDK kernel driver, `C:\Program Files\AMD\RyzenMasterSDK\bin`) is loaded on this PC right now; OGH's service bundle contains `AmdRyzenOverclockingService.dll` and its code calls `AMDSDKHelper` (`IsRmsdkPmTableWorking`), so OGH most likely gets CPU temperature through that driver (inferred, not proven). `HpReadHWData.sys` (HP's driver behind OGH's `ReadHWData` route) is also loaded.

**Not yet tested on hardware:** every write command (0x1A mode, 0x2E fans, 0x27, power limits), the mode read-back question, and the type 41/42/55 layouts.

---

## 1. Transport to firmware

| | Finding | Status |
|---|---|---|
| WMI class | `root\wmi : hpqBIntM`, methods `hpqBIOSInt0/4/128/1024/4096(InData: hpqBDataIn, OutData: hpqBDataOut*)`. `hpqBDataIn` = `Sign`(u8[]), `Command`(u32), `CommandType`(u32), `Size`(u32), `hpqBData`(u8[], length = `Size`). `hpqBDataOut*` = `Sign`, `rwReturnCode`(u32), `Data`(u8[]). Calling an instance needs administrator rights (non-admin: access denied). | MOF read on this PC (no admin) |
| Packet OGH builds | `"SECU"` (53 45 43 55) + Command (LE u32) + CommandType (LE u32) + Size + data. Size class: 0 / 4 / 128 / 1024 / 4096 (input padded up to the class). Return size class selects the method (0, <=4, <=128, <=1024, else 4096). Return code `0` = OK; OGH treats `-1` as transport failure. | D |
| Same fields as the WMI class | Packet layout matches `hpqBDataIn` field for field and the five size classes match the five methods. So plain WMI is the expected route. **Confirmed on hardware for reads (section 0).** Writes not yet tested. | D + **H** (reads) |
| Command id | Always `0x20008` (131080). | D |
| OGH's own routes (NOT used by this app) | (a) `ExecuteBiosWmiCommandThruDriver`: sends the packet to **HP's kernel driver** via `ReadHWData.Open/Rdmsr` (OGH UI/BG logs show this). (b) `BiosWmiCmd_*`: IPC to the HP SYSTEM service `HPOmenCap` (native `OmenCap.exe`, verifies the caller). Both rejected: (a) is an undocumented vendor-driver interface (conflicts with "no sketchy / no driver"); (b) can't be used by a third-party process. | D |

## 2. BIOS commands decoded (command always `0x20008`)

Sizes are (input bytes -> output bytes). "Layout U" = payload not decoded yet, so **not allowed in the app**.

| Type (dec / hex) | OGH name | Dir | Input / output as decoded | Status |
|---|---|---|---|---|
| 16 / 0x10 | `GetNumOfFan` (really BIOS-protection flags) | read | `00 00 00 00` -> 4 B; byte[1] bit0 = OCP, bit1 = OTP | D |
| **26 / 0x1A** | **`SetFanMode`** (thermal profile) | write | 4 B: `[0xFF, mode, fanControlByBios(0/1), 0x00]`; rc must be 0 | D |
| 33 / 0x21 | `GetGpuStatus` | read | layout U -> 4 B | U |
| 34 / 0x22 | `SetGpuStatus` / `SetTGP` / `SetTgpPpab` | write | layout U | U |
| 35 / 0x23 | `GetSensorValue(index)` | read | `[idx,0,0,0]` -> 4 B, value = byte[0]. idx 0 = IR (or ambient if `ChangeIrSensorToBoard`), 1 = ambient, 2 = PCH, 3 = VR | D |
| 39 / 0x27 | `SetMaxFan` | write | 1 B: 0 = off, 1 = on | D |
| 40 / 0x28 | `SystemDesignData` | read | `00 00 00 00` -> 128 B; **byte[3] = thermal policy version** | D + **L** (OGH cached: `E6 00 2D 01 0B 00 01 07 2D 00 ...` -> byte[3] = 1 = V1, matches log `GetThermalPolicyVersion = V1`) |
| 41 / 0x29 | `SetPowerLimit` / `SetConcurrentTdp` / `SetPL4(b__1)` | write | 4 B record `[PL2, PL1, PL4, concurrentTDP]`, **`0xFF` = leave that field unchanged**. PL1/PL2: `[pl2, pl1, FF, FF]` (AMD: pl2 = pl1 when 0). Concurrent TDP: `[FF, FF, FF, v]`. PL4: `[FF, FF, v, FF]`. Each setter touches only its own field, so no read-modify-write hazard. Constants recovered from OGH's SHA-256-named data fields. | D |
| 42 / 0x2A | `GetPowerData` | read | **empty input (Size 0)** -> 128 B; byte[3], byte[4] = host-set PL1/PL2 (0 = none set; **they expire about 2 min after the write**), byte[6] = PL4, byte[7] = concurrent TDP. | D + **H** (steps 5-6) |
| 44 / 0x2C | `GetFanType` | read | `00 00 00 00` -> 128 B: bytes 0-3 = fan-type nibble pairs; bytes 8, 9 = capability bit masks | D |
| 45 / 0x2D | `GetFanLevel` | read | `00 00 00 00` -> 128 B: current fan levels until first 0 | D |
| **46 / 0x2E** | **`SetSwFanControlLevel`** | write | 128 B: `[cpuLevel, gpuLevel, fan3Level(only 3-fan models), 0...]`, raw units; rc 0 = OK | D |
| 47 / 0x2F | `GetFanTable` | read | `00 00 00 00` -> 128 B (layout U) | D (input) / U (output) |
| 53 / 0x35 | `GetIsThermalThrottling` | read | layout U -> 128 B | U |
| 55 / 0x37 | `SetTwoBytePowerLimit` / `SetPL4(b__0)` | write | 128 B two-byte form, used only above 254 W (not needed at 80 W): `[0x20,0,FF,FF,0,0,pl2lo,pl2hi,0,0,pl1lo,pl1hi,0,0,...]` | D |

**Important gap:** OGH's code contains **no BIOS read for "current thermal profile / mode"**. OGH tracks the mode in its own profile storage. So firmware read-back verification of the *mode itself* may be impossible; verification would have to use indirect reads (fan levels, power data) or re-apply on events. (U, to settle in Phase 0b.)

## 3. Modes

OGH `PerformanceMode` enum (D): Default 0, Performance 1, Cool 2, Quiet 3, Extreme 4, L0 16, L1 32, L2 48, L3 64, L4 80, L5 17, L6 33, L7 49, L8 4, Eco 256.
`ThermalControl` enum (D): Max 0, Auto 1, Manual 2. `FanSetScheme` (D): EWMA_ALGO, SLIDER, IDLE_AUTO, NO_SET.
OGH lists on this laptop (L): Eco, Balance, Performance, Unleash; `IsSwFanControlSupport = True`, `IsSurfaceTempSupport = True`, `IsWin11PowerModeSyncSupport = True`, `IsSystemSupportedPBO = True`.

Mode -> BIOS byte on thermal policy V1 (this laptop), from `SetFanMode(isMapped=true)` (D):

| OGH mode | Sent as byte[1] of type 0x1A |
|---|---|
| Default (OGH "Balance"; label mapping inferred from the mode list order) | **0x30 (48)** |
| Performance | 0x31 (49) |
| Cool | 0x50 (80) |
| **Eco (256)** | **0x30 (48), identical to Default** |
| Unleashed = mode 4 (L8, log: `mode=L8 -> UNLEASHED`) | **U**: the code path that decides the byte for mode 4 with custom fan curve is not fully decoded |

Last logged OGH state (L): `Mode = 48` (0x30), `ThermalControl = 2` (Manual), custom fan curve enabled. Earlier today `Mode = 4`, `ThermalControl = 1`.

OGH's own custom-curve bounds on this board (L, read from its log 2026-11-11): upper bound 48 at every point; lower bound 19 up to 70 °C and **29 from 75 °C**, for both the CPU and the GPU fan (the IR list: 29 from 50 °C). Other values it logged: `UnleashedModeTppDefaultValue = 72`, `PerformanceModeMinimumBatteryValue = 10`, `GetThermalPolicyVersion = V1`, `IsUnleashedModeSupport = True`, `AutoSwitchToPerformanceMode = True`. The app uses the 48 ceiling and the 29-from-75 °C floor for the custom curve.

### Eco (D)
Eco is **not** just a BIOS value. `Entry.UpdateEcoModeSettings` also calls `SetNvidiaMaxFrameRate`, `SetDisplayRefreshRate`, `SetIntelTurboBoost` (Intel only, n/a here) and `SyncWindowsPowerMode` (Windows power mode). So "Eco" = BIOS profile 0x30 + display refresh change + NVIDIA frame cap + Windows power mode. -> **Decision needed (section 8).**

### Balanced (D, partly U)
Balance = Default -> 0x30 with `ThermalControl = Auto`. In OGH, "Auto" is **not firmware-only**: `fanControlByBios` (3rd payload byte) is set by `IsNBBiosFanControl()` (true only on battery with `BiosAutoFanControlInDc`, or low-power-mode on AC); otherwise OGH's own software algorithm (EWMA fan scheme, adaptive power control, PL1 clipping) runs in OGH's background process. Reproducing "Balanced with OGH automatic thermal management" therefore means re-implementing that software loop, or accepting BIOS-only control. **Decided 2026-11-11: BIOS-only auto control, `fanControlByBios = 1` (section 8).**

## 4. Unleashed: your spec vs HP's limits for this laptop

HP's platform file `Khalilah_DRG_N22X4X6.json` (D, version 20250523) gives:

| Spec value | HP limit / data | Verdict |
|---|---|---|
| Thermal reference CPU, 9-point curve 50..90 C | OGH custom curve = 9 points 50,55,...,90 with **separate CPU, GPU and IR tables**; fan speeds in raw units | Format matches |
| Fan 50 / 60 / 65 / 70 / 100 % | Raw 19..48 (upper bound 48 at every point; lower bound 19 up to 70 C, **29 from 75 C**). Your saved OGH curve 24, 29, 31, 34 = `round(percent x 0.48)` -> 100 % = **48**. Spec raw values: 24, 29, 31, 34, 48 | **Within bounds** (conversion rule inferred, **confirm in UI code**) |
| SPPT 80 W | `UnleashedModeMinPL1 45`, `MaxPL1 80`, `DefaultPL1 80`; OGH currently stores **`UnleashedModePowerLimit1 = 80`** (L) | **Accepted (at the maximum); mapping confirmed by OGH's stored value.** Applied as BIOS type 41 PL1/PL2 |
| Chassis temp limit 54 C | `MinSurfaceTemp 42`, `MaxSurfaceTemp 54`, `Default 54`; OGH stores **`UnleashedModeSurfaceTemp = 54`** (L) | **Accepted (at the maximum).** *Not a firmware write:* input to OGH's IR-sensor PL1 governor |
| Smart Performance Gain 27 W | OGH stores **`UnleashedModeTppOffset = 27`**, `UnleashedModeTppMode = 1` (L). `SetConcurrentTdp` sends value = offset + platform `TppMinValue 55` = **82** (D), only on AC + performance mode + TPP supported. Platform `TppMaxValue` is 72 | **82 W is above the platform's 72 W TPP maximum.** OGH's UI accepted the offset, but **step 2 (H): the write returned rc=0 and the firmware did not apply 82 (read-back stayed 30).** **Step 5 (H): under the Unleashed profile `0x04` the firmware applied 82 (read-back 82): accepted.** The earlier refusals were mode gating |
| Max Battery Drain 40 % | OGH stores **`UnleashedModeMinimumBatteryValue = 40`** (L). Threshold = (100 - 40) / 100 = **0.60** (D) | **Accepted by OGH; the earlier "possibly below 45" concern is withdrawn** (`BatteryLowerBound 45` is the PL1 floor, not this value's range). *Not a firmware write:* governor input |

Other facts from the same file (D): software fan algorithm intervals `IntervalAlgoShort 1000 ms`, `IntervalAlgoLong 30000 ms`, `IntervalHeartbeat 30000 ms`; custom-curve smoothing `Lamda_Increase 0.1 / Lamda_Decrease 0.1`; OGH's built-in Unleashed fan table differs from your custom curve (it is the platform default, not your values).

### OGH's Unleashed power governor (D)
`AdaptivePowerControlV1` (OGH background process) treats `UnleashedModePowerLimit1` as the **upper bound** of PL1 and adjusts PL1 at run time, writing it with BIOS type 41:
- **Battery drain:** when on AC but the battery is draining and Windows `PowerStatus.BatteryLifePercent` < threshold (0.60 for your 40 %), it lowers PL1 by 1 W per cycle, down to `max(BatteryLowerBound 45, NbPL1LowerBound)`, and raises it again when safe. It only acts when measured CPU power is near PL1 (`WorkingRatioPL1 0.9`).
- **Chassis temperature:** BIOS IR sensor (type 35, idx 0) against the platform's `IrOverheat/IrPl1/IrRelease` threshold arrays and `SurfaceTemp`; lowers PL1 by `IrPl1Reduce 5` W steps.
- Cadence from the platform file: `IntervalAlgoShort 1000 ms` while active, `IntervalAlgoLong 30000 ms` otherwise.
- Inputs: battery % and AC state (standard Windows API, **no driver**), IR sensor (BIOS, no driver), measured CPU power (`PowerContext.CpuPower`, from OGH's service / AMD SDK: **not driverless**). Everything except CPU power is available without a driver; the CPU-power check only avoids needless PL1 changes.

### Governor port: what it needs (D, 2026-11-11; the user wants both safety rules implemented)
- **Chassis rule (IR):** in Unleashed with `SurfaceTemp > 0`, OGH sets `irOverheatThreshold = irGpsThreshold = irPl1Threshold = SurfaceTemp` (54) and `irReleaseThreshold = SurfaceTemp - UnleashedModeIrReleaseThresholdOffset` (platform value, not read yet; the platform arrays are used only otherwise). Input: IR sensor (BIOS type 35). Actions by `IrOverheatCase`: **overheat** -> cut **GPU power sharing** (GPS to `GpsMinTemperature`, PPAB off when `PpabOffWhenIrOverheat`) **and** PL1 -= `IrOverheatPl1Reduce` (5 W); **GPS band** -> stepwise GPU power-sharing reduction; **PL1 band** -> PL1 -= `IrPl1Reduce` (5 W); **release** -> raise PL1 / GPS again. It uses **GPU commands type 33 (read: cTGP, PPAB, dState, GPS) and type 34 (write)**, layouts not decoded or tested.
- **Battery rule:** threshold (100 - 40)/100 = 0.60. Decrease: `BatteryLifePercent < 0.60` with `IsBatteryDrain` -> PL1 -1 W per cycle down to `max(BatteryLowerBound 45, NbPL1LowerBound)`, also gated on measured CPU power near PL1 and GPU usage above `GamingGpuUsage`. Increase: +`Increase` (5 W) gated on the **average CPU temperature** vs `CpuTemperatureThreshold` / `...Lower` and on throttling counts. `IsBatteryDrain` is set elsewhere (not decoded).
- **Inputs with no known driverless source:** CPU temperature (increase logic), measured CPU power (gate). **Substitutes that exist:** battery % and AC state (Windows API), IR sensor and thermal-throttling flag (BIOS types 35, 53), GPU usage (Windows GPU counters), GPU power state (types 33/34).
- So an *identical* port is impossible without a driver; a *behaviour-equivalent* port of the two protections (PL1 and GPU power sharing, driverless inputs) is possible but needs the GPU commands decoded and hardware-tested.
- **Lead:** type 42 byte[0] moves like a temperature (66 -> 51 while cooling; 50-67 at idle). `tools/b0-correlation.ps1` tests it under a short load; if it tracks the CPU it would be a **driverless CPU temperature**, which would also make OGH's increase logic reproducible.

### Unleashed is software, not a firmware table (D)
- The custom fan curve runs in OGH's background process: `FanHandler` (`GetAlgoFanSpeed`, `GetTargetFanSpeed`, `SetFan`) + `EWMA` smoothing + the 3 temperature inputs (CPU, GPU, IR), then writes levels with type 0x2E. `DynamicFanCurve.IsOverheating / UpdateOverheatOverrideState` **overrides the user's curve when overheating** (a safety net we must replicate).
- The surface (chassis) temperature limit is enforced by OGH's adaptive-power software loop (`AdaptivePowerControlV1.UpdateIrSettings`, IR thresholds) which lowers PL1, not by a single firmware setting.
- OGH appears to apply software fan/power control only on **AC** (inferred from result enums `FanControlResult.NoFanControlBiosAutoFanControlInDc`, `PowerControlResult.NoPowerControlDc`); on battery BIOS auto control seems to stay. To confirm.
- **Fan count (H):** **2 fans, CPU and GPU.** Type 44 returned `21` = fan types 1 (Cpu) and 2 (Gpu); type 16 byte[0] = 2. The write command (0x2E) takes separate CPU and GPU levels, so independent levels exist in the interface; whether the spec's single CPU-referenced curve should drive both is a design choice.
- `Entry.ApplySettings` can also call `AMDSDKHelper.SetPBOStatus` (AMD Ryzen Master SDK, which uses an AMD kernel driver). **Excluded** by the no-driver rule unless you say otherwise.

## 5. CPU temperature
- OGH's CPU temperature comes from its HP service (`OmenHsaClient.GetCpuTemperature`, native code, not readable) and it is never logged. **U.**
- Driverless candidates on this PC (no admin, no driver): Windows counters `\Thermal Zone Information(*)\Temperature`: `_TZ.THRM` (~58 C) and `_TZ.TZ01` (~75 C) at 7 % CPU load. **Which one, if any, tracks the CPU the way OGH does is unproven.** Test: compare against OGH's own CPU temperature readout under a short load (user-run, read-only). If no zone tracks the CPU, a CPU-temperature curve is **not possible without a driver**, and that must be your call.
- BIOS sensors (type 35 idx 0-3) are IR / ambient / PCH / VR only, no CPU. On hardware all four returned 36 C (0x24): one shared sensor or the index is ignored; useless for the curve either way (section 0).
- Correlation test prepared: `tools/cpu-temp-correlation.ps1` (no admin, moderate 30 s load, aborts at 92 C). Result goes in section 0 once run.

## 6. Detecting resets / OGH interference
- OGH re-asserts its state: `RunHeartbeatLoop` / `OnTimedEventHearbeat` every 30 s, log line `Heartbeat Start - Initial or User logon or OGH take-control`; also reacts to `PowerEvent`, `UserLogon`, `UserUnlock`, game start/stop (`AutoSwitchToPerformanceMode = True`). (D/L; heartbeat internals not decoded yet.)
- Because there is no firmware read for the mode (section 2), detection options: re-apply on resume/unlock/logon (events), read `GetFanLevel`/`GetPowerData` as indirect witnesses, and treat OGH running as a conflict signal only if you allow process-name checks (currently excluded). **U.**

## 7. Prerequisites (partial)
- Admin rights for WMI calls (confirmed). Windows ACPI-WMI mapper devices on this PC: `ACPI\PNP0C14\0`, `\AOD`, `\CPSWMI` (all OK). Which one hosts `hpqBIntM` and whether HP drivers are needed: **U** (probe prints the instance name; HP drivers `HP Omen Driver 1.0.838/1.0.704`, `HP Application Driver 1.82/1.66` are installed here).
- Fresh/debloated Windows: preflight will check `Winmgmt`, `Schedule`, mapper device, class `hpqBIntM`, board `8E35`.

## 8. Decisions
*First version (1.0); superseded by sections 11–14.*


**Made by you (2026-11-11):**
1. **Custom fan curve dropped** (no driverless CPU temperature, section 5).
2. **All three modes use automatic thermal management:** the mode byte + `fanControlByBios = 1` (the firmware's own fan control, the state OGH itself uses on battery). Caveat: on AC, OGH layers a *software* fan algorithm on top of "Auto"; that part needs CPU/GPU temperatures and is **not reproduced**. `fanControlByBios = 1` on AC is decoded from code but **untested on hardware** (it must be the first write test).
3. **Unleashed keeps your four settings** (SPPT 80 W, SPG 27 W, Max Battery Drain 40 %, chassis 54 C).

**Still open:**
4. **How Unleashed applies them.** SPPT and SPG are plain BIOS writes: PL1/PL2 `[80,80,FF,FF]` and concurrent TDP `[FF,FF,FF,82]` (82 = 27 + 55). Battery drain and chassis temperature are OGH's *software governor inputs*, not writes. Options: **(A, recommended)** static writes only; report the other two as "stored but not enforced by this app". **(B)** A + a small governor, only while Unleashed is active on AC: battery % below 60 % -> PL1 -1 W per cycle down to 45 W (and back up); IR sensor above threshold -> PL1 -5 W. No driver needed (Windows battery API + BIOS IR sensor), but it polls while active and the IR sensor semantics are unverified (all four sensors read 36 C).
5. **Eco scope.** Include display refresh-rate + NVIDIA max-frame-rate changes (what OGH does) or only BIOS profile + Windows power mode? Recommended: the latter; the NVIDIA/refresh parts touch the display driver and the spec lists graphics optimisation as out of scope.
6. ~~SPG 82 W above the platform's `TppMaxValue 72`~~ **Resolved (step 5, H): the firmware accepts 82 W under the Unleashed profile.**
7. **Unleashed heartbeat (resolved, steps 6-8):** profile and PL1/PL2 expire after ~2 min without a type-16 read; a 30 s heartbeat read keeps them alive and runs only while Unleashed is active. **Crash safety comes free:** if the app dies, Unleashed ends by itself in ~2 min, so no restore-on-exit is needed (this closes the spec's open exit/crash question).

## 8c. Governor and power-state decode (Roadmap A, 2026-11-11; all D unless marked L)

**AC/DC and charger (OGH's auto-Eco):**
- Source of truth: Windows `WM_POWERBROADCAST` + `GetSystemPowerStatus` (`ACLineStatus == 1` = AC). Event-driven.
- `ShouldAutoSwitchToEcoMode` = feature on and (on battery, **or** on AC with `IsLowWattageAdapter`) and not "overridden" (user changed mode manually). `ShouldRestoreFromEcoMode` = feature on, on AC, adapter **not** low-wattage, not overridden. OGH restores the *previous* mode (Eco is derived on top of the user's mode); **the user chose "always Balanced" instead.**
- **Charger read:** `BiosWmiCmd_GetSync(command = 1, commandType = 15, 4 zero bytes, return 4)`, **not command 0x20008**; retried up to 3 times. Reply: byte[0] `SmartAdapterStatus` (0 NotSupported, 1 MeetsRequirement, 2 BelowRequirement, 3 BatteryPower, 4 NotFunctioning, 5 ConnectedTypeC); byte[1] bit 7 = barrel supported; byte[2] x 5 = USB-C design rating (W); byte[3] x 5 = adapter power rating (W, 0xFF = unknown).
- OGH's `IsLowWattage`: status 5 -> low if `0 < rating < designRating`, or (barrel supported and designRating == 0); **any other status != 1 -> low** (so NotSupported counts as low in OGH). **Our app is stricter (fail-open): only status 2, or the Type-C rule, counts as weak; two consecutive reads.**
- **L:** OGH's logs on this PC with your charger: `status=MeetsRequirement`, `isLowWattage=False`, `powerRating=45` (read at every OGH start). First hardware read of command 1 / type 15 still to do (probe-b).
- **Windows power mode (Eco side effect):** `PowerSetUserConfiguredACPowerMode / ...DCPowerMode` (powrprof). Eco = `961cc777-2547-4f9d-8174-7d86181b8a7a` (Best power efficiency), Balanced = `Guid.Empty`, performance (Unleashed) = `ded574b5-45a0-4f42-8737-46345c09c238` (Best performance). Only applied when OGH's `AutoSwitchToWin11PowerMode` is on and the API exists.

**Battery rule, exact (D):**
- `IsBatteryDrain`, set on every power event (percent change), only on AC: reset to false when not on AC, or when the power source just changed, or on the first sample; else if already draining: `drain = !(last < current)` (stays true until the percentage rises); else `drain = last > current`. Then `last = current`.
- Threshold = `(100 - UnleashedModeMinimumBatteryValue) / 100` = **0.60**. Platform constants: `BatteryDrainBuffer 0.03`, `BatteryLowerBound 45` (JSON; code default 35), `GamingGpuUsage 30`, `UnleashedModeDefaultBatteryDrain 40` (so your 40 % is OGH's default).
- `ProcessBatteryDrain_DecreasePL1`: acts **only if GPU usage > 30 % and `IsBatteryDrain`**. If `battery% < 0.60`: PL1 -= 1 W (floor `max(BatteryLowerBound, NbPL1LowerBound)`), `SetPL1` immediately. If `0.60 <= battery% < 0.63`: hold (cancel any pending increase). Above 0.63: increases allowed. Result is capped at `NbPL1UpperBound` (80 in Unleashed).
- Increase (not ported exactly): +`Increase` steps gated on average CPU temperature vs 90/85 C and throttling counts. Our substitute gate: BIOS throttling flag (type 53) clear and no IR overheat state.

**Chassis (IR) rule (D, skeleton; exact arithmetic to be re-read when implementing):**
- Thresholds in Unleashed with `SurfaceTemp > 0`: overheat = GPS = PL1 = 54, release = 54 - `UnleashedModeIrReleaseThresholdOffset` (5) = 49. Input `IrTemp` = BIOS sensor (type 35). Runs every `IrCycle` (30) counter ticks.
- Cases (`IrOverheatCase`): **overheat** (>= 54): GPU target temperature (GPS) to `GpsMinTemperature` 75 and PPAB off if `PpabOffWhenIrOverheat`, **and PL1 -= `IrOverheatPl1Reduce` (5 W)**. **GPS band:** GPS target = GPU temp - IR gap, clamped 75..87 (`GpsMin 75`, `GpsMax 87`). **PL1 band:** PL1 -= `IrPl1Reduce` (5 W) x min(consecutive cycles, 3). **Release** (IR below 49): PL1 stepped back up toward the bound; when IR is below release and GPS was cut: GPS back to 87.
- **GPU commands:** type 33 read (4 zero bytes in, 4 out = `[cTGP, PPAB, dState, GPS]`); type 34 write `[cTGP, ppab, dState, gps]` (bytes; OGH calls it with `(0, 0, 1, gps)` for GPS steps; meaning of the zeros and of dState = 1 is **U**, to be read from hardware first). Throttling read: type 53, input `[0, 4, 0, 0]`, return 128, **byte[2] nonzero = throttling**.
- Platform constants (SDK `PlatformSettings` defaults): `IrOverheatPl1Reduce 5`, `IrPl1Reduce 5`, `GpsMinTemperature 75`, `GpsMaxTemperature 87`, `CpuTemperatureThreshold 90 / Lower 85`, `IntervalHeartbeat 30000`, `IntervalAlgoShort 1000`.

**Still U before the governor is built:** meaning of GPU status bytes (probe-b reads them), the IR sensor's real behaviour (all four BIOS sensors read the same at idle), exact release arithmetic, and whether type 42 byte[0] is a CPU temperature (`b0-correlation.ps1`).

## 8a. Values the app writes (static, decided 2026-11-11)

| Mode | Mode byte (type 0x1A) | Power (type 41) | Notes |
|---|---|---|---|
| Eco | `[FF, 30, 01, 00]` | PL1 = PL2 = 55, concurrent TDP = firmware default | Windows power mode change (decision 5 still open). |
| Balanced | `[FF, 30, 01, 00]` | PL1 = PL2 = 55, concurrent TDP = firmware default | Same BIOS profile as Eco (OGH maps both to 0x30). |
| Unleashed | `[FF, 04, 01, 00]` (D: mode 4 is sent as 4) | PL1 = PL2 = 80, concurrent TDP = 82 | Concurrent TDP is ignored under Balanced but **applied (55 and 82, read back) under 0x04** (step 5). Whether it and PL1/PL2 persist without a refresh: step 6. |

Sources (D): platform `PL1DefaultValue 55` / `NbPL1UpperBoundDefault 55` (Performance 60); in Unleashed OGH sets PL1 default = `UnleashedModePowerLimit1` (80); AMD: PL2 = PL1 when 0. OGH's "default concurrent TDP" comes from SystemDesignData byte[8] = `0x2D` = 45 W (`DefaultConcurrentTdpNoCache`), **but the value actually in the firmware right now is 30 W (H, step 1)**, so the app must not assume 45. OGH writes concurrent TDP only in performance mode on AC. **Leaving Unleashed must restore** PL1/PL2 = 55 and concurrent TDP = the value read from type 42 at first start before any write (30 W on this PC today), otherwise Unleashed's values would stick.
Verification (H, steps 5-6): PL1/PL2, concurrent TDP and PL4 are all verified by type 42 read-back. **The Unleashed profile and PL1/PL2 expire ~2 min without OGH's heartbeat, a type-16 read every 30 s (step 8, H). So Unleashed runs that 30 s heartbeat plus a type 42 verify (write only on mismatch); Balanced/Eco need no timer (expiry falls back to the firmware default, believed to be the same 0x30 profile; inferred).** **Ordering rules (H):** enter Unleashed = profile, then PL1/PL2, then concurrent TDP (ignored outside Unleashed); leave = restore concurrent TDP first (while still Unleashed), then write PL1/PL2 55/55 once, then the Balanced profile. OGH's *dynamic* PL1 (up to 55 W in Balanced) is not reproduced; 55 W is its upper bound.
**Unleashed mode byte:** decoded, see section 0 (`0x04`); its hardware behaviour is untested.

## 8b. Phase 1 write-test plan (each step needs your explicit go)
Run with OGH left running unless a step says otherwise (its 30 s heartbeat also gives us override evidence), on AC, battery > 50 %, no game open. Script: `tools/write-step.ps1`, which prints the exact packet and sends only after you type `SEND`.
1. **No-op write:** read type 42, write back the *same* concurrent TDP (`[FF,FF,FF,C]`), read back. Proves the write path and the 0xFF semantics with no state change. **PASS 2026-11-11 19:42.**
2. **SPG:** write 82, read back immediately and after 35 s (OGH override check), auto-restore the step-1 value. Answers "does firmware accept 82". **Result 19:46/19:49: rc=0 but not applied** (section 0). Next: **2b** = same step with `-Value 55` (OGH's own baseline TPP) to separate "value too high" from "needs a performance profile".
3. PL1/PL2 80/80 then 55/55 (return code only, no read-back) - after 1-2 pass.
4. **Balanced = `[FF,30,01,00]`** (type 26) with **OGH's background process stopped** (otherwise it keeps writing fan levels); watch fan levels (type 45) for 30 s. **PASS 19:58** (fans 19/16 -> 23/21). Next: `-Step 5` (Unleashed `0x04` + concurrent TDP 55/82 read-back, always restores Balanced and 30 W).
5. **Unleashed `[FF,04,01,00]`**, then concurrent TDP 55 and 82 with read-back. **PASS 20:01** (both applied under 0x04, ignored under Balanced).
5b. **`-Step 6` persistence test (OGH stopped, ~6 min idle):** profile 0x04, PL1/PL2 `[80,80,FF,FF]`, concurrent TDP 82, then sample type 42 bytes 3-4 and 7 plus fan levels every 10 s for 330 s to see whether anything reverts without a refresh (the heartbeat question). Auto-restores Balanced, TDP 30, and PL1/PL2 55/55 only if they are still set.
5c. Steps 7 and 8: the profile expires without the heartbeat (7: NOT APPLIED after 10 min); **the type-16 heartbeat every 30 s keeps profile and PL1/PL2 alive for 6+ min - PASS 21:15.**
6. Full mode switches, then a reboot to learn whether any setting is volatile or persistent. (Reboot test dropped: the app re-applies the saved mode at logon anyway.)
The mode byte itself has **no read-back**; return to your old OGH setup by restarting OGH (it re-applies its stored state).
Abort rules (safety protocol 6) apply throughout; rescue path for step 1-2: `write-step.ps1 -Step R`, or restart OGH (it re-applies its own state).

## 10. The app (built 2026-11-11, `app/`)
*Version 1.0 ("OmenModes"); superseded by sections 11–14.*

- **Source** `app/src` (C# 5, Windows' in-box `csc.exe`, `/noconfig`; references only mscorlib, System, System.Windows.Forms, System.Drawing, System.Management; P/Invoke only `kernel32`, `powrprof`). **Build** `app\build.cmd`: `out\selftest.exe` (109 checks pass) and `out\OmenModes.exe` (64 KB).
- **Firmware access** only through `HpBios` (range checks, no clamping) over `IFirmware`: `WmiFirmware` (real; refuses write types in read-only mode for `--read` / `--dry-run`) and `SimFirmware` (simulator encoding sections 0 and 8: TDP gating, ~110 s expiry without type 16, GPU record persistence). The allow-list in `FirmwareRules` is exactly the verified pairs; the self-check proves no other type 0..255 is accepted.
- **Orchestration** `AppCore` (`Modes.cs`): enter/leave Unleashed in the order of 8a with read-back and rollback; heartbeat 30 s + governor 5 s only while Unleashed; conflict -> at most 3 re-applies, then Balanced + failure naming OGH; start-up repair after a crash (GPU baseline back; TDP baseline back via profile 0x04 -> TDP -> 0x30).
- **Decisions taken during the build (not in OGH):** GPU usage is not read (it would need per-process GPU counters), so the battery rule treats the GPU as busy (acts on any AC drain below 60 %: more protective than OGH); `--dry-run` is a governor observer on live reads; the "GPU reduced" flag is saved before the reducing write; charger "weak" = status 2 or the USB-C rule, double read, fail-open.
- **Measured with `--demo`** (simulator, no hardware, no admin): idle working set 5.5 MB (peak 23.4 MB, private 20.9 MB), CPU 0.03 s in 20 s; Unleashed loop 8.2 MB, CPU 0.09 s in 40 s. Budget 50 MB.
- **Not yet validated on hardware:** the app itself (its WMI calls go through the same `hpqBIntM` route as the PowerShell tests). Ordered checks: `docs/USER-TESTS.md`. Everything it touches: `docs/WHAT-IT-DOES.md`.

## 11. Second sources: Ohman and Linux hp-wmi (2026-11-12), and v2 "Salva OPM"
Reference copy of Ohman (GPL-3.0), read at commit `66663ce2ac8119f980fbf5419050fdaffd7e4239` (main, 2026-11-02; never built or run). Line references below are to that commit. Labels:
- **O** = Ohman source or its research.md (measured on other OMEN boards; 8E35 is on its owner-verified list);
- **L** = Linux `drivers/platform/x86/hp/hp-wmi.c`;
- **H** = verified on this laptop.

| Fact | Source | Status here |
|---|---|---|
| Type 16 (`0x10`) arms/keeps the firmware's "user-defined thermal and fan state"; fallback after 120 s | L, O | matches H (steps 6-8: profile + PL lapse ~110 s, kept by type 16) |
| Calling type 16 while fans should be automatic holds them (Ohman: "freezes them"); Ohman sends it only right before a fan-level write | O | not tested here. v2 follows it: type 16 is sent only directly before type 46 |
| Type 26 byte 2 = fans by BIOS; with 1 the firmware ignores levels and runs its own curve; Ohman sends 1 only on battery (Auto) and on exit | O (from OGH) | v1 used 1 on AC (H). v2 uses 0 on AC: new, test H1 |
| Type 46 = fan levels, 128 B `[cpu, gpu, 0…]`; 0 = off, 1..17 refused by Ohman | O, L | new, test H1. v2 writes 19..48 only (this board's type-47 table top = 48, H) |
| Type 45 = fan speed in 100 rpm units | O | consistent with H (idle 21/19) |
| GPU presets Base `{0,0,1,75}`, Boost `{0,1,1,87}`, Max `{1,1,1,87}` (type 34; Ohman always writes dState 1) | O | GPS/PPAB writes H (GPU steps 1-3); cTGP 1 and dState 1 new, tests H3/H4. v2 verifies bytes 0, 1, 3 only |
| Ohman's Auto curve for boards without a table: CPU `{23,23,25,32,39,46,46,46,49}`, GPU `{23,25,31,35,46,46,46,46,46}` at 50..90 °C, top 57, fallback 35, 3 levels per 5 s tick, smoothing 0.4 down, deadband 2, refresh 30 s | O | v2 rescales to top 48 (Ohman's own rescale rule): CPU `{19,19,21,27,33,39,39,39,41}`, GPU `{19,21,26,29,39,39,39,39,39}`, fallback 29 |
| Thermal guard: engage after 2 ticks at CPU >= 95 or sensor-1 >= 62 or (CPU >= 75 and fan sum < 10); release after 60 s at CPU < 85 and < 54; one warm sample doesn't reset the minute | O | copied as-is. Ohman's guard reads `0x23` index 1 (ambient); OGH's 54 °C rule reads index 0 (IR); on this laptop all four read the same at idle (H) |
| Exit: park at max(fallback, current), then profile with fans by BIOS = 1 | O | copied |
| OMEN key = `hpqBEvnt` EventID 29, EventData 8613 | O | new, test H7 |
| Graphics mode: command 1/2 type 0x52, byte 0 & 0x7F; 0 Hybrid, 1 Discrete, 2 Optimus, 3 iGPU-only; rc 6 = accepted, applies after restart; system data (type 40) byte 7 = offered modes (1 iGPU, 2 Hybrid, 4 Discrete, 8 Optimus) | O | new, test H8 |
| AMD die temperature: PawnIO `AMDFamily17` module, `ioctl_read_smn 0x59800`, bits 31:21 × 0.125, bit 19 -> −49 °C; median of 3 | O | new, test H2 |
| GPU temperature: NVML in process; don't poll while the dGPU is in D3 (polling wakes it) | O | new, test H2 |
| OGH take-over: end `OmenCommandCenterBackground` and `OmenInstallMonitor`; disable `\OmenInstallMonitor*` tasks | O | tasks exist on this PC: `OmenInstallMonitor-sid-…`, `OmenInstallMonitorCustomEvent-sid-…` (read 2026-11-12) |

**Superseded rows above (2026-11-12, v2.1/v2.2):**
- **Auto curve:** no rescale. Ohman only rescales upwards (`Engine.cs:544`), so on 8E35 its table runs unscaled: CPU `{23,23,25,32,39,46,46,46,49}`, GPU `{23,25,31,35,46,46,46,46,46}`, fallback 35; writes capped at 48.
- **Graphics mode:** removed from the app (never read or written).
- **Fan table (type 47) columns:** per Linux `hp-wmi.c` each row is `{cpu_rpm, gpu_rpm, noise_dB}`. So this board's table runs from 19/16 to 44/46 (×100 rpm); the `0x30` in the last row is 48 dB, not a level. Level 48 is OGH's own upper bound for a custom curve here (section 3), and the firmware accepted it (section 12).
- **Linux lists 8E35:** `hp_wmi_feature_boards` maps `8E35` to `omen_v1_legacy_board_params`, which uses the same fan path (`0x2E` set, `0x10` trigger, 120 s timeout, 90 s keep-alive).

## 12. Salva OPM on this laptop (hardware evidence, 2026-11-12)
Identity (read-only): HP `OMEN Gaming Laptop 16-ap0xxx`, board `8E35`, BIOS `F.13` (AMI, 2026-05-13), EC `99.24`.

From `C:\ProgramData\SalvaOPM\log.txt`, the user's own installs of v2.0 (01:20) and v2.1 (17:07). All **H**:
| What | Result |
|---|---|
| PawnIO 2.2.0 | installer exit 0; `PawnIO.sys` signed by "Microsoft Windows Hardware Compatibility Publisher", running with HVCI on (VBS status 2, service 2) |
| CPU temperature (PawnIO SMN) | 328 curve steps, never unknown; 46–77 °C |
| GPU temperature (NVML) | never unknown; 35–50 °C |
| Fan-level writes (16 + 46, fans-by-BIOS 0 on AC) | accepted all day, levels 19–48; fan watch never tripped; no refusal |
| GPU presets | Base `00-00-01-4B`, Boost `00-01-01-57`, Max `01-01-01-57` all read back; **dState echoes 1, cTGP 1 accepted** |
| Performance | "PL 80/80, TDP 82 verified" ten times; leaving restores the baseline |
| Power rules | unplug → Eco (auto) 17:12; replug → Balanced 17:13; boot on battery → Eco (auto) 17:15 and 22:38; plug → Balanced |
| Exit / shutdown | "parked fans at 21/23 … handed to the firmware's own curve" |
| OGH background with only the tasks disabled (v2.0) | came back at two logons (ended by the app each time) |
| OGH full removal (v2.1) | `HPOmenCap` stopped and disabled; `OverlayHelper`, `OMENKeyboardRemapper`, `OMENKeyboardRemapLauncher` ended; 2 + 2 tasks deleted; package "not installed any more"; both HP folders deleted. Verified after: no package, no Omen task, no Omen process |

Measured on the running v2.1 (elevated, Performance, window open at times): working set 12–20 MB, private 54 MB, CPU 4 s in 17 min.
- **Handles** rose about 5 per second and fell back when .NET's garbage collector ran (975 → 447): undisposed WMI result objects, not a leak. v2.2 disposes them at once.
- Stand-in measurement: 40 WMI method calls without `Dispose` = +152 handles, back to baseline after a collection. 20 NVML init/read/shutdown cycles = +0.

**v2.2 build:** `app\build.cmd` → `dist\SalvaOPM-Setup.exe` (3.56 MB), 161 self-checks. `--demo`: 34.0 MB with the window open, 2.1 MB in the tray.

**Not yet on hardware (v2.2 changes):** Auto-only fan switch in Eco/Balanced, OGH's 2900 rpm lower bound from 75 °C, retry after a failed apply, immediate WMI disposal, tray icon per mode. Still never observed: thermal guard engaging, either governor rule acting, OMEN key event, behaviour after a BIOS update. Checks: `docs/USER-TESTS.md`.

**Documentation checked (2026-11-12):**
- `PowerSetUserConfiguredACPowerMode` / `DCPowerMode`: documented Microsoft API (powrprof.h); the efficiency / none / best-performance GUIDs are the documented values.
- PawnIO (pawnio.eu): "scriptable universal kernel driver" by namazso; the official build is the signed one; provided "as is".
- `Remove-AppxPackage -AllUsers` + `Remove-AppxProvisionedPackage -Online`: Windows' own cmdlets; no official Microsoft page was found for this exact workflow, community guides agree.

## 13. Salva OPM 2.2 on hardware, and the 2.3 changes (2026-11-13)
From the user's 2.2 install (2026-11-12 23:09, log):
- Identity check passed (`16-ap0xxx`, `8E35`, `F.13`). A stale concurrent TDP (82, from an earlier kill) was repaired to 30 at start. **H**
- Modes, presets and "PL 80/80, TDP 82 verified" on every Performance entry. **H**
- One `drift detected (PL 80/80, TDP 45)` in Performance, re-sent once, not repeated. Cause unknown (OGH was already removed).
- GPU record is re-written at every boot, so it does not survive a shutdown (it had only been shown not to lapse while running).
- **User report:** after leaving Performance the laptop stays hot with loud fans, unlike after a fresh start. No temperatures were logged in 2.2.

2.3 response (hypothesis, to be confirmed by check 3 in `docs/user-tests.md`):
- Leaving Performance wrote PL1/PL2 55 W, and the fan keep-alive (type 16 every 30 s) held that host state indefinitely; a fresh start has PL 0/0. Host limits cannot be cleared by a write (range 45..80), only by lapsing.
- 2.3 leaves the firmware alone for 125 s after Performance (fans by BIOS, no 16/26/41/46), then expects type 42 to read PL 0/0.
- Auto curve is now the owner's quiet table `{19,21,24,29,34,42,48}` at 60..90 °C, with an 85 °C / 60 s push to 48.

## 14. Salva OPM 2.4 on hardware (2026-11-13, from `log.txt` and the Windows System log)
- **Host PL lapses by itself (H):** after leaving Performance with nothing sent, `PL 0/0` read 125 s later, three times (09:58 → 10:00:05, 13:23:27 → 13:25:33, 13:32:20 → 13:34:54). The ~110 s expiry assumed since step 8 is now measured.
- **A profile write in the lapse keeps PL alive (H):** left Performance 13:37:25 (PL 55); Eco 13:39:12 and Balanced 13:40:23 re-sent profile `0x30` and the GPU record; at 180 s PL still read 55/55. Fixed in 2.4.1: nothing is sent during the lapse.
- **TDP reset after GPU Base → Max (H):** entering Performance from Eco, TDP read back 82 and then 45 about a second later, 6 of 6 times; from Balanced (Boost → Max) never, 7 of 7. Rewriting 82 holds (13:43:18 → 13:46 without drift).
- **Unplugging clears the host PL (H):** 13:46:03 left Performance by unplugging (PL 55 written); 11 s later, on AC again, PL read 0/0.
- **Modern Standby (H):** `powercfg /a` lists S0 Low Power Idle only. Kernel-Power 506/507 show 13 standby cycles at 13:31–13:44; the app received no resume event. 2.4.1 detects the gap in its timer.
- **Firmware fan curve when cool (H):** with the firmware in charge the fans read 0 rpm at CPU up to 64 °C.
- **Eight hours in Performance (H5, 2.4.2):** 14:56–22:56, window closed, PC kept awake. Working set 38–44 MB, handles 401–929 with no trend, no warnings. Private memory 53.4 → 56.8 MB (about 0.3 MB/h after hour 4); accepted, since it starts fresh at every Windows start. The 85 °C rule engaged again while gaming (22:44:43) and the lapse ended 127 s after leaving Performance (22:49:48).
- **Uninstall and install (H4, 2.4.2):** everything removed and restored; the factory GPU record `00-01-00-57` read back after reinstall, so the uninstall's restore works. PawnIO downloaded again (3,410,960 bytes, hash matched, exit 0). PawnIO's own uninstaller leaves its driver package in the driver store.
- **No HP drivers needed (H7, 2.4.3, 23:30–23:35):** with `HP Omen Driver` (ACPI `HPIC0004`) and `HP Application Driver` (`HPIC0003`) removed, every mode applied and verified, CPU temperature read, OMEN key worked. `hpqBIntM` is a WMI class from the BIOS itself (provider `WmiProv`, GUID `5FB7F034-2C63-45E9-BE91-3D44E2C707E4`, the same as Linux `hp-wmi`), served by Windows' own `WmiAcpi` driver on `ACPI\PNP0C14`.
- **Exit in Performance (2.4.3, 2026-11-14 00:03:49):** the tray Exit put the GPU record back to Boost (`00-01-01-57`) before handing the fans to the firmware; the GPU record does not lapse by itself.
- **Uninstall from Settings > Apps and a fresh install (2.4.5, 2026-11-14 03:51–03:56):** the program folder was gone although the final message stayed open for 10 s (2.4.4 and earlier left the exe in that case). The fresh install created the data folder in one step with its permissions (owner Administrators; SYSTEM and Administrators full, Users read) and read the factory GPU record `00-01-00-57` again.
- **Thermal guard on the laptop (2.4.6, 2026-11-14):** first time observed. Gaming in Performance on a custom curve that tops out at 4300 rpm, the CPU reached 95 °C: `THERMAL GUARD engaged: cpu 95, ambient 42` at 04:32:04, fans to maximum, `thermal guard released` at 04:37:25 (a minute after the game ended). No other warning.
- **Audit fixes on the laptop (build of 49f62c8, 2026-11-18 02:43–02:47):** fresh install, then Eco to Performance and back. The concurrent-TDP baseline (30) was kept and put back when leaving Performance (02:45:48); both entries verified PL 80/80 and TDP 82. Entering from Eco the firmware again showed TDP 45 right after the GPU write, as in section 14.
- **Fan-speed reads every 2 s do not hold host state (2.5.0, 2026-11-18 03:29:59–03:32:06):** with the window open the app read type 45 every 2 s; after leaving Performance (PL 55) the limit still lapsed in 127 s, as with the 10 s reads.
