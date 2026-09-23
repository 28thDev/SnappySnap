[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Output = (Join-Path $PSScriptRoot 'artifacts\publish\win-x64'),
    [string]$VCRuntimeDirectory = $env:SNAPPYSNAP_VC_RUNTIME
)

$ErrorActionPreference = 'Stop'
# ScreenRecorderLib imports these native runtimes. .NET's *_cor3.dll is not a substitute.
# App-local deployment preserves the offline, non-admin installation contract.
if (!$VCRuntimeDirectory) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install the Visual Studio C++ build tools or set SNAPPYSNAP_VC_RUNTIME to the pinned x64 CRT directory. See docs/public/BUILDING.md.' }
    $visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (!$visualStudio) { throw 'Visual Studio C++ build tools were not found. Set SNAPPYSNAP_VC_RUNTIME; see docs/public/BUILDING.md.' }
    $VCRuntimeDirectory = Join-Path $visualStudio 'VC\Redist\MSVC\14.44.35112\x64\Microsoft.VC143.CRT'
}
$runtimeNames = @('concrt140.dll', 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll')
foreach ($name in $runtimeNames) {
    $file = Get-Item -LiteralPath (Join-Path $VCRuntimeDirectory $name)
    if ($file.VersionInfo.FileVersion -ne '14.44.35211.0') { throw "Expected Microsoft VC runtime 14.44.35211.0: $name" }
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw "Invalid Microsoft signature: $name" }
    $stream = [IO.File]::OpenRead($file.FullName)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        $stream.Position = 0x3c; $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Expected x64 PE runtime: $name" }
    } finally { $reader.Dispose() }
}
$project = Join-Path $PSScriptRoot 'src\SnappySnap.App\SnappySnap.App.csproj'
dotnet restore $project --runtime win-x64 --locked-mode
if ($LASTEXITCODE -ne 0) { throw "Restore failed ($LASTEXITCODE)." }
dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained true --output $Output --no-restore --nologo -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }
foreach ($name in $runtimeNames) { Copy-Item -LiteralPath (Join-Path $VCRuntimeDirectory $name) -Destination (Join-Path $Output $name) }

$hostProject = Join-Path $PSScriptRoot 'src\SnappySnap.UpdateHost\SnappySnap.UpdateHost.csproj'
dotnet restore $hostProject --runtime win-x64 --locked-mode
if ($LASTEXITCODE -ne 0) { throw "UpdateHost restore failed ($LASTEXITCODE)." }
dotnet publish $hostProject --configuration $Configuration --runtime win-x64 --self-contained true --output (Join-Path $Output 'UpdateHost') --no-restore --nologo -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "UpdateHost publish failed ($LASTEXITCODE)." }
