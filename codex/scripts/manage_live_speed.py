#!/usr/bin/env python3
"""Install, inspect or remove the Agent Speed Codex integration."""
from __future__ import annotations

import argparse
import json
import os
import plistlib
import shutil
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
INSTALLED_ROOT = Path.home() / "Library/Application Support/CodexLiveSpeed"
LABEL = "local.codex.live-speed"
DOMAIN = f"gui/{os.getuid()}"
PLIST = Path.home() / "Library/LaunchAgents" / f"{LABEL}.plist"


def launchctl(*args, check=False):
    return subprocess.run(["/bin/launchctl", *args], capture_output=True, text=True, check=check)


def install(port):
    if not 1 <= port <= 65535:
        raise ValueError("Port must be between 1 and 65535")
    binary = ROOT / "bin/AgentSpeedCodexMeter"
    if not binary.exists():
        raise SystemExit("Run scripts/setup.sh before installing.")
    subprocess.run([str(binary), "--help"], check=True, capture_output=True, text=True)
    codex_home = os.environ.get("CODEX_HOME")
    if not codex_home and PLIST.exists():
        with PLIST.open("rb") as handle:
            codex_home = plistlib.load(handle).get("EnvironmentVariables", {}).get("CODEX_HOME")
    # Prepare and validate the complete payload before taking the working
    # service down. A missing license/script or an unlaunchable copy must not
    # leave an otherwise healthy installation stopped.
    documents = ("README.md", "LICENSE-Agent-Speed", "LICENSE-Codex-Monitor", "LICENSE-DeepSeek-Harness", "UPSTREAM.md")
    if ROOT != INSTALLED_ROOT:
        INSTALLED_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix=".install-", dir=INSTALLED_ROOT) as folder:
            staged = Path(folder)
            shutil.copytree(ROOT / "scripts", staged / "scripts",
                            ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
            for filename in documents:
                shutil.copy2(ROOT / filename, staged / filename)
            staged_binary = staged / binary.name
            shutil.copy2(binary, staged_binary)
            subprocess.run([str(staged_binary), "--help"], check=True, capture_output=True, text=True)
            launchctl("bootout", f"{DOMAIN}/{LABEL}")
            shutil.copytree(staged / "scripts", INSTALLED_ROOT / "scripts", dirs_exist_ok=True)
            installed_bin = INSTALLED_ROOT / "bin"
            installed_bin.mkdir(exist_ok=True)
            # A new inode avoids macOS reusing the previous Mach-O signature.
            staged_binary.replace(installed_bin / binary.name)
            for filename in documents:
                shutil.copy2(staged / filename, INSTALLED_ROOT / filename)
    else:
        launchctl("bootout", f"{DOMAIN}/{LABEL}")
    subprocess.run([str(INSTALLED_ROOT / "bin/AgentSpeedCodexMeter"), "--help"],
                   check=True, capture_output=True, text=True, cwd=INSTALLED_ROOT)
    (INSTALLED_ROOT / "runtime").mkdir(exist_ok=True)
    PLIST.parent.mkdir(parents=True, exist_ok=True)
    log = INSTALLED_ROOT / "runtime/monitor.log"
    if log.exists() and log.stat().st_size > 1024 * 1024:
        log.replace(log.with_suffix(".log.1"))
    config = {
        "Label": LABEL,
        "ProgramArguments": ["/bin/bash", str(INSTALLED_ROOT / "scripts/start_live_speed.sh"), str(port)],
        "WorkingDirectory": str(INSTALLED_ROOT),
        "RunAtLoad": True,
        "KeepAlive": True,
        "ProcessType": "Background",
        "ThrottleInterval": 15,
        "StandardOutPath": str(log),
        "StandardErrorPath": str(log),
        "EnvironmentVariables": {
            "HOME": str(Path.home()),
            "PATH": "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin",
            "CODEX_SPEED_PYTHON": sys.executable,
            "PYTHONDONTWRITEBYTECODE": "1",
        },
    }
    if codex_home:
        # launchd does not inherit the installer's custom Codex data directory.
        config["EnvironmentVariables"]["CODEX_HOME"] = str(Path(codex_home).expanduser().resolve())
    with PLIST.open("wb") as handle:
        plistlib.dump(config, handle)
    PLIST.chmod(0o600)
    launchctl("bootstrap", DOMAIN, str(PLIST), check=True)
    launchctl("enable", f"{DOMAIN}/{LABEL}", check=True)
    print(json.dumps({"installed": True, "launchAgent": str(PLIST), "runtime": str(INSTALLED_ROOT), "port": port}))


def remove_badge(port):
    sys.path.insert(0, str(ROOT / "scripts"))
    try:
        from codex_cdp import CDPClient, devtools_targets, is_codex_renderer_target
        targets = devtools_targets(port)
    except (OSError, RuntimeError):
        return  # The app may already be closed; the temporary badge disappears with it.
    for target in targets:
        if not is_codex_renderer_target(target) or not target.get("webSocketDebuggerUrl"):
            continue
        client = None
        try:
            client = CDPClient(target["webSocketDebuggerUrl"])
            client.evaluate("window.__codexLiveSpeedBadge?.destroy()")
        except (OSError, RuntimeError):
            continue
        finally:
            if client:
                client.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["install", "status", "stop", "uninstall"])
    parser.add_argument("--port", type=int)
    args = parser.parse_args()
    if args.port is None:
        args.port = 9222
        if args.command != "install":
            try:
                recorded = int((INSTALLED_ROOT / "runtime/renderer-port").read_text().strip())
                if 1 <= recorded <= 65535:
                    args.port = recorded
            except (OSError, ValueError):
                pass
    if args.command == "install":
        install(args.port)
    elif args.command in {"stop", "uninstall"}:
        launchctl("bootout", f"{DOMAIN}/{LABEL}")
        remove_badge(args.port)
        if args.command == "uninstall":
            PLIST.unlink(missing_ok=True)
        print(json.dumps({"stopped": True, "removed": args.command == "uninstall"}))
    else:
        job = launchctl("print", f"{DOMAIN}/{LABEL}")
        connected = False
        badge = None
        badges = []
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{args.port}/json", timeout=1) as response:
                connected = bool(json.load(response))
        except (OSError, ValueError):
            pass
        if connected:
            sys.path.insert(0, str(ROOT / "scripts"))
            try:
                from codex_cdp import CDPClient, devtools_targets
                from live_speed_injector import renderer_targets
                for target_id, target in renderer_targets(devtools_targets(args.port)).items():
                    client = None
                    try:
                        client = CDPClient(target["webSocketDebuggerUrl"], timeout=1)
                        # Query only this extension's own numeric status, no chat DOM.
                        state = client.evaluate("window.__codexLiveSpeedBadge?.inspect() || null")
                        badges.append({"targetId": target_id, "badge": state})
                    except (OSError, RuntimeError, ValueError):
                        continue  # One closing window must not hide other windows.
                    finally:
                        if client:
                            client.close()
                badge = next((item["badge"] for item in badges if item["badge"]), None)
            except (OSError, RuntimeError, ValueError):
                pass
        print(json.dumps({"installed": PLIST.exists(), "serviceLoaded": job.returncode == 0,
                          "rendererAvailable": connected, "port": args.port, "badge": badge,
                          "badges": badges}))


if __name__ == "__main__":
    main()
