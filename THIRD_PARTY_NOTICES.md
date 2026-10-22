# Third-Party Notices

## scrcpy

- Project: `Genymobile/scrcpy`
- License: Apache License 2.0
- Source: <https://github.com/Genymobile/scrcpy>
- Usage in this repo: launched as the external mirroring engine for each Quest session

This repository does not vendor `scrcpy` source code. When you download the
official Windows build with `tools/setup/Install-Scrcpy.ps1`, keep the upstream
license files that ship inside the release archive.

## Reference-Only Inputs

- `hiroyamochi/quest-screen-caster`
  - Source: <https://github.com/hiroyamochi/quest-screen-caster>
  - Usage in this repo: feature inspiration only
  - Note: no explicit repository license was found on April 2, 2026, so no code
    from that repository is copied here

- `ViscerealityCompanion`
  - Local reference: `C:\Users\tillh\source\repos\ViscerealityCompanion`
  - Usage in this repo: project structure and operator-shell style reference

## External owned display producers

- [rusty-quest](https://github.com/MesmerPrism/rusty-quest) and
  [rusty-hostess](https://github.com/MesmerPrism/rusty-hostess): AGPL-3.0
  producer/receiver implementations selected as separate external processes.
- QMS independently implements only the documented RMANVID1 protocol adapter
  under this repository's MIT license. No Quest, Hostess or Meta platform
  implementation or binary is included or relicensed by this integration.
- Exact external helper/script paths and hashes belong to operator configuration;
  their licenses and any platform requirements continue to apply independently.
