[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublicSourceDirectory,
    [Parameter(Mandatory)][string]$VersionedExe,
    [string]$TestBuildExe
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = Join-Path $repo ('artifacts/release-tool-tests/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $root | Out-Null
$fixture = Join-Path $root 'repo'
New-Item -ItemType Directory $fixture | Out-Null
Copy-Item -LiteralPath (Join-Path $PublicSourceDirectory 'tools') -Destination $fixture -Recurse
Copy-Item -LiteralPath (Join-Path $PublicSourceDirectory 'Directory.Build.props') -Destination $fixture
$fixtureVersion = (Get-Item -LiteralPath $VersionedExe).VersionInfo.FileVersion.Trim() -replace '\.0$', ''
$fixturePropsPath = Join-Path $fixture 'Directory.Build.props'
$fixtureProps = [IO.File]::ReadAllText($fixturePropsPath) -replace '<Version>[^<]+</Version>', "<Version>$fixtureVersion</Version>"
$fixtureProps = $fixtureProps -replace '<TestBuildLabel>[^<]*</TestBuildLabel>', '<TestBuildLabel></TestBuildLabel>'
[IO.File]::WriteAllText($fixturePropsPath, $fixtureProps)
$testKey = [Security.Cryptography.ECDsa]::Create()
$testKey.GenerateKey([Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
New-Item -ItemType Directory -Force (Join-Path $fixture 'src/SnappySnap.Updates') | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'src/SnappySnap.Updates/release-public.pem'), $testKey.ExportSubjectPublicKeyInfoPem())
# A synthetic repository/identity/license for testing only; never the user's public identity.
Set-Content -LiteralPath (Join-Path $fixture 'LICENSE') -Value 'Release tooling test fixture only; not a project license.'
git -C $fixture init -b master | Out-Null
git -C $fixture config user.name 'Release tooling fixture'
git -C $fixture config user.email 'fixture@example.invalid'
git -C $fixture add .
git -C $fixture commit -m 'Synthetic fixture' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize isolated test fixture.' }
$results = [Collections.Generic.List[string]]::new()
function Reject([string]$Name, [scriptblock]$Action, [string]$ExpectedMessage = '') {
    $rejected = $false
    try { & $Action | Out-Null } catch {
        if ($ExpectedMessage -and $_.Exception.Message -cne $ExpectedMessage) { throw }
        $rejected = $true
    }
    if (!$rejected) { throw "Expected rejection: $Name" }
    $results.Add("PASS: $Name")
}
$stableProps = [IO.File]::ReadAllText($fixturePropsPath)
[IO.File]::WriteAllText($fixturePropsPath, $stableProps.Replace('<TestBuildLabel></TestBuildLabel>', '<TestBuildLabel>a</TestBuildLabel>'))
git -C $fixture add Directory.Build.props
git -C $fixture commit -m 'Synthetic test label' | Out-Null
Reject 'test-labelled source cannot be tagged' { & (Join-Path $fixture 'tools/release/New-ReleaseTag.ps1') -Version $fixtureVersion } 'Clear TestBuildLabel and rebuild before public release.'
Reject 'test-labelled source cannot be staged' { & (Join-Path $fixture 'tools/release/Stage-Release.ps1') -InstallerPath missing -SignedCatalogDirectory missing -NotesPath missing -OutputDirectory missing } 'Clear TestBuildLabel and rebuild before public release.'
[IO.File]::WriteAllText($fixturePropsPath, $stableProps)
git -C $fixture add Directory.Build.props
git -C $fixture commit -m 'Synthetic public build' | Out-Null
$audit = Join-Path $fixture 'tools/release/Test-PublicTree.ps1'
$tagger = Join-Path $fixture 'tools/release/New-ReleaseTag.ps1'
$stage = Join-Path $fixture 'tools/release/Stage-Release.ps1'
Reject 'uninitialized export cannot tag its parent repository' { & (Join-Path $PublicSourceDirectory 'tools/release/New-ReleaseTag.ps1') -Version '0.5.1' -WhatIf }
& $audit -Directory $fixture -RequireLicense
$results.Add('PASS: clean allowlisted fixture')
Set-Content -LiteralPath (Join-Path $fixture 'private.log') -Value 'synthetic'
git -C $fixture add -f private.log
Reject 'tracked log is refused' { & $audit -Directory $fixture }
git -C $fixture rm -f private.log | Out-Null
New-Item -ItemType Directory -Force (Join-Path $fixture 'config') | Out-Null
$secretFile = Join-Path $fixture 'config/probe.txt'
[IO.File]::WriteAllText($secretFile, ('gh' + 'p_' + ('X' * 36)))
git -C $fixture add config/probe.txt
Reject 'synthetic credential is refused without echoing its contents' { & $audit -Directory $fixture }
git -C $fixture rm -f config/probe.txt | Out-Null
[xml]$props = Get-Content (Join-Path $fixture 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
& $tagger -Version $version -WhatIf
if (@(git -C $fixture tag --list).Count) { throw 'WhatIf created a tag.' }
& $tagger -Version $version
if ((git -C $fixture cat-file -t "v$version") -ne 'tag') { throw 'Release tag is not annotated.' }
$results.Add('PASS: WhatIf has no tag mutation; explicit tag is annotated')
Reject 'duplicate tag is refused' { & $tagger -Version $version }
Reject 'version mismatch is refused' { & $tagger -Version '99.0.0' }
$branchMessage = "Tag this version from master or release/$version only."
git -C $fixture switch -c feature/test | Out-Null
Reject 'release tag on a feature branch is refused' { & $tagger -Version $version } $branchMessage
git -C $fixture switch -c release/99.0.0 | Out-Null
Reject 'release branch for another version is refused' { & $tagger -Version $version } $branchMessage
git -C $fixture switch --detach | Out-Null
Reject 'detached HEAD is refused' { & $tagger -Version $version } $branchMessage
git -C $fixture switch -c "release/$version" | Out-Null
Set-Content -LiteralPath (Join-Path $fixture 'README.md') -Value 'Synthetic release stabilization commit.'
git -C $fixture add README.md
git -C $fixture commit -m 'Synthetic release fix' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated release branch commit.' }
# Delete only the synthetic fixture tag to exercise actual creation on a release branch.
git -C $fixture tag -d "v$version" | Out-Null
& $tagger -Version $version
if ((git -C $fixture cat-file -t "v$version") -ne 'tag') { throw 'Release branch tag is not annotated.' }
if ((git -C $fixture rev-parse "v$version^{}") -ne (git -C $fixture rev-parse HEAD)) { throw 'Release branch tag targets the wrong commit.' }
$results.Add('PASS: matching release branch creates an annotated tag at its commit')
git -C $fixture switch master | Out-Null
$commit = (git -C $fixture rev-parse HEAD).Trim()
$installer = Join-Path $root "SnappySnap-Setup-$version-x64.exe"
Copy-Item -LiteralPath $VersionedExe -Destination $installer
$proof = [ordered]@{version=$version;sourceCommit=$commit;sourceDirty=$false;bytes=(Get-Item $installer).Length;sha256=(Get-FileHash $installer).Hash}
$proof | ConvertTo-Json | Set-Content -LiteralPath ($installer + '.build.json')
$notes = Join-Path $root 'notes.md'
Set-Content -LiteralPath $notes -Value "SnappySnap $version`nSynthetic packaging verification only."
$catalogDir = Join-Path $root 'signed'
New-Item -ItemType Directory $catalogDir | Out-Null
$catalog = [ordered]@{ version=$version; publishedUtc=[DateTimeOffset]::UtcNow.ToString('o'); notes="SnappySnap $version"; fileName=$installer.Substring($installer.LastIndexOf([IO.Path]::DirectorySeparatorChar) + 1); size=(Get-Item $installer).Length; sha256=(Get-FileHash $installer).Hash; platform='win-x64'; minimumWindowsBuild=22000 }
$catalogBytes = [Text.Encoding]::UTF8.GetBytes(($catalog | ConvertTo-Json))
[IO.File]::WriteAllBytes((Join-Path $catalogDir 'latest.json'), $catalogBytes)
[IO.File]::WriteAllBytes((Join-Path $catalogDir 'latest.sig'), $testKey.SignData($catalogBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation))
$out = Join-Path $root 'staged'
& $stage -InstallerPath $installer -SignedCatalogDirectory $catalogDir -NotesPath $notes -OutputDirectory $out
if (@(Get-ChildItem $out -File).Count -ne 6) { throw 'Unexpected staged release contents.' }
$publicManifest = Get-Content (Join-Path $out 'release-manifest.json') -Raw
if ($publicManifest -match '[A-Za-z]:\\|payload|sourceDirty') { throw 'Public manifest contains local metadata.' }
$results.Add('PASS: exactly six staged files, no private build path')
Reject 'existing release directory is refused' { & $stage -InstallerPath $installer -SignedCatalogDirectory $catalogDir -NotesPath $notes -OutputDirectory $out }
Set-Content -LiteralPath $notes -Value "$version DRAFT"
Reject 'draft notes are refused' { & $stage -InstallerPath $installer -SignedCatalogDirectory $catalogDir -NotesPath $notes -OutputDirectory (Join-Path $root 'draft') }
Set-Content -LiteralPath $notes -Value "SnappySnap $version"
$proof.sourceCommit = '0' * 40
$proof | ConvertTo-Json | Set-Content -LiteralPath ($installer + '.build.json')
Reject 'stale source provenance is refused' { & $stage -InstallerPath $installer -SignedCatalogDirectory $catalogDir -NotesPath $notes -OutputDirectory (Join-Path $root 'stale') }
$proof.sourceCommit = $commit; $proof.sha256 = '0' * 64
$proof | ConvertTo-Json | Set-Content -LiteralPath ($installer + '.build.json')
Reject 'mismatched installer hash is refused' { & $stage -InstallerPath $installer -SignedCatalogDirectory $catalogDir -NotesPath $notes -OutputDirectory (Join-Path $root 'hash') }
$proof.sha256 = (Get-FileHash $installer).Hash; $proof.sourceDirty = $true
$proof | ConvertTo-Json | Set-Content -LiteralPath ($installer + '.build.json')
Reject 'installer built from dirty source is refused' { & $stage -InstallerPath $installer -SignedCatalogDirectory $catalogDir -NotesPath $notes -OutputDirectory (Join-Path $root 'dirty') }
$proof.sourceDirty = $false; $proof | ConvertTo-Json | Set-Content -LiteralPath ($installer + '.build.json')
$savedSignature = [IO.File]::ReadAllBytes((Join-Path $catalogDir 'latest.sig'))
[IO.File]::WriteAllBytes((Join-Path $catalogDir 'latest.sig'), (New-Object byte[] 64))
Reject 'invalid catalog signature is refused' { & $stage -InstallerPath $installer -SignedCatalogDirectory $catalogDir -NotesPath $notes -OutputDirectory (Join-Path $root 'signature') }
[IO.File]::WriteAllBytes((Join-Path $catalogDir 'latest.sig'), $savedSignature)
$testKey.Dispose()
if ($TestBuildExe) {
    Copy-Item -LiteralPath $TestBuildExe -Destination $installer -Force
    Reject 'renamed test executable cannot be staged as public' { & $stage -InstallerPath $installer -SignedCatalogDirectory $catalogDir -NotesPath $notes -OutputDirectory (Join-Path $root 'renamed-test') } 'Test installers cannot be staged for public release.'
}
$results | Set-Content -LiteralPath (Join-Path $root 'results.txt')
$results
Write-Output "Evidence: $root. Fixture tags/identity are isolated; no remote or installer execution."
