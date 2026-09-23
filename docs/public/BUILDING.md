# Build and test

Use Windows 11 x64, Git, PowerShell 7 and the .NET SDK specified by `global.json`. A self-contained installer needs no separately installed .NET runtime on the user's machine. SDK/tool downloads are build-time requirements. On Windows N, the optional Media Feature Pack remains an OS feature rather than an application download.

```powershell
dotnet restore SnappySnap.sln --locked-mode
./build.ps1 -Configuration Release
./test.ps1 -Configuration Release -NoBuild
```

Run Debug tests after building Debug when changing native/WPF harnesses. The ordinary test suite does not start a full desktop capture session. Native acceptance tools are explicit Debug entry points and must use isolated profiles. Never run unknown harness arguments against an old binary: an unrecognized option can start the ordinary application.

## Installer

Publishing also requires the x64 Microsoft Visual C++ runtime from Visual Studio 2022 C++ Build Tools: redist directory `14.44.35112`, DLL file version `14.44.35211.0`. `publish.ps1` locates the C++ tools through `vswhere`; set `SNAPPYSNAP_VC_RUNTIME` to the matching `x64/Microsoft.VC143.CRT` directory for another installation layout. The publisher checks Microsoft signatures, x64 architecture and the pinned file version before copying the four ScreenRecorderLib dependencies beside the application. No C++ installer runs on the user's machine. Review Microsoft's [redistribution terms](https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution) when distributing these files.

```powershell
./installer/prepare-tools.ps1
./build-installer.ps1
```

The first command prepares repository-local Inno Setup 7.1.0 after checking the official installer signature. It does not install a system-wide compiler. Review Inno Setup's license for your use. The second command publishes a fresh self-contained payload, validates versions/manifests/dependencies/notices, and produces `artifacts/installer/SnappySnap-Setup-<version>-x64.exe` with a provenance sidecar.

Build from a clean committed revision when preparing an installer. Preserve the matching source commit, payload and build sidecar. Rebuilding an already distributed version creates ambiguity: use the appropriate next version, never replace an existing release asset with a new binary of the same name. See the [release versioning rules](RELEASE_PROCESS.md#versions-and-source).

## Public screenshots

Build Debug, then run the explicit artwork harness with a new absolute output directory:

```powershell
./build.ps1 -Configuration Debug
$output = Join-Path $PWD ('artifacts/public-screenshots-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
./src/SnappySnap.App/bin/x64/Debug/net10.0-windows10.0.19041.0/SnappySnap.exe "--public-screenshots=$output"
```

The harness renders real off-screen WPF windows with a fictional project, fully loaded history thumbnails and native video preview/export. It uses its own profile and media; it does not capture the desktop or change the clipboard, hotkeys or startup preferences. It refuses an existing output directory and writes `result.txt` on success or `failure.txt` on failure. Review the images before copying `shelf-history.png`, `screenshot-editor.png` and `video-editor.png` to `docs/public/images/`. Keep generated media, profiles and logs out of the public source tree. The entry point is excluded from Release builds.

## Dependency changes

Direct package versions are in project files; all projects commit `packages.lock.json`. CI restores in locked mode. For an intentional upgrade, update the project version, run `dotnet restore SnappySnap.sln --force-evaluate`, review lock-file and license changes, then rebuild and retest. Do not add a binary codec distribution without an explicit dependency/licensing decision.

CI also exercises release scripts in synthetic repositories after building. Locally, export a fresh public-source directory and pass it with the built `SnappySnap.exe` to `tests/Test-ReleaseTools.ps1`. Fixture tags, author identity and packaging files stay under ignored `artifacts/`; no installer is executed or remote contacted. The public-source CI gate requires the committed MIT `LICENSE` at the repository root; `Test-PublicTree.ps1 -RequireLicense` checks this before publication.

## Desktop acceptance

Use a disposable Windows 11 profile/VM for `installer/test-install.ps1 -DisposableProfile`, passing the new installer and a genuine previous installer when testing upgrades. Do not run installation/removal acceptance against your working capture history. The script refuses an occupied profile; do not bypass that guard.

For a release, exercise capture → edit → save/copy/drag; MP4 record → pause/resume → audio toggles → finalize → edit/export; shortcut conflicts; startup preferences; and installation/update/uninstall with retained files and settings. Check multiple monitors, mixed scaling and screen-reader use when claiming that coverage; otherwise state those limitations in the release notes. Record the tested OS, display/audio setup, source commit and outcomes locally. A green CI job is not a substitute.
