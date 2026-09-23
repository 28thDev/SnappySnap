[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Coverage,
    [switch]$NoBuild,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot 'SnappySnap.sln'
$testArguments = @('test', $solution, '--configuration', $Configuration, '--no-restore', '--nologo', '-p:Platform=x64')
if ($NoBuild) { $testArguments += '--no-build' }
if ($Coverage) {
    if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $PSScriptRoot ('artifacts/coverage/' + (Get-Date -Format 'yyyyMMdd-HHmmssfff')) }
    $testArguments += @('--settings', (Join-Path $PSScriptRoot 'tests/coverage.runsettings'), '--results-directory', $ResultsDirectory)
}
& dotnet @testArguments
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)." }
if ($Coverage) { & (Join-Path $PSScriptRoot 'tests/summarize-coverage.ps1') -ResultsDirectory $ResultsDirectory }
