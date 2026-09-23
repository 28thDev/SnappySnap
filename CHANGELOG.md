# Changelog

## 1.1.5 — release candidate

- Window and browser capture suggestions are clipped to the visible desktop, fixing region capture failures for maximized or partly offscreen windows.
- The first screenshot opens an editor. While an editor remains open, subsequent captures save directly to Shelf. When all editors close, the next capture opens an editor again. A failed save always retains the image in an editor for retry.

This PATCH improves the existing screenshot workflow and supersedes the 1.1.4 tester candidate. Owner acceptance and publication are pending.

## 1.1.4 — release candidate

- Fixed opacity slider keyboard steps sticking at some percentages because of floating-point conversion.
- Reviewed all editor property sliders with exhaustive step, boundary, undo/redo and settings round-trip checks.

This PATCH fixes an existing editor interaction and supersedes the 1.1.3 tester candidate. Owner acceptance and publication remain pending.

## 1.1.3 — release candidate

- Take successive screenshots while earlier editors remain open. Each editor saves its own capture independently.
- Opacity shows a rounded percentage instead of a long floating-point fraction.
- Shelf context commands use distinct Open, Edit, Refresh and Screenshot icons.

This PATCH candidate fixes existing capture and editing workflows and supersedes the 1.1.2 tester build. Owner acceptance and publication are pending.

## 1.1.2 — release candidate

- Fixed the default Print Screen shortcut being shown as invalid after installation. The key now keeps the same display name through input, normalization and registration.

This PATCH candidate supersedes the 1.1.1 tester installer. It awaits owner testing and is not published.

## 1.1.1 — release candidate

- Rectangle, line and freehand annotations also start at 10 px and reach 24 px; former saved defaults move once while custom widths remain.
- Right-click cancels region selection. Print Screen captures the full virtual desktop directly, then saves it and opens the editor.
- Double-click or Enter on a Shelf item opens SnappySnap's editor. The Open action still uses the Windows default app.
- Browser address-and-page capture begins at the detected toolbar's top edge so the URL controls are included.

This candidate also includes the automatic window and browser region proposals and thicker arrows from the unreleased 1.1.0 tester build. It was superseded by 1.1.2 before publication.
The PATCH increment reflects compatible improvements to the existing capture, editor and Shelf workflows.

## 1.1.0 — release candidate

- Arrow annotations start at 10 px and can reach 24 px; an old saved default of 5 px migrates once to 10 px.
- The region selector highlights the window under the pointer. In Chrome, Edge and Firefox it proposes the whole browser over tabs, the address bar plus visible page over the toolbar, and the visible page over page content.
- A click accepts the proposed region; dragging selects a custom rectangle. Screenshot double-click still selects the full monitor. If browser zones cannot be determined reliably, the selector clearly offers the whole browser including tabs.

The 1.1.0 tester build was superseded before publication. Its installer remains available for comparison; see the newest candidate entry above.

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
