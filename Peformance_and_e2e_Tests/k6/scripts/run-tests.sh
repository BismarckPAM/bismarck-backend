#!/usr/bin/env bash
set -euo pipefail
TEST="${1:-smoke}"
case "$TEST" in smoke|load|stress|spike|endurance) ;; *) echo 'Choose smoke/load/stress/spike/endurance' >&2; exit 2;; esac
command -v k6 >/dev/null || { echo 'k6 is not installed' >&2; exit 1; }
: "${BASE_URL:?Set BASE_URL to your API gateway URL}"
if [[ "$TEST" != smoke ]]; then : "${ACCESS_TOKEN:?Set ACCESS_TOKEN to a valid test-user JWT}"; fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/results"
PREFIX="$ROOT/results/${TEST}-$(date +%Y%m%d-%H%M%S)"
k6 run --summary-export "${PREFIX}-summary.json" --out "json=${PREFIX}-raw.json" "$ROOT/tests/${TEST}.test.js"
