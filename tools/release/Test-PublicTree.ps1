[CmdletBinding()]
param(
    [string]$Directory = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [switch]$RequireLicense
)
$ErrorActionPreference = 'Stop'
$Directory = (Resolve-Path -LiteralPath $Directory).Path
$allowed = Get-Content (Join-Path $PSScriptRoot 'public-files.json') -Raw | ConvertFrom-Json
if (Test-Path -LiteralPath (Join-Path $Directory '.git')) {
    $files = @(git -C $Directory -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked source.' }
} else {
    $files = @(Get-ChildItem -LiteralPath $Directory -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($Directory, $_.FullName).Replace('\','/') })
}
$findings = [Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $listed = @($allowed | Where-Object { if ($_.EndsWith('/')) { $file.StartsWith($_, [StringComparison]::Ordinal) } else { $file -ceq $_ } }).Count -gt 0
    if (!$listed) { $findings.Add("Not in publication allowlist: $file"); continue }
    if ($file -match '(^|/)(bin|obj|artifacts|\.vs|\.ssh|\.secrets)/|\.(exe|dll|pdb|msi|zip|7z|db|sqlite3?|log|dpapi|pfx|p12|key)$|(^|/)\.env($|\.)|\.local\.' -or
        ($file.EndsWith('.pem') -and $file -cne 'src/SnappySnap.Updates/release-public.pem')) { $findings.Add("Private/generated file: $file"); continue }
    $item = Get-Item -LiteralPath (Join-Path $Directory $file)
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $findings.Add("Linked file requires review: $file"); continue }
    if ($item.Length -gt 5MB) { $findings.Add("Large source file requires review: $file"); continue }
    if ($file -match '\.(png|jpg|jpeg|ico)$') { continue } # Artwork is reviewed visually, not as text.
    $text = [IO.File]::ReadAllText($item.FullName)
    # Report filenames only: never print a matching credential or personal value.
    if ($text -match '-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----|\bgh[pousr]_[A-Za-z0-9]{30,}\b|\bgithub_pat_[A-Za-z0-9_]{30,}\b|\bAKIA[A-Z0-9]{16}\b|https?://[^\s/]+:[^\s/]+@') { $findings.Add("Potential secret: $file") }
    if ($text -match '(?i)[A-Z]:\\Users\\(?!Public(?:\\|\b)|Default(?:\\|\b)|<)[A-Za-z0-9_.-]+\\|[A-Z]:/Users/(?!Public/|Default/|<)[A-Za-z0-9_.-]+/') { $findings.Add("Personal absolute path: $file") }
}
if ($files.Count -eq 0) { $findings.Add('No source files found.') }
if ($RequireLicense -and !(Test-Path -LiteralPath (Join-Path $Directory 'LICENSE'))) { $findings.Add('Owner-approved LICENSE is required before public release.') }
if ($findings.Count) { $findings | ForEach-Object { Write-Output $_ }; throw "Public source check failed ($($findings.Count) findings)." }
Write-Output "Public source check passed: $($files.Count) files. This bounded scan does not replace history/artwork review."
