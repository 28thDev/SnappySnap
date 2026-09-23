[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot 'SnappySnap.sln'
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed ($LASTEXITCODE)." }
dotnet build $solution --configuration $Configuration --no-restore --nologo -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }
