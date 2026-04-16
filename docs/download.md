---
title: Download Quest Multi Stream
description: Release artifacts for the public Windows preview.
summary: Choose between the guided setup bootstrapper and the portable zip release.
nav_label: Download
nav_group: Getting Started
nav_order: 2
---

# Download Quest Multi Stream

The public Windows preview is published through GitHub Releases.

## Recommended path

Use `QuestMultiStream-Preview-Setup.exe`.

That guided bootstrapper:

- downloads the latest `QuestMultiStream-win-x64.zip`,
- installs or updates the app under `%LOCALAPPDATA%\Programs\QuestMultiStream`,
- refreshes Desktop and Start Menu shortcuts,
- launches the installed app.

## Direct assets

- `QuestMultiStream-win-x64.zip`: portable desktop build with the bundled scrcpy runtime.
- `SHA256SUMS.txt`: published hashes for release verification.

## Notes

- The packaged and portable releases bundle the scrcpy runtime, so they do not
  depend on a checked-out source repo.
- The guided setup bootstrapper can be code-signed when a signing certificate is
  configured for releases, but the portable zip remains the simplest fallback.
