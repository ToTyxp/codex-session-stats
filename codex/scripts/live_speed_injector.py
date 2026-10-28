#!/usr/bin/env python3
"""Display Agent Speed completed-call measurements in every Codex window."""

from __future__ import annotations

import argparse
import json
import logging
import signal
import threading
import time
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlsplit

from codex_cdp import (
    CDPClient, CDPError, devtools_targets, is_codex_renderer_target,
)
from completed_meter import CompletedCallStore

LOG = logging.getLogger("codex-live-speed")
BADGE_SOURCE = Path(__file__).with_name("speed_badge.js")


def normalize_selection(value):
    if not isinstance(value, dict):
        return None, "local"
    thread_id = value.get("threadId")
    if not isinstance(thread_id, str) or not thread_id.strip():
        return None, "local"
    thread_id = thread_id.strip().removeprefix("local:")
    if thread_id.startswith(("chatgpt:", "remote:", "cloudremote:", "client-new-thread:")):
        return None, "local"
    return thread_id, str(value.get("hostId") or "local")


def renderer_targets(targets):
    """Native windows, including deep links; exclude browser and known utilities."""
    result = {}
    for target in targets:
        url = unquote(str(target.get("url") or "")).lower()
        parsed = urlsplit(str(target.get("url") or "").lower())
        route = parse_qs(parsed.query).get("initialroute", [""])[0]
        utility = urlsplit(route).path.rstrip("/") in {"/avatar-overlay", "/spotlight", "/detached-window"}
        if (target.get("type") != "page" or not is_codex_renderer_target(target)
                or not target.get("webSocketDebuggerUrl")
                or parsed.path != "/index.html"
                or "avatar-overlay" in url or utility):
            continue
        key = str(target.get("id") or target["webSocketDebuggerUrl"])
        result[key] = target
    return result


class RendererSession:
    """Each window owns its own conversation selection and display."""
    def __init__(self, target, source, monitor_factory):
        self.url = target["webSocketDebuggerUrl"]
        self.source = source
        self.monitor = monitor_factory()
        self.stop_event = threading.Event()
        self.thread = threading.Thread(target=self._run, name="codex-speed-window", daemon=True)

    def start(self):
        self.thread.start()

    def stop(self):
        self.stop_event.set()
        self.thread.join(timeout=3.5)

    def _run(self):
        self.monitor.start()
        retry = 1.0
        last_error = None
        try:
            while not self.stop_event.is_set():
                client = None
                try:
                    client = CDPClient(self.url, timeout=2.0)
                    client.evaluate(self.source)
                    retry, last_error = 1.0, None
                    while not self.stop_event.is_set():
                        tick = time.monotonic()
                        encoded = json.dumps(self.monitor.snapshot(), allow_nan=False, separators=(",", ":"))
                        selected = client.evaluate(
                            "(() => {const badge=window.__codexLiveSpeedBadge;"
                            "if(!badge)return false;badge.update(" + encoded + ");"
                            "return badge.selection();})()"
                        )
                        if selected is False:
                            self.monitor.select(None)
                            client.evaluate(self.source)
                        else:
                            self.monitor.select(*normalize_selection(selected))
                        self.stop_event.wait(max(0.0, 0.25 - (time.monotonic() - tick)))
                except (CDPError, OSError, ValueError, TimeoutError) as exc:
                    self.monitor.select(None)
                    error = type(exc).__name__
                    if error != last_error:
                        LOG.warning("Window renderer unavailable (%s); reconnecting", error)
                        last_error = error
                finally:
                    if client:
                        if self.stop_event.is_set():
                            try:
                                client.evaluate("window.__codexLiveSpeedBadge?.destroy()")
                            except (CDPError, OSError, ValueError):
                                pass
                        client.close()
                self.stop_event.wait(retry)
                retry = min(15.0, retry * 2)
        finally:
            self.monitor.stop()


def run(port: int, stop: threading.Event, monitor_factory=None, poll_interval=0.75):
    source = BADGE_SOURCE.read_text(encoding="utf-8")
    sessions = {}
    failures = 0
    store = None
    try:
        while not stop.is_set():
            try:
                targets = renderer_targets(devtools_targets(port))
                if not targets:
                    raise CDPError("No native main windows")
                failures = 0
            except (CDPError, OSError, ValueError):
                failures += 1
                # Discovery can fail while existing renderer sockets remain
                # healthy. Only an authoritative listing may remove a window.
                if failures >= 3:
                    return  # Let the launcher detect a later ordinary app restart.
                stop.wait(poll_interval)
                continue
            for key in list(sessions):
                if key not in targets or sessions[key].url != targets[key]["webSocketDebuggerUrl"]:
                    sessions.pop(key).stop()
            for key, target in targets.items():
                if key not in sessions:
                    if monitor_factory is None:
                        store = CompletedCallStore()
                        store.start()
                        monitor_factory = store.view
                    session = RendererSession(target, source, monitor_factory)
                    sessions[key] = session
                    session.start()
            stop.wait(poll_interval)
    finally:
        for session in sessions.values():
            session.stop()
        if store:
            store.stop()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=9222)
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args()
    logging.basicConfig(level=logging.WARNING if args.quiet else logging.INFO,
                        format="%(asctime)s %(levelname)s %(message)s")
    stop = threading.Event()
    for sig in (signal.SIGTERM, signal.SIGINT):
        signal.signal(sig, lambda *_: stop.set())
    run(args.port, stop)


if __name__ == "__main__":
    main()
