# SnappySnap dependencies

Runtime dependencies are distributed with the application or provided by Windows. No system-wide prerequisite installer, cloud service, telemetry SDK, FFmpeg binary, or external capture application is required on the user's machine.

| Dependency | Version | License | Purpose |
| --- | --- | --- | --- |
| ScreenRecorderLib | 7.0.1 | MIT | Windows screen/video capture, pointer/audio source integration, H.264/AAC recording |
| Microsoft Visual C++ Runtime (x64) | 14.44.35211.0 (VS 2022 redist directory 14.44.35112) | Microsoft Visual Studio redistribution terms | App-local `concrt140.dll`, `msvcp140.dll`, `vcruntime140.dll`, `vcruntime140_1.dll`, required by ScreenRecorderLib on a clean Windows installation |
| SharpDX.Direct3D11 | 4.2.0 | MIT | D3D11 device/texture interop for capture fallback and MediaPlayer frame-server preview |
| SharpDX.DXGI | 4.2.0 | MIT | DXGI adapter/output enumeration, Desktop Duplication and preview surface interop |
| SharpDX | 4.2.0 (transitive) | MIT | SharpDX base interop types |
| Microsoft.Data.Sqlite | 10.0.12 | MIT | SQLite metadata/history storage |
| SQLitePCLRaw (bundle/core/provider/native package, transitive) | 2.1.12 | Apache-2.0 for wrapper; SQLite public domain | Native SQLite runtime carried with self-contained publish |
| System.Drawing.Common | 10.0.12 | MIT | Windows thumbnail/image utility operations |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT | Test host |
| Microsoft.CodeCoverage (existing transitive test dependency) | 17.12.0 | MIT | Built-in Cobertura collector for `test.ps1 -Coverage`; not distributed with the application |
| xunit | 2.9.3 | Apache-2.0 | Unit/integration test framework |
| xunit.runner.visualstudio | 3.0.0 | Apache-2.0 | Test discovery/runner integration |
| Inno Setup x64 (build tool only) | 7.1.0 | Inno Setup License (official distribution `license.txt`) | Offline per-user Setup EXE and uninstaller; compiler prepared explicitly in repository-local portable mode |

Platform components used without a third-party runtime package:

- .NET 10 and WPF;
- .NET BCL ECDSA P-256/SHA-256 and named pipes (CurrentUserOnly), Windows DPAPI for developer release-key protection, Win32 user mutexes and MessageBox for the updater. No new NuGet dependency. UpdateHost is a self-contained .NET executable with no WPF/WinForms dependency.
- Windows Graphics Capture, WinRT `Windows.Media.Editing`, Media Foundation, D3D11/DXGI, Win32 monitor/DPI/hotkey/hook APIs;
- SQLite native provider included through Microsoft.Data.Sqlite.

The C++ runtime comes from the licensed Visual Studio C++ tools, not from System32 or an unrelated application's installation. See the [Microsoft runtime notice](../installer/notices/Microsoft-Visual-C-Runtime.txt) and [redistribution list](https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution). App-local runtime security updates must be delivered in subsequent SnappySnap packages.

License evidence is taken from the restored package metadata/licenses and the package project documentation at the exact versions above. The installer build includes applicable restored package license files and the dependency inventory in its notice bundle. Inno Setup's license is retained in `installer/notices/`; [build instructions](public/BUILDING.md) describe preparation and provenance. Its compiler is not a runtime dependency. The application remains local-only and does not send capture data or diagnostics to a service.
