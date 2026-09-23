[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!(Test-Path -LiteralPath (Join-Path $repo '.git'))) { throw 'Export from an initialized repository root.' }
$pending = @(git -C $repo status --porcelain)
if ($LASTEXITCODE -ne 0 -or $pending.Count) { throw 'Commit the reviewed source before exporting; the working tree must be clean.' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Choose a new output directory. Existing files are never overwritten.' }
$archive = $destination + '.zip'
if (Test-Path -LiteralPath $archive) { throw 'Archive already exists; choose a new output directory.' }
$allowed = Get-Content (Join-Path $PSScriptRoot 'public-files.json') -Raw | ConvertFrom-Json
$paths = @($allowed | ForEach-Object { $_.TrimEnd('/') } | Where-Object { Test-Path -LiteralPath (Join-Path $repo $_) })
New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
git -C $repo archive --format=zip "--output=$archive" HEAD -- @paths
if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
Expand-Archive -LiteralPath $archive -DestinationPath $destination
& (Join-Path $PSScriptRoot 'Test-PublicTree.ps1') -Directory $destination
Write-Output "Prepared source only: $destination"
Write-Output 'No Git history, remote, tag, installer or publication was created. Review before initializing the public repository.'
