# Seeds a realistic event set directly into the analytics DB, then asserts the
# live API returns the canonical BIS-402 numbers. Run verify-live.ps1 first.
param(
    [string]$BaseUrl = 'http://localhost:5290',
    [string]$SigningKey = 'verify-only-not-a-real-key-1234567890'
)

$ErrorActionPreference = 'Stop'

function New-Bis402Token {
    param([string]$Key, [string]$Issuer = 'Identity.Service', [string]$Audience = 'Bismarck.Services')
    function Encode([object]$o) {
        $json = $o | ConvertTo-Json -Compress -Depth 5
        [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    $h = Encode @{ alg = 'HS256'; typ = 'JWT' }
    $p = Encode @{
        sub = 'security-admin'; name = 'security-admin'; role = 'Admin'
        iss = $Issuer; aud = $Audience
        exp = [DateTimeOffset]::UtcNow.AddHours(1).ToUnixTimeSeconds()
        iat = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    }
    $signer = New-Object System.Security.Cryptography.HMACSHA256
    $signer.Key = [Text.Encoding]::UTF8.GetBytes($Key)
    $sig = [Convert]::ToBase64String($signer.ComputeHash([Text.Encoding]::UTF8.GetBytes("$h.$p"))).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    return "$h.$p.$sig"
}

$headers = @{ Authorization = "Bearer $(New-Bis402Token -Key $SigningKey)" }

function Get-Json([string]$path) {
    $r = Invoke-WebRequest -Uri "$BaseUrl$path" -Headers $headers -UseBasicParsing -TimeoutSec 30
    return $r.Content | ConvertFrom-Json
}

# ---------------------------------------------------------------- expectations
# 2 requests, 1 approval, 2 denials (AccessDenied + ApprovalRejected),
# 2 revocations (expiry + admin JIT manual revoke).
# AccessRequested / AccessGranted must NOT be counted.
$range = 'startDate=2026-10-01T00:00:00Z&endDate=2026-10-31T23:59:59Z'

$summary = Get-Json "/api/analytics/summary?$range"
Write-Host "`n=== LIVE METRIC VERIFICATION (Oct 2026) ===`n" -ForegroundColor Cyan
Write-Host ("Summary totals        : requests={0} approvals={1} denials={2} revocations={3}" -f `
    $summary.totals.requests, $summary.totals.approvals, $summary.totals.denials, $summary.totals.revocations)
Write-Host ("Trend buckets         : {0}" -f $summary.trend.Count)
foreach ($p in $summary.trend) {
    Write-Host ("  {0}  req={1} app={2} den={3} rev={4}" -f $p.date, $p.requests, $p.approvals, $p.denials, $p.revocations) -ForegroundColor DarkGray
}

$top = Get-Json "/api/analytics/top-resources?$range"
Write-Host ("`nTop resources        : totalRequests={0}" -f $top.totalRequests)
foreach ($i in $top.items) {
    Write-Host ("  #{0} {1} ({2}) = {3} ({4}%)" -f $i.rank, $i.resourceName, $i.resourceId, $i.requestCount, $i.percentage) -ForegroundColor DarkGray
}

$den = Get-Json "/api/analytics/denial-reasons?$range"
Write-Host ("`nDenial reasons        : totalDenials={0}" -f $den.totalDenials)
foreach ($i in $den.items) {
    Write-Host ("  {0} = {1} ({2}%)" -f $i.reason, $i.count, $i.percentage) -ForegroundColor DarkGray
}

# ------------------------------------------------------------------ assertions
$failures = 0
function Assert-Equal($name, $actual, $expected) {
    if ($actual -eq $expected) {
        Write-Host ("  PASS  {0} = {1}" -f $name, $actual) -ForegroundColor Green
    } else {
        Write-Host ("  FAIL  {0} = {1} (expected {2})" -f $name, $actual, $expected) -ForegroundColor Red
        $script:failures++
    }
}

Write-Host "`n--- assertions ---" -ForegroundColor Cyan
Assert-Equal 'requests'     $summary.totals.requests    2
Assert-Equal 'approvals'    $summary.totals.approvals   1
Assert-Equal 'denials'      $summary.totals.denials     2
Assert-Equal 'revocations'  $summary.totals.revocations 2
Assert-Equal 'trend buckets' $summary.trend.Count        5
Assert-Equal 'top totalRequests' $top.totalRequests     2
Assert-Equal 'denial totalDenials' $den.totalDenials   2

# Zero-data range must be a safe 200.
$empty = Get-Json '/api/analytics/summary?startDate=2020-01-01T00:00:00Z&endDate=2020-01-31T00:00:00Z'
Assert-Equal 'empty-range requests' $empty.totals.requests 0
Assert-Equal 'empty-range trend'    $empty.trend.Count     0

# DoD invariant: totals must equal the sum of the trend buckets.
$sumReq = ($summary.trend | Measure-Object -Property requests    -Sum).Sum
$sumApp = ($summary.trend | Measure-Object -Property approvals   -Sum).Sum
$sumDen = ($summary.trend | Measure-Object -Property denials     -Sum).Sum
$sumRev = ($summary.trend | Measure-Object -Property revocations -Sum).Sum
Assert-Equal 'sum(trend.requests)    == totals.requests'    $sumReq $summary.totals.requests
Assert-Equal 'sum(trend.approvals)   == totals.approvals'   $sumApp $summary.totals.approvals
Assert-Equal 'sum(trend.denials)     == totals.denials'     $sumDen $summary.totals.denials
Assert-Equal 'sum(trend.revocations) == totals.revocations' $sumRev $summary.totals.revocations

# Percentages must be mathematically correct: 1 of 2 -> 50.00
Assert-Equal 'top item percentage'    $top.items[0].percentage 50.00
Assert-Equal 'denial item percentage' $den.items[0].percentage 50.00

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures assertion(s) FAILED" -ForegroundColor Red; exit 1 }
else { Write-Host "All live assertions PASSED" -ForegroundColor Green }
