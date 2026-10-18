param([ValidateSet('smoke','load','stress','spike','endurance')][string]$Test='smoke')
$ErrorActionPreference='Stop'
if (-not (Get-Command k6 -ErrorAction SilentlyContinue)) { throw 'Install k6 first: winget install k6.k6' }
if (-not $env:BASE_URL) { throw 'Set BASE_URL to the API gateway URL (example: $env:BASE_URL="https://your-gateway-host")' }
if ($Test -ne 'smoke' -and -not $env:ACCESS_TOKEN) {throw 'Set ACCESS_TOKEN from a dedicated assignment test user login. Do not commit tokens.'}
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'results'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$prefix=Join-Path $out "$Test-$stamp"
Write-Host "Running $Test against $env:BASE_URL (review safe load limits before proceeding)"
& k6 run --summary-export "$prefix-summary.json" --out "json=$prefix-raw.json" (Join-Path $root "tests/$Test.test.js")
if ($LASTEXITCODE -ne 0) { Write-Warning "k6 exited with $LASTEXITCODE (threshold failed, connectivity or test error). Check output."; exit $LASTEXITCODE }
Write-Host "Saved: $prefix-summary.json and $prefix-raw.json"
