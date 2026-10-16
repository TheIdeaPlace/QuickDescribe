# Security Policy

## Reporting a vulnerability

Please report a security problem privately, not in a public issue or through Report a Bug (bug
reports are public):

- **[Report it on GitHub](https://github.com/TheIdeaPlace/QuickDescribe/security/advisories/new)**
  (private vulnerability reporting), or
- email **kelly@theideaplace.net**.

Include what you found, how to reproduce it, and what it could affect. You'll hear back within a
few days.

## What's worth knowing

Quick Describe captures the screen, a window or the focused item when you press a hotkey or
choose a command, and describes it on your PC with Windows' on-device AI.

- **Pictures stay on your PC.** They're described and read by Windows itself, through a small
  packaged helper that Quick Describe starts and talks to over its standard input and output.
  Pictures and results aren't saved or sent anywhere.
- **What it keeps** is your settings, what its update checks found, and its log, in
  `%LOCALAPPDATA%\Quick Describe`. The log says what Quick Describe started with and what went
  wrong, not what it described; it's kept for two weeks and leaves your PC only if you send it.
- **Bug reports** go through The Idea Place's bug-report relay to GitHub, only when you press
  Send, and are public. The form shows exactly what will be sent, and writes your user name as
  `%USERPROFILE%`.
- **Updates** come from this repository's releases, through Windows' App Installer.
- **Releases** are signed with Kelly Ford's Azure Artifact Signing certificate, so Windows shows
  the publisher before it installs. Only a `v*` tag's release build can reach the signing
  certificate.

## Supported versions

Only the latest release gets fixes.
