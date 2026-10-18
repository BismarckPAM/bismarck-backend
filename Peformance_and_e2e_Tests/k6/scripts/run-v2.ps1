param(
  [ValidateSet('resources','approvals','analytics','notifications','all')][string]$Endpoint='resources',
  [ValidateSet('baseline','ramp','burst','soak')][string]$Profile='baseline',
  [ValidateRange(1,10)][int]$VUs=2
)
$ErrorActionPreference='Stop'
if (-not (Get-Command k6 -ErrorAction SilentlyContinue)) { throw 'Install k6: winget install --id GrafanaLabs.k6 -e --source winget' }
if (-not $env:BASE_URL) { throw 'Set BASE_URL to the gateway HTTPS URL' }
if (-not $env:ACCESS_TOKEN) { throw 'Set ACCESS_TOKEN privately in the current terminal' }
$env:ENDPOINT=$Endpoint
$env:V2_PROFILE=$Profile
$env:MAX_VUS=[string]$VUs
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'results'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$prefix=Join-Path $out "v2-$Endpoint-$Profile-$stamp"
Write-Host "V2 | endpoint=$Endpoint profile=$Profile maxVUs=$VUs | target=$env:BASE_URL"
& k6 run --summary-export "$prefix-summary.json" --out "json=$prefix-raw.json" (Join-Path $root 'tests/advanced.test.js')
$code=$LASTEXITCODE
if (Test-Path "$prefix-summary.json") {
  & python (Join-Path $root 'scripts/report-v2.py') "$prefix-summary.json" "$prefix-raw.json"
  if ($LASTEXITCODE -ne 0) { Write-Warning 'Python report could not be generated. k6 results remain saved.' }
}
Write-Host "Saved results prefix: $prefix"
if ($code -ne 0) { Write-Warning "k6 failed with exit code $code"; exit $code }
