[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!(Test-Path -LiteralPath (Join-Path $repo '.git'))) { throw 'Tag an initialized repository root, not a source export.' }
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Use a stable numeric version, for example 1.0.0.' }
$branch = & git -C $repo branch --show-current
if ($branch -cne 'master' -and $branch -cne "release/$Version") { throw "Tag this version from master or release/$Version only." }
if (@(& git -C $repo status --porcelain).Count) { throw 'Commit reviewed changes before tagging.' }
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
if ([string]$props.Project.PropertyGroup.TestBuildLabel) { throw 'Clear TestBuildLabel and rebuild before public release.' }
if ([string]$props.Project.PropertyGroup.Version -ne $Version) { throw 'Tag version must match Directory.Build.props.' }
& (Join-Path $PSScriptRoot 'Test-PublicTree.ps1') -Directory $repo -RequireLicense
$tag = "v$Version"
git -C $repo show-ref --verify --quiet "refs/tags/$tag"
if ($LASTEXITCODE -eq 0) { throw "Tag already exists: $tag. Published tags must never move." }
if ($LASTEXITCODE -ne 1) { throw 'Cannot inspect existing tags.' }
git -C $repo var GIT_COMMITTER_IDENT | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Configure the approved public Git identity first.' }
if ($PSCmdlet.ShouldProcess($tag, 'Create a local annotated release tag at HEAD')) {
    git -C $repo tag -a $tag -m "SnappySnap $Version"
    if ($LASTEXITCODE -ne 0) { throw 'Tag creation failed.' }
    Write-Output "Created $tag locally. No push or GitHub release was performed."
}
