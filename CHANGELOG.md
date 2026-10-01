# Changelog

## 1.2.0

- New: Settings → Screenshots → Open editor after capture. When off, screenshots go straight to Shelf and follow the automatic clipboard option; a failed save still opens the editor for recovery.
- The browser "address bar and page" suggestion starts slightly above the toolbar, so its top edge no longer cuts through the bottom of the tab strip.
- The region selector uses fixed high-contrast colors and a readable hint in every appearance.
- The recording pill has a red recording indicator and can be dragged out of the way.
- Screenshot editor tools and actions share one row when the window is wide enough.
- Settings rows use consistent spacing; alternative shortcut suggestions appear next to the shortcut that has a conflict.
- The tray menu follows the selected appearance. Shelf cards show capture dimensions. Scroll bars and the primary button hover state are easier to see.
- Unexpected errors are written to the local log and reported without closing SnappySnap.
- Recording no longer writes a log entry for every mouse click. Old log files are removed automatically; only the ten most recent are kept.

## 1.1.0

- Fixed region capture failing when a proposed window extends outside the desktop. The highlight and captured output now use the visible intersection.
- The first screenshot opens the editor. While any screenshot editor remains open, including minimized, subsequent captures save directly to Shelf without opening additional editors. After all editors close, the next screenshot opens an editor again. A failed save opens an editor so the capture can be recovered.
- Open any number of screenshot editors manually from Shelf; each window keeps its own edits and save session. Concurrent saves remain tracked until all finish.
- Fixed opacity slider steps sticking and removed fractional tails from property labels.
- Print Screen captures the full virtual desktop; right-click cancels region selection. Hotkeys and Shelf report when Windows screen capture intercepts Print Screen.
- Window and browser hover suggestions support full browser, address toolbar with page, and page content. Unrecognized browser layouts explicitly offer the full window with tabs.
- Arrow, rectangle, line and freehand widths default to 10 px and reach 24 px; custom preferences are preserved.
- Double-click, Enter or Edit in Shelf opens the built-in editor. Context actions have distinct icons. Open still uses the Windows default app.
- Fixed an H.264 recording failure for selected regions with odd pixel dimensions.
- Video history thumbnails now use decoded frames; failed recording starts no longer report a recoverable file when none was created.
- Recording start no longer displays a Windows notification inside the selected video area.

Region screenshots use the frame frozen at hotkey time. Video records a fixed rectangle. Saved captures remain individually editable through Shelf.

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
