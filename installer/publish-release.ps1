[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$NotesPath,
    [string]$KeyPath = (Join-Path $env:LOCALAPPDATA 'SnappySnap-ReleaseKeys\release-key.dpapi'),
    [string]$InstallerPath,
    [Parameter(Mandatory)][string]$PublishedAppPath
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use PowerShell 7.' }
$repo = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
if ([string]$props.Project.PropertyGroup.TestBuildLabel) { throw 'Clear TestBuildLabel and rebuild before public release.' }
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Version must be numeric major.minor.patch.' }
$name = "SnappySnap-Setup-$version-x64.exe"
if (!$InstallerPath) { $InstallerPath = Join-Path $repo "artifacts\installer\$name" }
foreach ($file in @($InstallerPath, $PublishedAppPath)) {
    if ((Get-Item -LiteralPath $file).VersionInfo.ProductVersion.Trim() -match '^\d+\.\d+\.\d+-[a-z]+(\+|$)') { throw 'Test builds cannot be signed for public release.' }
    if ((Get-Item -LiteralPath $file).VersionInfo.FileVersion.Trim() -ne "$version.0") { throw "Version mismatch: $file" }
}
$notes = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesPath))
if ($notes.Length -gt 12000) { throw 'Release notes exceed 12000 characters.' }
New-Item -ItemType Directory -Force $Destination | Out-Null
$Destination = (Resolve-Path -LiteralPath $Destination).Path
# Serialize publishers; a release filename can never be overwritten, including after a failed publication.
$lock = [IO.File]::Open((Join-Path $Destination '.publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$key = [Security.Cryptography.ECDsa]::Create()
try {
    $target = Join-Path $Destination $name
    if (Test-Path -LiteralPath $target) { throw 'This release version has already been published. Bump the version.' }
    $plain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $KeyPath)), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { $read = 0; $key.ImportPkcs8PrivateKey($plain, [ref]$read) }
    finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($plain) }
    $public = [IO.File]::ReadAllText((Join-Path $repo 'src\SnappySnap.Updates\release-public.pem'))
    $verifier = [Security.Cryptography.ECDsa]::Create()
    try {
        $verifier.ImportFromPem($public)
        if ($key.ExportSubjectPublicKeyInfoPem() -ne $verifier.ExportSubjectPublicKeyInfoPem()) { throw 'Signing key does not match the application trust key.' }
        $stage = Join-Path $Destination ('.staging-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory $stage | Out-Null
        $stagedSetup = Join-Path $stage $name
        Copy-Item -LiteralPath $InstallerPath -Destination $stagedSetup
        $catalog = [ordered]@{ version=$version; publishedUtc=[DateTimeOffset]::UtcNow.ToString('o'); notes=$notes; fileName=$name; size=(Get-Item $stagedSetup).Length; sha256=(Get-FileHash $stagedSetup -Algorithm SHA256).Hash; platform='win-x64'; minimumWindowsBuild=22000 }
        $bytes = [Text.Encoding]::UTF8.GetBytes(($catalog | ConvertTo-Json))
        $signature = $key.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
        if (!$verifier.VerifyData($bytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)) { throw 'Signature self-check failed.' }
        [IO.File]::WriteAllBytes((Join-Path $stage 'latest.json'), $bytes)
        [IO.File]::WriteAllBytes((Join-Path $stage 'latest.sig'), $signature)
        [IO.File]::Move($stagedSetup, $target)
        # Immutable per-release catalog is retained. Current metadata switches only after the package is complete.
        foreach ($file in @('latest.json','latest.sig')) {
            $temporary = Join-Path $Destination ($file + '.' + [guid]::NewGuid().ToString('N') + '.partial')
            [IO.File]::Copy((Join-Path $stage $file), $temporary)
            [IO.File]::Move($temporary, (Join-Path $Destination $file), $true)
        }
        Write-Output "Published signed release $version to $Destination"
    } finally { $verifier.Dispose() }
} finally { $key.Dispose(); $lock.Dispose() }
