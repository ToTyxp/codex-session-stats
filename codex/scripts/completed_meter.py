"""One shared Agent Speed process, with independent per-window conversation views.

The Swift backend owns parsing, arithmetic, deduplication and durable history.
This adapter only routes numeric measurements to the matching composer.
"""
from __future__ import annotations

import json
import logging
import math
import os
from pathlib import Path
import subprocess
import threading

LOG = logging.getLogger("codex-live-speed")
ROOT = Path(__file__).resolve().parents[1]


def normalize_thread(value):
    if not isinstance(value, str):
        return None
    value = value.strip().removeprefix("local:")
    return value if value and not value.startswith(("client-new-thread:", "chatgpt:", "remote:", "cloudremote:")) else None


def validated_sample(value):
    if not isinstance(value, dict) or value.get("type") != "sample":
        return None
    if value.get("source") != "agent-speed/1.4.1" or value.get("metric") != "response-wall":
        return None
    thread = normalize_thread(value.get("threadId"))
    if not thread or not isinstance(value.get("hostId"), str) or not value["hostId"]:
        return None
    if not isinstance(value.get("responseId"), str) or not value["responseId"]:
        return None
    for key in ("outputTokens", "reasoningTokens", "startMs", "endMs", "durationMs"):
        if type(value.get(key)) is not int:
            return None
    rate = value.get("rate")
    if type(rate) not in (int, float) or not math.isfinite(rate) or rate <= 0:
        return None
    if not 0 <= value["reasoningTokens"] <= value["outputTokens"] or value["outputTokens"] <= 0:
        return None
    if value["durationMs"] <= 0 or value["endMs"] - value["startMs"] != value["durationMs"]:
        return None
    if value.get("model") is not None and not isinstance(value["model"], str):
        return None
    return {key: value.get(key) for key in (
        "hostId", "responseId", "model", "outputTokens", "reasoningTokens",
        "startMs", "endMs", "durationMs", "rate", "metric", "source",
    )} | {"threadId": thread}


def validated_stats(value):
    if not isinstance(value, dict) or value.get("type") != "stats":
        return None
    if value.get("source") != "agent-speed/1.4.1":
        return None
    thread = normalize_thread(value.get("threadId"))
    if not thread or not isinstance(value.get("hostId"), str) or not value["hostId"]:
        return None
    for key in ("steps", "inputTokens", "cachedInputTokens", "outputTokens", "updatedAtMs"):
        if type(value.get(key)) is not int or value[key] < 0:
            return None
    turns = value.get("turns")
    if turns is not None and (type(turns) is not int or not 0 <= turns <= value["steps"]):
        return None
    if value["cachedInputTokens"] > value["inputTokens"] or type(value.get("complete")) is not bool:
        return None
    return {key: value.get(key) for key in (
        "hostId", "turns", "steps", "inputTokens", "cachedInputTokens", "outputTokens",
        "updatedAtMs", "complete", "source",
    )} | {"threadId": thread}


class CompletedCallStore:
    def __init__(self, binary=None, codex_home=None, cache_file=None):
        self.binary = Path(binary) if binary else ROOT / "bin/AgentSpeedCodexMeter"
        self.codex_home = Path(codex_home or os.environ.get("CODEX_HOME") or Path.home() / ".codex")
        self.cache_file = Path(cache_file) if cache_file else ROOT / "runtime/completed-calls.json"
        self._lock = threading.RLock()
        self._stop = threading.Event()
        self._thread = None
        self._process = None
        self._ready = False
        self._samples = {}
        self._stats = {}
        self._unavailable = {}
        self._views = {}
        self._next_view = 0

    def view(self):
        with self._lock:
            self._next_view += 1
            return CompletedCallView(self, self._next_view)

    def start(self):
        with self._lock:
            if self._thread and self._thread.is_alive():
                return
            self._stop.clear()
            self._thread = threading.Thread(target=self._run, name="agent-speed-meter", daemon=True)
            self._thread.start()

    def stop(self):
        self._stop.set()
        with self._lock:
            process = self._process
        if process:
            self._finish_process(process)
        if self._thread:
            self._thread.join(timeout=4)

    @staticmethod
    def _finish_process(process):
        try:
            if process.stdin:
                process.stdin.close()
        except OSError:
            pass
        try:
            process.wait(timeout=1)
        except subprocess.TimeoutExpired:
            process.terminate()
            try:
                process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=1)

    def _send_select(self, target):
        with self._lock:
            process = self._process
            if not self._ready or not process or not process.stdin or process.stdin.closed:
                return
            command = {"command": "select", "threadId": target[1], "hostId": target[0]}
            try:
                process.stdin.write(json.dumps(command, separators=(",", ":")) + "\n")
                process.stdin.flush()
            except (OSError, ValueError):
                self._ready = False

    def select(self, view, thread, host):
        thread = normalize_thread(thread)
        target = (host or "local", thread) if thread else None
        with self._lock:
            if self._views.get(view) == target and view in self._views:
                return
            self._views[view] = target
        if target:
            self._send_select(target)

    def release(self, view):
        with self._lock:
            self._views.pop(view, None)

    def snapshot(self, view):
        with self._lock:
            target = self._views.get(view)
            sample = self._samples.get(target)
            stats = self._stats.get(target)
            ready = self._ready
            unavailable = self._unavailable.get(target)
            payload = {
                "threadId": target[1] if target else None,
                "hostId": target[0] if target else "local",
                "status": "connected" if ready else "connecting",
                "phase": "waiting", "rate": None, "revision": 0,
                "retained": False, "turnId": None,
                "stats": stats,
            }
            if sample:
                payload.update(sample)
                payload.update(status="connected" if ready and not unavailable else "cached",
                               phase="completed", retained=True,
                               completedAtMs=sample["endMs"], revision=sample["endMs"])
            elif stats:
                payload["status"] = "connected" if ready and not unavailable else "cached"
            elif unavailable:
                payload["status"] = "unavailable"
            return payload

    def ingest(self, event):
        if not isinstance(event, dict):
            return
        if event.get("type") == "ready":
            with self._lock:
                self._ready = True
                targets = set(filter(None, self._views.values()))
            for target in targets:
                self._send_select(target)
            return
        if event.get("type") == "unavailable":
            thread = normalize_thread(event.get("threadId"))
            if thread:
                with self._lock:
                    self._unavailable[(event.get("hostId") or "local", thread)] = True
            return
        if event.get("type") == "stats":
            stats = validated_stats(event)
            if stats is None:
                return
            key = stats["hostId"], stats["threadId"]
            with self._lock:
                old = self._stats.get(key)
                if stats == old:
                    self._unavailable.pop(key, None)
                    return
                if old and stats["updatedAtMs"] <= old["updatedAtMs"]:
                    return
                self._stats[key] = stats
                self._unavailable.pop(key, None)
            return
        sample = validated_sample(event)
        if sample is None:
            return
        key = sample["hostId"], sample["threadId"]
        with self._lock:
            old = self._samples.get(key)
            if sample == old:
                # A successful replay after reconnect is also an availability
                # acknowledgement, even when there is no newer completion.
                self._unavailable.pop(key, None)
                return
            if old and (sample["endMs"] < old["endMs"] or sample["responseId"] == old["responseId"]):
                return
            if old and sample["endMs"] == old["endMs"]:
                return
            self._samples[key] = sample
            self._unavailable.pop(key, None)

    def _run(self):
        delay = 1.0
        while not self._stop.is_set():
            process = None
            try:
                self.cache_file.parent.mkdir(parents=True, exist_ok=True)
                process = subprocess.Popen(
                    [str(self.binary), "--codex-home", str(self.codex_home),
                     "--cache-file", str(self.cache_file), "--host-id", "local"],
                    stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                    text=True, encoding="utf-8", bufsize=1,
                )
                with self._lock:
                    self._process = process
                for line in process.stdout:
                    if self._stop.is_set():
                        break
                    if len(line) > 65536:
                        LOG.warning("Oversized meter event ignored")
                        continue
                    try:
                        event = json.loads(line)
                    except ValueError:
                        LOG.warning("Invalid meter JSON ignored")
                        continue
                    if not isinstance(event, dict):
                        LOG.warning("Invalid meter event ignored")
                        continue
                    self.ingest(event)
                    if event.get("type") == "ready":
                        delay = 1.0
            except OSError as exc:
                LOG.warning("Agent Speed unavailable (%s); retrying", type(exc).__name__)
            finally:
                with self._lock:
                    self._ready = False
                    if self._process is process:
                        self._process = None
                if process:
                    self._finish_process(process)
                    if process.stdout:
                        process.stdout.close()
            self._stop.wait(delay)
            delay = min(15.0, delay * 2)


class CompletedCallView:
    def __init__(self, store, key):
        self.store, self.key = store, key

    def start(self):
        self.store.select(self.key, None, "local")

    def stop(self):
        self.store.release(self.key)

    def select(self, thread_id, host_id="local"):
        self.store.select(self.key, thread_id, host_id)

    def snapshot(self):
        return self.store.snapshot(self.key)
