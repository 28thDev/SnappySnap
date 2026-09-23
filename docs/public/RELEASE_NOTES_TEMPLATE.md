# SnappySnap VERSION — DRAFT, not released

Preparation template: replace `VERSION` and `<version>` with the candidate version, complete the checklist, and save the final notes as `docs/public/releases/<version>.md`. Do not upload this template as final release notes.

SnappySnap brings region screenshots, annotation and MP4 screen recording to a native Windows application. Captures remain local, with no account or cloud service required.

## Included

- Screenshot annotation, crop, clipboard copy, replacement save and separate copies.
- Region recording with pause/resume, mouse-click visualization and system/microphone controls.
- Video trimming and removal of middle sections, with export to a new file.
- Recent-capture history, multi-selection and saved-file drag-out.
- English by default, with Russian, Simplified Chinese, Japanese and Spanish UI, plus System/Light/Dark appearance.

## Download and install

Download **`SnappySnap-Setup-<version>-x64.exe`** from the assets below. “Source code” archives are for developers. Requires Windows 11 x64 build 22000 or newer. Setup includes the runtime and installs for the current user without administrator rights.

Before updating, finish recording/export and exit through the tray. Captures/preferences are retained. Compare the downloaded installer's SHA-256 with `SHA256SUMS.txt`.

## Complete before publication

- [ ] State actual Authenticode signing status.
- [ ] Link the approved license and describe any dependency/codec limitations accurately.
- [ ] Record tested Windows/display/audio setups and known limitations.
- [ ] Confirm clean install, upgrade and retained data in a disposable profile.
- [ ] Confirm source tag, commit and installer hash match.
- [ ] State whether this is a validation build or the official release; list only completed acceptance, and retain known limitations.
- [ ] Confirm the signed catalog, signature and installer are all attached before publication.

SnappySnap checks official stable GitHub Releases automatically unless you disable the option in Settings. Downloading and installing an update require your actions. A verified cached installer can be installed offline. See [privacy and update requests](https://github.com/28thDev/SnappySnap/blob/master/docs/public/PRIVACY.md) for the network and local-data details.
