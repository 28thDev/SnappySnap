[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$SignedCatalogDirectory,
    [Parameter(Mandatory)][string]$NotesPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!(Test-Path -LiteralPath (Join-Path $repo '.git'))) { throw 'Stage from an initialized repository root.' }
$commit = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or @(& git -C $repo status --porcelain).Count) { throw 'Release staging requires a clean committed checkout.' }
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
if ([string]$props.Project.PropertyGroup.TestBuildLabel) { throw 'Clear TestBuildLabel and rebuild before public release.' }
$version = [string]$props.Project.PropertyGroup.Version
$name = "SnappySnap-Setup-$version-x64.exe"
$installer = Get-Item -LiteralPath $InstallerPath
if ($installer.VersionInfo.ProductVersion -match '^\d+\.\d+\.\d+-[a-z]+(\+|$)') { throw 'Test installers cannot be staged for public release.' }
if ($installer.Name -cne $name -or $installer.VersionInfo.FileVersion.Trim() -ne "$version.0") { throw 'Installer name/version does not match source.' }
$proof = Get-Content -LiteralPath ($installer.FullName + '.build.json') -Raw | ConvertFrom-Json
if ($proof.testBuildLabel) { throw 'Test installers cannot be staged for public release.' }
$hash = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash
if ($proof.sourceDirty -ne $false -or $proof.sourceCommit -ne $commit -or $proof.version -ne $version -or $proof.sha256 -ne $hash -or $proof.bytes -ne $installer.Length) { throw 'Installer provenance does not match this clean source revision.' }
$catalogPath = Join-Path $SignedCatalogDirectory 'latest.json'
$signaturePath = Join-Path $SignedCatalogDirectory 'latest.sig'
$catalogBytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $catalogPath))
$signatureBytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $signaturePath))
if ($catalogBytes.Length -gt 65536 -or $signatureBytes.Length -ne 64) { throw 'Signed catalog has an invalid size.' }
$public = [IO.File]::ReadAllText((Join-Path $repo 'src/SnappySnap.Updates/release-public.pem'))
$verifier = [Security.Cryptography.ECDsa]::Create()
try {
    $verifier.ImportFromPem($public)
    if ($verifier.KeySize -ne 256 -or !$verifier.VerifyData($catalogBytes, $signatureBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)) { throw 'Release catalog signature is invalid.' }
} finally { $verifier.Dispose() }
# ConvertFrom-Json preserves property names from the signed bytes.
$signed = [Text.Encoding]::UTF8.GetString($catalogBytes) | ConvertFrom-Json
if ($signed.version -cne $version -or $signed.fileName -cne $name -or $signed.size -ne $installer.Length -or $signed.sha256 -ne $hash -or $signed.platform -cne 'win-x64') { throw 'Signed catalog does not match the staged installer.' }
$notes = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesPath))
if ($notes -notmatch [regex]::Escape($version) -or $notes -match '(?im)\b(DRAFT|TODO|TBD)\b|\[ \]') { throw 'Finalize release notes for this exact version before staging.' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Choose a fresh release directory; assets are never overwritten.' }
New-Item -ItemType Directory -Path $destination | Out-Null
Copy-Item -LiteralPath $installer.FullName -Destination (Join-Path $destination $name)
Copy-Item -LiteralPath $catalogPath -Destination (Join-Path $destination 'latest.json')
Copy-Item -LiteralPath $signaturePath -Destination (Join-Path $destination 'latest.sig')
[IO.File]::WriteAllText((Join-Path $destination 'RELEASE_NOTES.md'), $notes, [Text.UTF8Encoding]::new($false))
"$($hash.ToLowerInvariant())  $name" | Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Encoding ascii
# Public manifest deliberately excludes machine paths and the private build sidecar.
[ordered]@{ version=$version; sourceCommit=$commit; installer=$name; bytes=$installer.Length; sha256=$hash; signatureStatus=[string](Get-AuthenticodeSignature -LiteralPath $installer.FullName).Status } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'release-manifest.json') -Encoding utf8
Write-Output "Prepared six local release files in $destination. Nothing was uploaded or tagged."
