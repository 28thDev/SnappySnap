# Changelog

## 1.1.0 — release candidate

- Arrow annotations start at 10 px and can reach 24 px; an old saved default of 5 px migrates once to 10 px.
- The region selector highlights the window under the pointer. In Chrome, Edge and Firefox it proposes the whole browser over tabs, the address bar plus visible page over the toolbar, and the visible page over page content.
- A click accepts the proposed region; dragging selects a custom rectangle. Screenshot double-click still selects the full monitor. If browser zones cannot be determined reliably, the selector clearly offers the whole browser including tabs.

This candidate awaits physical browser and recording acceptance. The 1.1.0 installer is for local testing until the owner approves release.

## 1.0.0

First public release.

- Region screenshots freeze the desktop while selecting. Annotate, crop, copy, replace the original or save a separate copy.
- MP4/H.264 region recording with pause/resume, mouse-click indicators and independent system-audio and microphone controls.
- Video trimming and removal of middle sections, with export to a new file.
- Local capture history with thumbnails, multiple selection and file drag-out.
- English, Russian, Simplified Chinese, Japanese and Spanish interfaces; System, Light and Dark appearance.
- Per-user offline Windows 11 x64 installer, including .NET and the Microsoft C++ runtime needed for recording on a clean system.
- Automatic or manual GitHub update checks. Download and installation require user action; packages are verified against a signed catalog.

The installer has no Authenticode publisher signature. See the [release notes](docs/public/releases/1.0.0.md) for installation requirements.
