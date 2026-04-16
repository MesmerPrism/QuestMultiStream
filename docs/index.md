---
title: Quest Multi Stream
description: Public project overview for the Windows Quest casting operator app.
summary: Multi-headset Quest casting on Windows with source switching, resize-safe wrapper controls, and a public preview installer path.
nav_label: Overview
nav_group: Getting Started
nav_order: 1
---

# Quest Multi Stream

Quest Multi Stream is a Windows operator shell for driving one or more Meta
Quest casts through [`scrcpy`](https://github.com/Genymobile/scrcpy). It keeps
the upstream video path intact, then layers operator controls around that feed:
source selection, wake/proximity control, pinning, and resize-safe cast window
management.

## What the Windows app already handles

- One row per connected headset.
- Per-headset source selection across working Quest displays and cameras.
- Resize-safe cast wrapper behavior that restarts the feed cleanly instead of
  smearing stale frames.
- Wake and keep-awake control for headsets that would otherwise sleep mid-cast.
- CLI-driven visual verification through screenshots.

## Delivery channels

- [Download](/download.html): release assets and what each file is for.
- [Installer Guide](/installer.html): guided preview setup flow.
- [Architecture](/architecture.html): how the app is structured.
- [Live Verification](/live-verification.html): smoke and screenshot workflow.

## Source repo

The source lives in the public GitHub repo and keeps the desktop app, CLI,
packaging scripts, and GitHub Actions workflows in one place.
