#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export ENDPOINT="${1:-resources}" V2_PROFILE="${2:-baseline}" MAX_VUS="${3:-2}"
: "${BASE_URL:?Set BASE_URL}" "${ACCESS_TOKEN:?Set ACCESS_TOKEN}"
mkdir -p "$ROOT/results"
PREFIX="$ROOT/results/v2-${ENDPOINT}-${V2_PROFILE}-$(date +%Y%m%d-%H%M%S)"
k6 run --summary-export "$PREFIX-summary.json" --out "json=$PREFIX-raw.json" "$ROOT/tests/advanced.k6.js"
python3 "$ROOT/scripts/report-v2.py" "$PREFIX-summary.json" "$PREFIX-raw.json"
