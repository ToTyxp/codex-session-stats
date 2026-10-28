#!/usr/bin/env bash
# Regenerates icon-1024.png, AgentSpeed.icns, AgentSpeed.ico and icon-windows-256.png in this directory.
set -euo pipefail

ICON_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/agent-speed-icon.XXXXXX")"
trap 'rm -rf "$WORK_DIR"' EXIT

command -v swift >/dev/null 2>&1 || { echo "error: swift not found (install Xcode or the Command Line Tools)" >&2; exit 1; }
command -v iconutil >/dev/null 2>&1 || { echo "error: iconutil not found (macOS only)" >&2; exit 1; }

echo "==> Rendering icon artwork"
swift "$ICON_DIR/generate-icon.swift" "$ICON_DIR" "$WORK_DIR"

echo "==> Packing AgentSpeed.icns"
iconutil --convert icns --output "$ICON_DIR/AgentSpeed.icns" "$WORK_DIR/AgentSpeed.iconset"

echo "==> Done"
for file in icon-1024.png AgentSpeed.icns AgentSpeed.ico icon-windows-256.png; do
    printf '    %-22s %s\n' "$file" "$(du -h "$ICON_DIR/$file" | awk '{print $1}')"
done
