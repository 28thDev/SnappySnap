# Changelog

## 1.1.0 — release candidate

- Test builds show a letter (1.1.0-a, b, ...) in Settings, logs and Setup; the public release has no letter.

- Fixed region capture failing when a proposed window extends outside the desktop. The highlight and captured output now use the visible intersection.
- The first screenshot opens the editor. While any screenshot editor remains open, including minimized, subsequent captures save directly to Shelf without opening additional editors. After all editors close, the next screenshot opens an editor again. A failed save opens an editor so the capture can be recovered.
- Open any number of screenshot editors manually from Shelf; each window keeps its own edits and save session. Concurrent saves remain tracked until all finish.
- Fixed opacity slider steps sticking and removed fractional tails from property labels.
- Print Screen captures the full virtual desktop; right-click cancels region selection. Hotkeys and Shelf report when Windows screen capture intercepts Print Screen.
- Window and browser hover suggestions support full browser, address toolbar with page, and page content. Unrecognized browser layouts explicitly offer the full window with tabs.
- Arrow, rectangle, line and freehand widths default to 10 px and reach 24 px; custom preferences are preserved.
- Double-click, Enter or Edit in Shelf opens the built-in editor. Context actions have distinct icons. Open still uses the Windows default app.

Region screenshots use the frame frozen at hotkey time. Video records a fixed rectangle. Saved captures remain individually editable through Shelf.

Owner testing and publication are pending.

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
