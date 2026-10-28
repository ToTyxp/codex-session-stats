import json
from pathlib import Path
import sys
import threading
import unittest
from unittest.mock import patch

SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPTS))
import live_speed_injector as bridge


def target(key, url="app://-/index.html"):
    return {"id": key, "type": "page", "title": "Codex", "url": url,
            "webSocketDebuggerUrl": "ws://127.0.0.1/" + key}


class ImmediateEvent:
    def __init__(self):
        self.stopped = False

    def is_set(self):
        return self.stopped

    def set(self):
        self.stopped = True

    def wait(self, _timeout):
        return self.stopped


class FakeMonitor:
    def __init__(self):
        self.selections = []
        self.selected = (None, "local")
        self.started = self.stopped = False

    def start(self):
        self.started = True

    def stop(self):
        self.stopped = True

    def select(self, *selection):
        self.selected = selection
        self.selections.append(selection)

    def snapshot(self):
        return {"threadId": self.selected[0], "hostId": self.selected[1],
                "status": "connected", "phase": "writing", "rate": 42.0,
                "revision": 5}


class BridgeTests(unittest.TestCase):
    def test_selection_normalizes_local_and_rejects_non_codex_surfaces(self):
        self.assertEqual(bridge.normalize_selection({"threadId": "local:abc", "hostId": "machine"}), ("abc", "machine"))
        for value in (None, {}, {"threadId": ""}, {"threadId": "chatgpt:abc"},
                      {"threadId": "remote:cloud-task"}, {"threadId": "cloudremote:task"},
                      {"threadId": "local:client-new-thread:pending"}):
            self.assertEqual(bridge.normalize_selection(value), (None, "local"))

    def test_all_main_windows_are_selected_and_utility_pages_are_excluded(self):
        candidates = [target("one"), target("two", "app://-/index.html?initialRoute=%2Fthreads%2Ftwo"),
                      target("avatar", "app://-/index.html?initialRoute=%2Favatar-overlay"),
                      target("spotlight", "app://-/index.html?initialRoute=%2Fspotlight"),
                      target("detached", "app://-/detached-window.html?initialRoute=%2Fdetached-window"),
                      target("browser", "app://-/browser.html"),
                      target("web", "https://example.test"),
                      {**target("worker"), "type": "worker"}]
        self.assertEqual(set(bridge.renderer_targets(candidates)), {"one", "two"})

    def test_deep_linked_native_windows_do_not_require_codex_in_the_window_title(self):
        window = target("thread", "app://-/index.html?initialRoute=%2Fthreads%2Fthread")
        window["title"] = "Review changes"
        self.assertEqual(set(bridge.renderer_targets([window])), {"thread"})

    def test_native_window_can_enter_chat_after_starting_on_settings(self):
        window = target("settings", "app://-/index.html?initialRoute=%2Fsettings")
        window["title"] = "Settings"
        self.assertEqual(set(bridge.renderer_targets([window])), {"settings"})

    def test_lost_renderer_returns_to_launcher_without_starting_unbound_monitors(self):
        with patch.object(bridge, "devtools_targets", side_effect=bridge.CDPError("closed")) as discover, \
             patch.object(bridge, "RendererSession") as session:
            bridge.run(9222, ImmediateEvent())
        self.assertEqual(discover.call_count, 3)
        session.assert_not_called()

    def test_new_windows_join_and_closed_windows_release_only_their_own_session(self):
        event, made, calls = ImmediateEvent(), {}, 0

        class Session:
            def __init__(self, item, *_args):
                self.url = item["webSocketDebuggerUrl"]
                self.starts = self.stops = 0
                made[item["id"]] = self

            def start(self):
                self.starts += 1

            def stop(self):
                self.stops += 1

        def discover(_port):
            nonlocal calls
            calls += 1
            if calls == 1:
                return [target("one")]
            if calls == 2:
                return [target("one"), target("two")]
            if calls == 4:
                event.set()
            return [target("two")]

        with patch.object(bridge, "devtools_targets", side_effect=discover), \
             patch.object(bridge, "RendererSession", Session):
            bridge.run(9222, event, FakeMonitor)
        self.assertEqual(set(made), {"one", "two"})
        self.assertEqual([(item.starts, item.stops) for item in made.values()], [(1, 1), (1, 1)])

    def test_transient_discovery_failure_does_not_destroy_healthy_window_sessions(self):
        event, made, calls = ImmediateEvent(), [], 0

        class Session:
            def __init__(self, item, *_args):
                self.url = item["webSocketDebuggerUrl"]
                self.starts = self.stops = 0
                made.append(self)

            def start(self):
                self.starts += 1

            def stop(self):
                self.stops += 1

        def discover(_port):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise bridge.CDPError("temporary discovery failure")
            if calls == 3:
                self.assertEqual(len(made), 1)
                self.assertEqual(made[0].stops, 0)
                event.set()
            return [target("one")]

        with patch.object(bridge, "devtools_targets", side_effect=discover), \
             patch.object(bridge, "RendererSession", Session):
            bridge.run(9222, event, FakeMonitor)
        self.assertEqual(len(made), 1)
        self.assertEqual((made[0].starts, made[0].stops), (1, 1))

    def test_each_window_forwards_its_own_selection_and_cleans_up_on_stop(self):
        event, monitors, forwarded, destroyed = threading.Event(), [], {}, set()
        lock = threading.Lock()

        def factory():
            monitor = FakeMonitor()
            monitors.append(monitor)
            return monitor

        class Client:
            def __init__(self, url, **_kwargs):
                self.key = url.rsplit("/", 1)[-1]

            def evaluate(self, expression):
                if expression == "window.__codexLiveSpeedBadge?.destroy()":
                    destroyed.add(self.key)
                elif "badge.update(" in expression:
                    encoded = expression.split("badge.update(", 1)[1].split(");", 1)[0]
                    payload = json.loads(encoded)
                    if payload["threadId"]:
                        with lock:
                            forwarded[self.key] = payload
                            if len(forwarded) == 2:
                                event.set()
                    return {"threadId": "local:" + self.key,
                            "hostId": "local" if self.key == "one" else "second-host"}
                return True

            def close(self):
                pass

        # A timeout ensures a failed worker cannot leave this test waiting forever.
        deadline = threading.Timer(3, event.set)
        deadline.start()
        try:
            with patch.object(bridge, "devtools_targets", return_value=[target("one"), target("two")]), \
                 patch.object(bridge, "CDPClient", Client):
                bridge.run(9222, event, factory, poll_interval=0.01)
        finally:
            deadline.cancel()
        self.assertEqual(set(forwarded), {"one", "two"})
        self.assertEqual(forwarded["one"]["threadId"], "one")
        self.assertEqual(forwarded["two"]["threadId"], "two")
        self.assertEqual(forwarded["two"]["hostId"], "second-host")
        self.assertEqual(len(monitors), 2)
        self.assertTrue(all(m.started and m.stopped for m in monitors))
        self.assertEqual(destroyed, {"one", "two"})


if __name__ == "__main__":
    unittest.main()
