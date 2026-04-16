# Quest Multi Stream

Quest Multi Stream is a Windows operator app for launching and arranging
multiple simultaneous Meta Quest casts. It uses `scrcpy` as the mirroring
engine, keeps each headset on its own `adb`/`scrcpy` session, and adds a single
WPF control surface for discovery, layout, wake/proximity helpers, and session
tracking.

## Why this shape

- `scrcpy` remains the strongest open-source Windows mirroring base for low-ish
  latency Android and Quest display streaming
- multiple headsets map cleanly to multiple `scrcpy --serial ...` processes
- a separate operator shell is simpler and more maintainable than forking
  `scrcpy` itself
- the GUI style follows the disciplined WPF operator-shell posture from
  `ViscerealityCompanion`

## Current scope

- detect Quest devices through `adb`
- launch one `scrcpy` process per headset
- arrange live `scrcpy` windows into a grid
- expose per-headset wake, Wi-Fi ADB, and proximity helpers
- keep open-source licensing and upstream references explicit

## Quick start

1. Install the official `scrcpy` Windows build:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\setup\Install-Scrcpy.ps1
   ```

2. Build the solution:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 build
   ```

3. Run the app through the repo CLI:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 run
   ```

4. Or use the desktop launcher wrapper:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
   ```

5. Recreate the Desktop and Start Menu launchers:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 refresh-shortcuts
   ```

## CLI

The operational entry point for this repo is:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 help
```

Useful commands:

- `run`
- `build`
- `publish`
- `verify-launch`
- `doctor`
- `devices`
- `logs`
- `inspect-cast`
- `refresh-shortcuts`

`run` is single-instance aware: if Quest Multi Stream is already open, the CLI
focuses the existing window instead of launching another copy.

Recommended live workflow:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 doctor
powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 verify-launch
powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 inspect-cast -FixEmbeddedSize
```

## Windows note

On this machine, Smart App Control can block freshly published unsigned single-file
EXEs. The CLI and desktop launcher therefore try the published app first, then
fall back to the locally built WPF EXE when needed.

## References

- [Implementation references](docs/references.md)
- [Architecture notes](docs/architecture.md)
- [Live verification workflow](docs/live-verification.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

## License

MIT for this repository. See [LICENSE](LICENSE). Upstream dependencies keep
their own licenses.
