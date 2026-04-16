# Live Verification Workflow

Use this loop whenever the cast host shell, source switching, or future composite
layer changes need live confirmation against a connected Quest.

## Host Window Checks

1. Start a real cast from the app, or let the CLI do it:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 smoke-cast
   ```

2. Keep the cast window visible as the visual source of truth.
3. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 inspect-cast
   ```

4. Capture screenshot evidence for the current visible state:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 visual-test
   ```

5. Review the saved PNGs and `manifest.json` under
   `artifacts\visual-tests\<timestamp>\`.

6. Compare:
   - `EmbeddedHostSize`
   - `ScrcpyChildSize`

If those sizes diverge, the hosted `scrcpy` child is not filling the cast shell.

For temporary recovery while iterating:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 inspect-cast -FixEmbeddedSize
```

## Reliability Rule

Do not trust a single still image when validating stereo or motion-sensitive
behavior. Re-check the active cast after a resize, source switch, or state
change so the live window stays the primary evidence.

## Composite Layer Direction

The old crop-based "spectator" idea is not the right model for a usable mono or
participant-style view. The reference pattern from
`Dynamic Oscillatory Pattern Entrainment` is stronger:

- keep the visible sample eye-local
- compute a world-space point for each visible fragment
- reproject that fragment through the correct eye pose and intrinsics
- use any combined two-eye analysis only for hidden effect fields, not for the
  final visible sample

Relevant reference files:

- `C:\Users\tillh\source\repos\Dynamic Oscillatory Pattern Entrainment\Documentation\Build\projected-feed-colorama-quad-notes.md`
- `C:\Users\tillh\source\repos\Dynamic Oscillatory Pattern Entrainment\Assets\Scripts\Synedelica\Passthrough\PassthroughProjectedFeedQuadRenderer.cs`

## Capture Pipeline Constraint

The bundled `scrcpy 3.3.4` build on this machine can:

- disable local playback with `--no-playback`
- record to a file with `--record`
- expose `--v4l2-sink`, but only on Linux

It does not provide a Windows-native live sink that this WPF app can directly
compose against. A true custom composite layer therefore likely requires an
app-owned decode path instead of more `scrcpy` window embedding tricks.

## Next Composite Milestone

Build the custom composite layer only after the hosted cast shell is stable.
The likely order is:

1. make `Display 0` casting reliable inside the host shell
2. capture the stereo feed into an app-owned render path
3. implement a reprojection-based composite view instead of a 2D crop
