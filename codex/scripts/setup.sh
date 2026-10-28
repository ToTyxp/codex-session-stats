#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INTEGRATION_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
REPO_ROOT="$(cd "${INTEGRATION_ROOT}/.." && pwd)"
BUILD_DIR="${AGENT_SPEED_BUILD_DIR:-${REPO_ROOT}/macos/.build-codex}"
export AGENT_SPEED_CODEX_METER_ONLY=1
export CLANG_MODULE_CACHE_PATH="${BUILD_DIR}/module-cache"
export SWIFTPM_MODULECACHE_OVERRIDE="${BUILD_DIR}/module-cache"
xcrun swift build --disable-sandbox --package-path "${REPO_ROOT}/macos" \
  --scratch-path "${BUILD_DIR}" -c release --product AgentSpeedCodexMeter
BIN_DIR="$(xcrun swift build --disable-sandbox --package-path "${REPO_ROOT}/macos" \
  --scratch-path "${BUILD_DIR}" -c release --show-bin-path)"
mkdir -p "${INTEGRATION_ROOT}/bin"
STAGED_BINARY="$(mktemp "${INTEGRATION_ROOT}/bin/.AgentSpeedCodexMeter.XXXXXX")"
trap 'rm -f "${STAGED_BINARY}"' EXIT
cp "${BIN_DIR}/AgentSpeedCodexMeter" "${STAGED_BINARY}"
chmod +x "${STAGED_BINARY}"
mv -f "${STAGED_BINARY}" "${INTEGRATION_ROOT}/bin/AgentSpeedCodexMeter"
echo "Built Agent Speed Codex meter. Install with: python3 ${SCRIPT_DIR}/manage_live_speed.py install"
