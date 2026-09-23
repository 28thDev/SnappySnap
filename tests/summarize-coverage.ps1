[CmdletBinding()]
param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter '*.cobertura.xml')
if ($reports.Count -eq 0) { throw 'No Cobertura reports found; coverage collection did not succeed.' }
$modules = @{}
foreach ($report in $reports) {
    [xml]$xml = Get-Content -LiteralPath $report.FullName -Raw
    foreach ($package in $xml.coverage.packages.package) {
        $name = [string]$package.name
        if ($name -notlike 'SnappySnap*' -or $name -like '*Tests') { continue }
        if (-not $modules.ContainsKey($name)) { $modules[$name] = @{} }
        foreach ($class in $package.classes.class) {
            foreach ($line in $class.lines.line) {
                $key = [string]$class.filename + ':' + [string]$line.number
                $modules[$name][$key] = $modules[$name][$key] -eq $true -or [int]$line.hits -gt 0
            }
        }
    }
}
if ($modules.Count -eq 0) { throw 'No product modules found in coverage reports.' }
$rows = foreach ($name in ($modules.Keys | Sort-Object)) {
    $valid = $modules[$name].Count
    $covered = @($modules[$name].Values | Where-Object { $_ }).Count
    [pscustomobject]@{ Module = $name; CoveredLines = $covered; TotalLines = $valid; Percent = [math]::Round(100 * $covered / [math]::Max(1, $valid), 2) }
}
$coveredTotal = ($rows | Measure-Object CoveredLines -Sum).Sum
$validTotal = ($rows | Measure-Object TotalLines -Sum).Sum
$summary = @('# Automated source-line coverage', '', '| Module | Covered / total lines | Coverage |', '| --- | ---: | ---: |')
$summary += $rows | ForEach-Object { '| {0} | {1} / {2} | {3:F2}% |' -f $_.Module, $_.CoveredLines, $_.TotalLines, $_.Percent }
$summary += '| Instrumented product total | {0} / {1} | {2:F2}% |' -f $coveredTotal, $validTotal, (100 * $coveredTotal / $validTotal)
$summary += @('', 'Line locations are deduplicated per module and source file across reports. Tests and third-party modules are excluded.',
    'This is coverage of modules loaded by automated tests. App, Capture and UpdateHost are not exercised by these test projects; the WPF/native harnesses are separate acceptance evidence, not part of this percentage.')
$summary | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'summary.md') -Encoding utf8
$rows | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'summary.json') -Encoding utf8
$rows | Format-Table -AutoSize
