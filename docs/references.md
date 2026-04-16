# References

These are the upstream materials used to shape the first implementation.

## Direct inputs

- `Genymobile/scrcpy`
  - <https://github.com/Genymobile/scrcpy>
  - Windows install notes: <https://raw.githubusercontent.com/Genymobile/scrcpy/master/doc/windows.md>
  - Connection notes: <https://raw.githubusercontent.com/Genymobile/scrcpy/master/doc/connection.md>
  - Window options: <https://raw.githubusercontent.com/Genymobile/scrcpy/master/doc/window.md>
  - Video options: <https://raw.githubusercontent.com/Genymobile/scrcpy/master/doc/video.md>

- `hiroyamochi/quest-screen-caster`
  - <https://github.com/hiroyamochi/quest-screen-caster>
  - Readme used for feature ideas only: multiple-device mirroring, Quest model
    presets, proximity actions
  - No repository license was found on April 2, 2026, so code from that repo is
    not reused here

## Alternatives checked

- `barry-ran/QtScrcpy`
  - <https://github.com/barry-ran/QtScrcpy>
  - capable, but much heavier than needed for a clean Quest-only operator shell

- `t-34400/QuestMediaProjection`
  - <https://github.com/t-34400/QuestMediaProjection>
  - relevant because Quest-side MediaProjection is a more credible route to the
    real immersive scene than plain display mirroring

- `t-34400/MetaQuestScreenCaptureServer`
  - <https://github.com/t-34400/MetaQuestScreenCaptureServer>
  - useful prior art for Quest-side capture transport and headset-hosted helper
    patterns, even though it is not the base transport here

- PortalVR Cast
  - <https://portalvr.io/>
  - commercially licensed, so not a fit for an open-source redistributable base

## Local template reference

- `C:\Users\tillh\source\repos\ViscerealityCompanion`
  - used as the structural and visual template for a solid Windows operator app
