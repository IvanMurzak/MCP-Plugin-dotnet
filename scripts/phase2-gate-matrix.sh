#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  mcp-authorize Phase-2 gate (task b8) — runnable isolation/selection/pin/Origin/stdio
#  matrix + cross-language golden-vector parity, invoked as a single command locally or in CI.
#
#  Proves McpPlugin 7.0 end-to-end before the host (c1) and engines (Phase 4) consume it:
#    * live multi-tenant isolation matrix (2 accounts × 2 instances × 2 sessions) with
#      leak-detection — the harness FAILS if cross-account routing/notification leakage is
#      introduced (Phase2Gate* tests in McpPlugin.Server.Tests).
#    * cross-language golden-vector parity — the C# ProjectIdentity reference vectors the TS
#      consumers must reproduce at Phase 4 (ProjectIdentityGoldenVector* in McpPlugin.Tests).
#
#  Usage:   scripts/phase2-gate-matrix.sh            # build + run the whole gate
#           scripts/phase2-gate-matrix.sh --no-build # reuse an existing Release build
#
#  Hermetic: no external authorization server, no multi-process orchestration — a stable CI
#  gate. Run from any directory (resolves the repo root from this script's location).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CONFIGURATION="Release"

BUILD=1
for arg in "$@"; do
  case "$arg" in
    --no-build) BUILD=0 ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

cd "${REPO_ROOT}"

if [[ "${BUILD}" -eq 1 ]]; then
  echo "== restore + build (${CONFIGURATION}) =="
  dotnet restore
  dotnet build --no-restore --configuration "${CONFIGURATION}"
fi

echo ""
echo "== Phase-2 isolation/selection/pin/Origin/stdio matrix (McpPlugin.Server.Tests) =="
dotnet test McpPlugin.Server.Tests/McpPlugin.Server.Tests.csproj \
  --no-build --configuration "${CONFIGURATION}" --verbosity normal \
  --filter "FullyQualifiedName~IsolationMatrix" \
  -- RunConfiguration.TreatNoTestsAsError=true

echo ""
echo "== Golden-vector parity: C# ProjectIdentity reference (McpPlugin.Tests) =="
dotnet test McpPlugin.Tests/McpPlugin.Tests.csproj \
  --no-build --configuration "${CONFIGURATION}" --verbosity normal \
  --filter "FullyQualifiedName~ProjectIdentityGoldenVector" \
  -- RunConfiguration.TreatNoTestsAsError=true

echo ""
echo "== Phase-2 gate: PASS =="
