[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory,[Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
if (!('SnappySnapBuildResources' -as [type])) {
Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class SnappySnapBuildResources {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryExW(string file, IntPtr reserved, uint flags);
  [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);
  [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
  [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
  [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
  [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
  public static string Manifest(string file) {
    var module=LoadLibraryExW(file,IntPtr.Zero,2);
    if(module==IntPtr.Zero) throw new Win32Exception();
    try {
      var resource=FindResourceW(module,(IntPtr)1,(IntPtr)24);
      if(resource==IntPtr.Zero) throw new Win32Exception();
      var data=LockResource(LoadResource(module,resource));
      if(data==IntPtr.Zero) throw new Win32Exception();
      var bytes=new byte[SizeofResource(module,resource)];
      Marshal.Copy(data,bytes,0,bytes.Length);
      return System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0').TrimStart('\uFEFF');
    } finally { FreeLibrary(module); }
  }
}
'@
}
$exe = Join-Path $Directory 'SnappySnap.exe'
if ((Get-Item $exe).VersionInfo.FileVersion.Trim() -ne "$Version.0") { throw 'EXE version mismatch.' }
[xml]$manifest = [SnappySnapBuildResources]::Manifest($exe)
if ($manifest.assembly.assemblyIdentity.version -ne "$Version.0") { throw 'Embedded manifest version mismatch.' }
if ($manifest.assembly.trustInfo.security.requestedPrivileges.requestedExecutionLevel.level -ne 'asInvoker') { throw 'Application must not request elevation.' }
$updateHost = Join-Path $Directory 'UpdateHost\SnappySnap.UpdateHost.exe'
if (!(Test-Path -LiteralPath $updateHost) -or (Get-Item $updateHost).VersionInfo.FileVersion.Trim() -ne "$Version.0") { throw 'UpdateHost missing or version mismatch.' }
[xml]$hostManifest = [SnappySnapBuildResources]::Manifest($updateHost)
if ($hostManifest.assembly.assemblyIdentity.version -ne "$Version.0") { throw 'UpdateHost embedded manifest version mismatch.' }
if ($hostManifest.assembly.trustInfo.security.requestedPrivileges.requestedExecutionLevel.level -ne 'asInvoker') { throw 'UpdateHost must not request elevation.' }
$runtime = Get-Content (Join-Path $Directory 'SnappySnap.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!($runtime.runtimeOptions.includedFrameworks.name -contains 'Microsoft.NETCore.App') -or !($runtime.runtimeOptions.includedFrameworks.name -contains 'Microsoft.WindowsDesktop.App')) { throw 'Payload is not self-contained WPF.' }
$catalogs = @(
    @{ Directory = 'ru'; Culture = 'ru' },
    @{ Directory = 'zh-CN'; Culture = 'zh-CN' },
    @{ Directory = 'ja-JP'; Culture = 'ja-JP' },
    @{ Directory = 'es-ES'; Culture = 'es-ES' }
)
foreach ($catalog in $catalogs) {
    $resourcePath = Join-Path $Directory (Join-Path $catalog.Directory 'SnappySnap.Core.resources.dll')
    if (!(Test-Path -LiteralPath $resourcePath)) { throw "$($catalog.Culture) interface resources are missing." }
    $catalogIdentity = [Reflection.AssemblyName]::GetAssemblyName($resourcePath)
    if ($catalogIdentity.CultureName -ne $catalog.Culture -or $catalogIdentity.Version.ToString() -ne "$Version.0") { throw "$($catalog.Culture) interface resource identity mismatch." }
}
foreach ($name in @('hostfxr.dll','coreclr.dll','PresentationFramework.dll','ScreenRecorderLib.dll','e_sqlite3.dll')) {
    if (!(Get-ChildItem -LiteralPath $Directory -Filter $name -Recurse -File)) { throw "Required payload missing: $name" }
}
foreach ($name in @('concrt140.dll','msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll')) {
    $runtime = Join-Path $Directory $name
    if (!(Test-Path -LiteralPath $runtime) -or (Get-Item -LiteralPath $runtime).VersionInfo.FileVersion -ne '14.44.35211.0') { throw "Required app-local VC runtime missing or wrong version: $name" }
}
if (Get-ChildItem $Directory -Recurse -File | Where-Object { $_.Extension -eq '.pdb' -or $_.Name -match '\.Tests?\.|fixture|visual-harness' }) { throw 'Test/debug artifacts found in payload.' }
Write-Output "Payload verified: version $Version, embedded manifest, asInvoker, self-contained runtime, localized resources and native dependencies."
