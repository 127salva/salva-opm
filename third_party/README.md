# Third-party files

| File | What | Source | License |
|---|---|---|---|
| `AMDFamily17.bin` | Signed PawnIO module that reads AMD CPU registers (the app calls only `ioctl_read_smn` for the die temperature). Embedded in the exe, unmodified. | [PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) release 0.2.11, built from `AMDFamily17.p` in that release's source: <https://github.com/namazso/PawnIO.Modules/tree/0.2.11>. SHA-256 `dae74615761b78bdf064dfb3e136252ddcc6fc727d88f14738d0e5800d427a91` | LGPL-2.1-or-later, text in `PawnIO.Modules-COPYING.txt` |

The PawnIO driver itself is not in this repository or in the exe. When PawnIO is missing, the setup downloads the official signed installer, version 2.2.0, from <https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0>, checks SHA-256 `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032` and only then runs it (`Setup.PawnIoUrl`, `Setup.PawnIoSha256`).

The module is a signed binary because the official PawnIO driver loads only modules signed by its author. To rebuild it from source, see the PawnIO.Modules repository; a rebuilt module will not carry that signature.

Updating PawnIO: change `Setup.PawnIoUrl` and `Setup.PawnIoSha256` together, then re-run the hardware checks.
