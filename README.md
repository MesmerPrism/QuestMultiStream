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
- `pages-build`
- `publish-preview-setup`
- `verify-launch`
- `doctor`
- `devices`
- `logs`
- `inspect-cast`
- `visual-test`
- `smoke-cast`
- `refresh-shortcuts`

`run` is single-instance aware: if Quest Multi Stream is already open, the CLI
focuses the existing window instead of launching another copy.

Release/build extras:

- `pages-build` builds the static GitHub Pages site into `site\`
- `publish-preview-setup` builds the guided Windows installer bootstrapper into `artifacts\windows-installer\`
- `build-package` is still present for experimental MSIX work, but the current public release path is the portable installer + zip workflow because local Desktop Bridge tooling is not yet aligned with `.NET 10`

Recommended live workflow:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 doctor
powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 verify-launch
powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 smoke-cast
powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 inspect-cast -FixEmbeddedSize
powershell -ExecutionPolicy Bypass -File .\tools\questms.ps1 visual-test
```

`visual-test` captures the visible Quest Multi Stream window(s) and any visible
`scrcpy` cast windows into `artifacts\visual-tests\<timestamp>\` together with a
`manifest.json`, so UI and cast regressions can be checked from the CLI without
manual screenshot tooling.

`smoke-cast` drives the app through UI automation: it launches or reuses the
app, presses the first `Start Cast` button it finds, waits for the cast to come
up, then captures screenshot evidence and fails if no visible `scrcpy` window
exists.

## Public release path

The repo now includes:

- a static GitHub Pages site under `docs/` built via `npm run pages:build`
- a Windows preview bootstrapper project under `src/QuestMultiStream.PreviewInstaller`
- GitHub Actions workflows for Pages deployment and tagged Windows releases

The currently verified public installer path is:

1. publish the portable Windows build with bundled scrcpy
2. zip it as `QuestMultiStream-win-x64.zip`
3. publish `QuestMultiStream-Preview-Setup.exe`
4. let the bootstrapper download the zip, install it under `%LOCALAPPDATA%\Programs\QuestMultiStream`, refresh shortcuts, and launch the app

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
