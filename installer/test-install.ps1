<# Run ONLY in a fresh disposable Windows account/VM. No SDK or network is required.
   Refuses an existing SnappySnap profile or installation. Leaves evidence and user data intact.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][switch]$DisposableProfile,
    [string]$PreviousInstaller,
    [string]$EvidenceDirectory = ([Environment]::GetFolderPath('Desktop'))
)
$ErrorActionPreference = 'Stop'
if (!$DisposableProfile) { throw 'A disposable test account or VM is required.' }
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\SnappySnap'
$data = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SnappySnap'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{DC69B5B3-5B91-4678-BDA3-C0F0F6AB5102}_is1'
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'SnappySnap.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SnappySnap.lnk'
$captureRoot = Join-Path ([Environment]::GetFolderPath('MyPictures')) 'SnappySnap'
if ((Test-Path $root) -or (Test-Path $data) -or (Test-Path $captureRoot) -or (Test-Path $key) -or (Test-Path $shortcut) -or (Test-Path $desktop) -or (Get-ItemProperty $run -Name SnappySnap -ErrorAction SilentlyContinue)) {
    throw 'Existing SnappySnap installation/data/shortcuts/startup found. Use a fresh disposable profile.'
}
$evidence = Join-Path $EvidenceDirectory ('SnappySnap-Installer-Test-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory $evidence | Out-Null
Start-Transcript -Path (Join-Path $evidence 'results.txt') | Out-Null
function Assert-That([bool]$condition,[string]$message) { if (!$condition) { throw $message }; Write-Output "PASS: $message" }
function Invoke-Checked([string]$path,[string[]]$arguments,[string]$label,[bool]$expectFailure=$false,[int]$expectedFailureCode=0) {
    $p = Start-Process -FilePath $path -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$p.WaitForExit(120000)) { throw "$label timed out. Inspect the process; it was not force-terminated." }
    Assert-That (($p.ExitCode -eq 0) -ne $expectFailure) "$label (exit $($p.ExitCode))"
    if ($expectFailure -and $expectedFailureCode) { Assert-That ($p.ExitCode -eq $expectedFailureCode) "$label returns required failure code $expectedFailureCode" }
}
function Run-Setup([string]$path,[string]$name,[bool]$fail=$false,[int]$expectedFailureCode=0) {
    Invoke-Checked $path @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=desktopicon',"/LOG=`"$(Join-Path $evidence ($name+'.log'))`"") $name $fail $expectedFailureCode
}
function Run-Uninstall([string]$name) {
    Invoke-Checked (Join-Path $root 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$(Join-Path $evidence ($name+'.log'))`"") $name
}
try {
    $version = ([version](Get-Item $InstallerPath).VersionInfo.FileVersion).ToString(3)
    $first = if ($PreviousInstaller) { (Resolve-Path $PreviousInstaller).Path } else { $InstallerPath }
    Run-Setup $first 'install'
    $app = Join-Path $root 'SnappySnap.exe'
    Assert-That (Test-Path $app) 'Executable installed'
    Assert-That ((Test-Path $shortcut) -and (Test-Path $desktop)) 'Start menu and selected desktop shortcut exist'
    $settingsPath = Join-Path $data 'settings.json'
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    Assert-That (!$settings.general.startWithWindows) 'Fresh installation honors startup OFF'
    Assert-That (!(Get-ItemProperty $run -Name SnappySnap -ErrorAction SilentlyContinue)) 'OFF leaves no startup entry'
    $settings.general.shelfRecentCount = 37
    $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    New-Item -ItemType Directory -Force $captureRoot | Out-Null
    $sentinel = Join-Path $captureRoot 'installer-test-sentinel.txt'
    [IO.File]::WriteAllText($sentinel,'Do not delete captures during upgrade or uninstall.')
    if ($PreviousInstaller) { Run-Setup $InstallerPath 'upgrade' } else { Write-Output 'NOT RUN: cross-version upgrade (supply -PreviousInstaller)' }
    Run-Setup $InstallerPath 'same-version-reinstall'
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    Assert-That ((!$settings.general.startWithWindows) -and ($settings.general.shelfRecentCount -eq 37)) 'User preferences preserved across reinstall/upgrade'
    $before = (Get-FileHash $settingsPath).Hash
    Assert-That ((Get-ItemProperty $key).DisplayVersion -eq $version) 'Installed Apps version matches package'
    Assert-That ((Get-Item $app).VersionInfo.FileVersion -eq "$version.0") 'Executable version matches package'
    # Only delete a named application dependency in this previously verified disposable install directory.
    Remove-Item -LiteralPath (Join-Path $root 'SnappySnap.Editor.dll')
    Run-Setup $InstallerPath 'repair-missing-file'
    Assert-That (Test-Path (Join-Path $root 'SnappySnap.Editor.dll')) 'Same-version install restores missing file'
    Set-ItemProperty $key DisplayVersion '999.0.0'
    try { Run-Setup $InstallerPath 'reject-downgrade' $true }
    finally { Set-ItemProperty $key DisplayVersion $version }
    Assert-That ((Get-FileHash $settingsPath).Hash -eq $before) 'Rejected downgrade does not change profile'
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $marker = [Threading.Mutex]::new($false, ('Global\SnappySnap.Running.' + $sid))
    try { Run-Setup $InstallerPath 'reject-running-app' $true }
    finally { $marker.Dispose() }
    $lockedSettings = [IO.File]::Open($settingsPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
    try { Run-Setup $InstallerPath 'startup-configuration-failure' $true 31 }
    finally { $lockedSettings.Dispose() }
    Assert-That ((Get-FileHash $settingsPath).Hash -eq $before) 'Failed configuration retains original settings'
    Run-Setup $InstallerPath 'retry-after-configuration-failure'
    Invoke-Checked $app @('--configure-startup=on') 'Enable startup'
    Assert-That ((Get-ItemProperty $run).SnappySnap -eq ('"'+$app+'" --tray')) 'Startup points to installed executable'
    Invoke-Checked $app @('--configure-startup=preserve') 'Preserve startup'
    $before = (Get-FileHash $settingsPath).Hash
    Run-Uninstall 'uninstall'
    Assert-That (!(Test-Path $app) -and !(Test-Path $key) -and !(Test-Path $shortcut) -and !(Test-Path $desktop)) 'Application registration and shortcuts removed'
    Assert-That (!(Get-ItemProperty $run -Name SnappySnap -ErrorAction SilentlyContinue)) 'Owned startup entry removed'
    Assert-That ((Test-Path $sentinel) -and (Get-FileHash $settingsPath).Hash -eq $before) 'Captures and settings survive uninstall'
    Run-Setup $InstallerPath 'reinstall-retained-profile'
    Assert-That ((Get-FileHash $settingsPath).Hash -eq $before) 'Reinstall keeps retained profile'
    $otherExe = '"C:\SnappySnap-Other\SnappySnap.exe" --tray'
    Set-ItemProperty $run SnappySnap $otherExe
    Run-Uninstall 'uninstall-unrelated-startup'
    Assert-That ((Get-ItemProperty $run).SnappySnap -eq $otherExe) 'Uninstall does not delete another executable startup entry'
    Remove-ItemProperty $run SnappySnap
    Write-Output 'PASS: automated install/reinstall/removal cycle. Manual capture, sign-in, wizard DPI and failure scenarios remain separate.'
} finally { Stop-Transcript | Out-Null }
