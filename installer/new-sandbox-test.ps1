[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$packages = Join-Path $repo 'artifacts\installer'
if (!(Test-Path (Join-Path $packages "SnappySnap-Setup-$version-x64.exe"))) { throw 'Build the installer first.' }
$evidence = Join-Path $repo 'artifacts\installer-validation'
New-Item -ItemType Directory -Force $evidence | Out-Null
$config = Join-Path $evidence 'SnappySnap-test.wsb'
$xmlPackages = [Security.SecurityElement]::Escape($packages)
$xmlScripts = [Security.SecurityElement]::Escape($PSScriptRoot)
$xmlEvidence = [Security.SecurityElement]::Escape($evidence)
@"
<Configuration>
  <Networking>Disable</Networking>
  <MappedFolders>
    <MappedFolder><HostFolder>$xmlPackages</HostFolder><SandboxFolder>C:\SnappySnapPackage</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$xmlScripts</HostFolder><SandboxFolder>C:\SnappySnapTests</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$xmlEvidence</HostFolder><SandboxFolder>C:\SnappySnapEvidence</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -NoExit -ExecutionPolicy Bypass -File C:\SnappySnapTests\test-install.ps1 -InstallerPath C:\SnappySnapPackage\SnappySnap-Setup-$version-x64.exe -DisposableProfile -EvidenceDirectory C:\SnappySnapEvidence</Command></LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath $config -Encoding UTF8
Write-Output $config
