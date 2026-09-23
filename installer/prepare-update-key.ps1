[CmdletBinding()]
param([string]$KeyPath = (Join-Path $env:LOCALAPPDATA 'SnappySnap-ReleaseKeys\release-key.dpapi'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use PowerShell 7.' }
$repo = Split-Path $PSScriptRoot -Parent
$keyPathFull = [IO.Path]::GetFullPath($KeyPath)
if ($keyPathFull.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Private key must be outside the repository.' }
$publicPath = Join-Path $repo 'src\SnappySnap.Updates\release-public.pem'
if ((Test-Path -LiteralPath $KeyPath) -or (Test-Path -LiteralPath $publicPath)) { throw 'A key already exists. Never rotate an update trust key implicitly.' }
New-Item -ItemType Directory -Force (Split-Path $KeyPath) | Out-Null
$key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve]::CreateFromFriendlyName("nistP256"))
try {
    $plain = $key.ExportPkcs8PrivateKey()
    try { $encrypted = [Security.Cryptography.ProtectedData]::Protect($plain, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser) }
    finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($plain) }
    [IO.File]::WriteAllBytes($KeyPath, $encrypted)
    [IO.File]::WriteAllText($publicPath, $key.ExportSubjectPublicKeyInfoPem())
} finally { if ($key) { $key.Dispose() } }
Write-Output "Created user-protected release key outside the repository: $KeyPath"
Write-Output "Public verification key: $publicPath"
