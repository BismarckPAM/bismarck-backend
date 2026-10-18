# Bismarck Advanced Load & Performance Testing V2

**Adds to V1, does not replace previously passed test results.** All existing scripts remain intact.

## Improvements
- Four real **read-only** authenticated endpoints selectable individually or as an alternating mix.
- Four short profiles: baseline (50s), ramp (60s), burst (45s), soak (150s).
- 4 endpoints × 4 profiles = **16 meaningful scenario combinations** (not yet executed).
- V1 Smoke / Load / Stress / Spike / Endurance scripts retained unchanged.
- Per-endpoint tagged latency, p95 threshold, p99/avg/max, non-200 errors and offline HTML reports.
- Hard **10 VU cap**, independent of requested count. Start with 2 VUs; do not increase on Azure without verifying budget/health.
- No write operations. No passwords/tokens in the ZIP or Git logs.

## Windows: exact commands
Open PowerShell in extracted project directory:
```powershell
k6 version
python --version
$env:BASE_URL='https://gateway.mangopond-f3cfcd27.southeastasia.azurecontainerapps.io'
# Set your test account access token privately; NEVER share it or save it to the repo
$env:ACCESS_TOKEN='PASTE_YOUR_PRIVATE_TEST_TOKEN_HERE'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\scripts\run-v2.ps1' -Endpoint resources -Profile baseline -VUs 2
```
The command saves `results/v2-*-summary.json`, `results/v2-*-raw.json`, and `results/v2-*-report.html`.
Open resulting `*-report.html` file in Chrome. Take screenshots of terminal THRESHOLDS, HTML report summary and per-endpoint table **with credentials hidden**.

### Suggested order
1. First run `resources baseline 2` and inspect failures.
2. Then individually `approvals`, `analytics`, `notifications` baseline with 2 VUs.
3. Only if each passes, test `all ramp 5`, `all burst 5`, `all soak 5`.
4. More scenario combinations are optional; avoid running all at once against shared Azure infrastructure.

### Run command template
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\scripts\run-v2.ps1' -Endpoint analytics -Profile baseline -VUs 2
```

## Previous V1 experiments
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\scripts\run-tests.ps1' smoke
```
V1 metrics were collected 10 Oct 2026: Smoke 3, Load 135, Stress 337, Spike 258, Endurance 608 requests, all threshold PASS. Those are **historical real runs**, not V2 results. V2 is unexecuted until you run it.

## Integration
- `scripts/run-v2.ps1` is suitable for a group unified task runner on Windows.
- `scripts/run-v2.sh` works for bash runners.
- Workflow `.github/workflows/k6-manual.yml` is **manual only**; configure repository secrets `K6_BASE_URL`, `K6_ACCESS_TOKEN` before use. Do not enable scheduled/concurrent high-load jobs on shared Azure.

## Tools
k6 v2.2.0 (previously installed), Python 3 for optional HTML report generator. Run only against authorized project environment.

## Caveats
- HTTPS end-to-end observations include external client + gateway + microservice path; without Azure App Insights/CPU metrics, do not claim proof of bottleneck cause.
- Read-only endpoints may have different access permissions for test users. HTTP 403/401 are valid failures to investigate (not silently accepted).
- API cache, test-user authorization and deployment state can affect results. Tests do not measure writes, all workflows or maximum scaling capacity.
- Never commit raw outputs if they may include sensitive URLs/tags; `results/` is gitignored.
