[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$toolRoot = Join-Path $repo 'artifacts\tools\inno-7.1.0'
$download = Join-Path $repo 'artifacts\tools\innosetup-7.1.0-x64.exe'
New-Item -ItemType Directory -Force (Split-Path $download) | Out-Null
if (Test-Path (Join-Path $toolRoot 'ISCC.exe')) {
    if ((& (Join-Path $toolRoot 'ISCC.exe') --version) -ne '7.1.0') { throw 'Unexpected compiler version.' }
    Write-Output (Join-Path $toolRoot 'ISCC.exe')
    return
}
if (!(Test-Path -LiteralPath $download)) {
    Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $download
}
$signature = Get-AuthenticodeSignature $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Pyrsys B.V.') {
    throw 'Inno Setup signature verification failed. Tool was not executed.'
}
# Official portable option: repository-local compiler, no associations, shortcuts or uninstaller registration.
$process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/PORTABLE=1',"/DIR=`"$toolRoot`"", "/LOG=`"$download.log`"") -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Tool extraction failed: $($process.ExitCode)" }
if ((& (Join-Path $toolRoot 'ISCC.exe') --version) -ne '7.1.0') { throw 'Unexpected compiler version.' }
Write-Output (Join-Path $toolRoot 'ISCC.exe')
