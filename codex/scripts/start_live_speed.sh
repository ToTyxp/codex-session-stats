#!/usr/bin/env bash
set -euo pipefail

PORT="${1:-9222}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
PYTHON="${CODEX_SPEED_PYTHON:-python3}"
source "${SCRIPT_DIR}/port_utils.sh"
REQUESTED_PORT="${PORT}"
LAST_RELAUNCH_PID=""
LAST_DEFERRED_PID=""

if [[ ! -x "${ROOT_DIR}/bin/AgentSpeedCodexMeter" ]]; then
  echo "Run setup.sh first: the Agent Speed meter executable is missing." >&2
  exit 1
fi

# One service owns the injector; launchd already prevents duplicate jobs.
mkdir -p "${ROOT_DIR}/runtime"
if [[ -f "${ROOT_DIR}/runtime/renderer-port" ]]; then
  recorded_port="$(cat "${ROOT_DIR}/runtime/renderer-port")"
  if [[ "${recorded_port}" =~ ^[0-9]+$ ]] && codex_monitor_devtools_owned "${recorded_port}"; then
    PORT="${recorded_port}"
  fi
fi
initial_pid="$(codex_monitor_app_pid 2>/dev/null || true)"
if [[ -n "${initial_pid}" ]] && ! codex_monitor_devtools_ready "${PORT}"; then
  LAST_DEFERRED_PID="${initial_pid}"
  echo "Installed. Waiting for the next normal quit/reopen to enable the local renderer connection."
fi
while true; do
  until codex_monitor_app_running; do sleep 5; done
  if ! codex_monitor_devtools_ready "${PORT}"; then
    observed_pid="$(codex_monitor_app_pid 2>/dev/null || true)"
    if [[ -n "${observed_pid}" && ( "${observed_pid}" == "${LAST_RELAUNCH_PID}" || "${observed_pid}" == "${LAST_DEFERRED_PID}" ) ]]; then
      sleep 5
      continue
    fi
    PORT="$(codex_monitor_resolve_port "${REQUESTED_PORT}")"
    echo "Preparing local renderer connection; a normal app restart may be deferred by an active response."
    if ! "${SCRIPT_DIR}/reopen_codex_with_debug.sh" "${PORT}"; then
      LAST_DEFERRED_PID="${observed_pid}"
      echo "Restart deferred by the running app. The next normal quit/reopen will activate the badge."
      sleep 5
      continue
    fi
    LAST_RELAUNCH_PID="$(codex_monitor_app_pid 2>/dev/null || true)"
    if ! codex_monitor_wait_for_devtools "${PORT}" 30; then
      sleep 5
      continue
    fi
  fi
  printf '%s\n' "${PORT}" > "${ROOT_DIR}/runtime/renderer-port"
  "${PYTHON}" "${SCRIPT_DIR}/live_speed_injector.py" --port "${PORT}" --quiet || true
  sleep 3
done
