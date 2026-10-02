# Live end-to-end verification for BIS-402 against a running container + PostgreSQL.
param(
    [string]$BaseUrl = 'http://localhost:5290',
    [string]$SigningKey = 'verify-only-not-a-real-key-1234567890'
)

$ErrorActionPreference = 'Stop'

function New-Bis402Token {
    param([string]$Key, [string]$Issuer = 'Identity.Service', [string]$Audience = 'Bismarck.Services')

    $header = @{ alg = 'HS256'; typ = 'JWT' }
    $exp = [DateTimeOffset]::UtcNow.AddHours(1)
    $payload = @{
        sub   = 'security-admin'
        name  = 'security-admin'
        role  = 'Admin'
        iss   = $Issuer
        aud   = $Audience
        exp   = $exp.ToUnixTimeSeconds()
        iat   = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' = 'security-admin'
    }

    function Encode([object]$o) {
        $json = $o | ConvertTo-Json -Compress -Depth 5
        [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }

    $h = Encode $header
    $p = Encode $payload
    $signer = New-Object System.Security.Cryptography.HMACSHA256
    $signer.Key = [Text.Encoding]::UTF8.GetBytes($Key)
    $sig = [Convert]::ToBase64String($signer.ComputeHash([Text.Encoding]::UTF8.GetBytes("$h.$p"))).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    return "$h.$p.$sig"
}

$token = New-Bis402Token -Key $SigningKey
$headers = @{ Authorization = "Bearer $token" }

function Invoke-Api([string]$path) {
    try {
        $r = Invoke-WebRequest -Uri "$BaseUrl$path" -Headers $headers -UseBasicParsing -TimeoutSec 30
        return [pscustomobject]@{ Status = $r.StatusCode; Body = $r.Content }
    } catch {
        $status = $_.Exception.Response.StatusCode.value__
        $body = ''
        try {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $body = $reader.ReadToEnd()
        } catch { }
        return [pscustomobject]@{ Status = $status; Body = $body }
    }
}

Write-Host "`n=== BIS-402 LIVE VERIFICATION ===`n" -ForegroundColor Cyan

# 1. Swagger
$swagger = Invoke-WebRequest -Uri "$BaseUrl/swagger/index.html" -UseBasicParsing -TimeoutSec 30
Write-Host ("Swagger UI            : {0}  (expected 200)" -f $swagger.StatusCode)

# 2. Unauthenticated -> 401
try {
    Invoke-WebRequest -Uri "$BaseUrl/api/analytics/summary" -UseBasicParsing -TimeoutSec 30 | Out-Null
    Write-Host "No JWT                : 200  (UNEXPECTED)" -ForegroundColor Red
} catch {
    Write-Host ("No JWT                : {0}  (expected 401)" -f $_.Exception.Response.StatusCode.value__)
}

# 3. startDate > endDate -> 400
$r = Invoke-Api '/api/analytics/summary?startDate=2026-12-01T00:00:00Z&endDate=2026-01-01T00:00:00Z'
Write-Host ("startDate > endDate   : {0}  (expected 400)" -f $r.Status)

# 4. Malformed date -> 400
$r = Invoke-Api '/api/analytics/summary?startDate=not-a-date'
Write-Host ("Malformed startDate   : {0}  (expected 400)" -f $r.Status)

# 5. Valid requests -> 200
foreach ($p in @('/api/analytics/summary', '/api/analytics/top-resources', '/api/analytics/denial-reasons')) {
    $r = Invoke-Api $p
    Write-Host ("{0,-22}: {1}  (expected 200)" -f ($p -replace '/api/analytics/', ''), $r.Status)
    if ($r.Status -eq 200) {
        Write-Host ("  {0}" -f $r.Body) -ForegroundColor DarkGray
    }
}
