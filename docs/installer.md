---
title: Guided Installer
description: How the public Quest Multi Stream preview installer works on Windows.
summary: The guided setup installs the portable preview under LocalAppData and launches it from the Start menu.
nav_label: Installer Guide
nav_group: Getting Started
nav_order: 3
---

# Guided Installer

The preview installer is a small Windows bootstrapper that exists to make the
public preview usable on a clean machine.

## What it does

1. Downloads the latest `QuestMultiStream-win-x64.zip` release archive.
2. Extracts it under `%LOCALAPPDATA%\Programs\QuestMultiStream`.
3. Replaces any previous portable install in that location.
4. Creates Desktop and Start Menu shortcuts.
5. Launches Quest Multi Stream after install when possible.

## When to use the raw files instead

Use the raw `QuestMultiStream-win-x64.zip` if:

- you want to inspect the package manually,
- your environment blocks the bootstrapper EXE,
- or you want to control the install location yourself.
