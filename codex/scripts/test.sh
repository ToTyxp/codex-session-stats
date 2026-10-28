#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
export PYTHONDONTWRITEBYTECODE=1
python3 -m unittest discover -s "${REPO_ROOT}/codex/tests" -p 'test_*.py' -q
node --test "${REPO_ROOT}/codex/tests/test_speed_badge.mjs"

export AGENT_SPEED_CODEX_METER_ONLY=1
BUILD_DIR="${AGENT_SPEED_BUILD_DIR:-${REPO_ROOT}/macos/.build-codex}"
export CLANG_MODULE_CACHE_PATH="${BUILD_DIR}/module-cache"
export SWIFTPM_MODULECACHE_OVERRIDE="${BUILD_DIR}/module-cache"
SWIFT_ARGS=()
DEVELOPER_DIR_PATH="$(xcode-select -p)"
if [[ "${DEVELOPER_DIR_PATH}" == */CommandLineTools ]]; then
  FRAMEWORKS="${DEVELOPER_DIR_PATH}/Library/Developer/Frameworks"
  SWIFT_ARGS=(-Xswiftc -F -Xswiftc "${FRAMEWORKS}" -Xswiftc -Xfrontend
    -Xswiftc -disable-cross-import-overlays -Xlinker -F -Xlinker "${FRAMEWORKS}"
    -Xlinker -rpath -Xlinker "${FRAMEWORKS}")
fi
swift test --disable-sandbox --disable-xctest --package-path "${REPO_ROOT}/macos" \
  --scratch-path "${BUILD_DIR}" --cache-path "${BUILD_DIR}/swiftpm-cache" \
  "${SWIFT_ARGS[@]}" \
  --filter 'CodexMeterTests|CodexSessionStatsTests|ParserFixtureTests|ParserBehaviorTests|JSONScannerTests|FileTailerTests|CompressedStreamReaderTests'
