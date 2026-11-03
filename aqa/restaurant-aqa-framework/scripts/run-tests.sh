#!/usr/bin/env bash
set -euo pipefail

aqa_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$aqa_directory"
export AQA_RESULTS_DIR="${AQA_RESULTS_DIR:-$aqa_directory/TestResults}"
mkdir -p "$AQA_RESULTS_DIR"

dotnet test Restaurant.Aqa.csproj \
  --configuration Release \
  --no-build \
  --filter "${AQA_TEST_FILTER:-Category=Smoke}" \
  --results-directory "$AQA_RESULTS_DIR" \
  --logger 'trx;LogFileName=results.trx' \
  --logger 'html;LogFileName=results.html'
