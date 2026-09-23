[CmdletBinding()]
param(
    [string]$Compiler = (Join-Path $PSScriptRoot 'artifacts\tools\inno-7.1.0\ISCC.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\installer')
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot '.git'))) { throw 'Build from a repository root, not a source export nested inside another checkout.' }
$sourceCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Build from a Git checkout so package provenance can identify the source.' }
$sourceDirty = @(& git -C $PSScriptRoot status --porcelain).Count -gt 0
if (!(Test-Path -LiteralPath $Compiler)) { throw 'Inno Setup 7.1.0 is required. Run installer/prepare-tools.ps1 explicitly or supply -Compiler.' }
$Compiler = (Resolve-Path -LiteralPath $Compiler).Path
if ((& $Compiler --version) -ne '7.1.0') { throw 'Only Inno Setup 7.1.0 is supported.' }
[xml]$properties = Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props')
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Installer requires a numeric major.minor.patch version.' }
$published = Join-Path $PSScriptRoot "artifacts\releases\$version"
if (Test-Path -LiteralPath $published) { throw "Version $version is already published. Choose the next version: increment minor for a feature or patch for a fix." }
# A fresh directory avoids stale dependencies without deleting arbitrary output paths.
$stage = Join-Path $PSScriptRoot ('artifacts\installer-staging\' + [guid]::NewGuid().ToString('N'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $stage,$output | Out-Null
& (Join-Path $PSScriptRoot 'publish.ps1') -Configuration Release -Output $stage
$app = Join-Path $stage 'SnappySnap.exe'
if ((Get-Item $app).VersionInfo.FileVersion -ne "$version.0") { throw 'Published EXE version mismatch.' }
$manifest = Join-Path $PSScriptRoot 'src\SnappySnap.App\obj\x64\Release\net10.0-windows10.0.19041.0\win-x64\SnappySnap.manifest'
[xml]$manifestXml = Get-Content $manifest
if ($manifestXml.assembly.assemblyIdentity.version -ne "$version.0") { throw 'Manifest version mismatch.' }
$forbidden = @(Get-ChildItem $stage -Recurse -File | Where-Object { $_.Extension -eq '.pdb' })
foreach ($file in $forbidden) { Remove-Item -LiteralPath $file.FullName }
Copy-Item (Join-Path $PSScriptRoot 'docs\DEPENDENCIES.md') (Join-Path $stage 'DEPENDENCIES.md')
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
}
$notices = Join-Path $stage 'notices'
New-Item -ItemType Directory -Force $notices | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'installer\notices\*') $notices
$assets = Get-Content (Join-Path $PSScriptRoot 'src\SnappySnap.App\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
foreach ($package in $assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' }) {
    $relative = $package.Value.path
    $packageRoot = $packageRoots | ForEach-Object { Join-Path $_ $relative } | Where-Object { Test-Path $_ } | Select-Object -First 1
    foreach ($license in $package.Value.files | Where-Object { $_ -match '(^|/)(license[^/]*|.*notices?[^/]*|copying)$' -or $_ -like '*.nuspec' }) {
        $target = Join-Path $notices ($relative.Replace('/','-') + '-' + [IO.Path]::GetFileName($license))
        Copy-Item -LiteralPath (Join-Path $packageRoot $license) -Destination $target
    }
}
# Runtime packs are download dependencies rather than libraries in project.assets.json.
foreach ($pack in $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies | Where-Object { $_.name -match '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64$' }) {
    $packVersion = $pack.version.Trim('[',']').Split(',')[0].Trim()
    $packRoot = $packageRoots | ForEach-Object { Join-Path $_ ($pack.name.ToLowerInvariant() + '/' + $packVersion) } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (!$packRoot) { throw "Runtime pack not found: $($pack.name) $packVersion" }
    Get-ChildItem $packRoot -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $notices ($pack.name + '-' + $_.Name))
    }
}
& (Join-Path $PSScriptRoot 'installer\verify-payload.ps1') -Directory $stage -Version $version
& $Compiler "/DAppVersion=$version" "/DPublishDir=$stage" "/DInstallerOutput=$output" (Join-Path $PSScriptRoot 'installer\SnappySnap.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed ($LASTEXITCODE)." }
$installer = Join-Path $output "SnappySnap-Setup-$version-x64.exe"
if ((Get-Item $installer).VersionInfo.FileVersion.Trim() -ne "$version.0") { throw 'Installer version mismatch.' }
$endCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
if ($endCommit -ne $sourceCommit -or @(& git -C $PSScriptRoot status --porcelain).Count) { $sourceDirty = $true }
$provenance = [ordered]@{
    version = $version
    sourceCommit = $sourceCommit
    sourceDirty = $sourceDirty
    builtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    installer = [IO.Path]::GetFileName($installer)
    bytes = (Get-Item -LiteralPath $installer).Length
    sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    payload = $stage
}
$provenance | ConvertTo-Json | Set-Content -LiteralPath ($installer + '.build.json') -Encoding utf8
Write-Output "Installer: $installer"
