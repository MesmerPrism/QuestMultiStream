# Architecture

Quest Multi Stream keeps the architecture intentionally thin:

- `QuestMultiStream.Core`
  - `adb` discovery and Quest helper commands
  - `scrcpy` path resolution
  - session lifecycle and window arrangement
  - pure parsing and launch-profile logic

- `QuestMultiStream.App`
  - WPF operator shell
  - MVVM state and command binding
  - restrained dark operator theme inspired by `ViscerealityCompanion`

## Transport choice

The app does not reimplement video capture. It launches one external `scrcpy`
process per headset and manages those processes as sessions. That keeps the
codebase small and preserves upstream compatibility when `scrcpy` updates.

## Window management

Each active session gets a predictable window title. The app polls the
session's main window handle, then repositions windows into a grid inside the
primary working area.
