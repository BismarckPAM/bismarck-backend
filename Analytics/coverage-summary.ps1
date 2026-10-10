# Reports combined line coverage for the Analytics implementation from the
# Cobertura files produced by the unit and integration test runs.
# Usage: pwsh -File coverage-summary.ps1 -ResultsDir .\TestResults

param([string]$ResultsDir = ".\TestResults")

$files = Get-ChildItem $ResultsDir -Recurse -Filter coverage.cobertura.xml
if (-not $files) { Write-Error "No coverage.cobertura.xml found under $ResultsDir"; exit 1 }

# Union of line numbers per class across all runs, so a line covered by either
# suite counts as covered.
$covered = @{}
$valid   = @{}

foreach ($file in $files) {
    [xml]$xml = Get-Content $file.FullName
    foreach ($class in $xml.coverage.packages.package.classes.class) {
        $name = $class.name

        # Exclude code that is not part of the Analytics implementation.
        if ($name -like 'Messaging*') { continue }
        if ($name -like '*Migrations*') { continue }
        if ($name -eq 'Program') { continue }
        if ($name -match '/<>|<>c__DisplayClass|<[A-Za-z]+>d__') { continue }

        foreach ($line in $class.lines.line) {
            $key = "$name|$($line.number)"
            if (-not $valid.ContainsKey($key)) { $valid[$key] = $name }
            if ([int]$line.hits -gt 0) { $covered[$key] = $name }
        }
    }
}

# Report per type (the display name before any namespace).
$byType = @{}
foreach ($key in $valid.Keys) {
    $type = ($key -split '\|')[0]
    if (-not $byType.ContainsKey($type)) { $byType[$type] = @(0, 0) }
    $byType[$type][0] += 1
    if ($covered.ContainsKey($key)) { $byType[$type][1] += 1 }
}

$rows = foreach ($type in ($byType.Keys | Sort-Object)) {
    $v = $byType[$type][0]; $c = $byType[$type][1]
    [pscustomobject]@{
        Type    = $type
        Covered = $c
        Lines   = $v
        Percent = if ($v -gt 0) { [math]::Round(100 * $c / $v, 1) } else { 0 }
    }
}

$rows | Format-Table -AutoSize

$totalLines   = ($rows | Measure-Object -Property Lines -Sum).Sum
$totalCovered = ($rows | Measure-Object -Property Covered -Sum).Sum
$percent      = if ($totalLines -gt 0) { [math]::Round(100 * $totalCovered / $totalLines, 2) } else { 0 }

Write-Host ""
Write-Host "ANALYTICS IMPLEMENTATION LINE COVERAGE: $totalCovered / $totalLines = $percent%"
