import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"


def target(url="app://-/index.html", title="Codex", **extra):
    return {"id": "window", "type": "page", "title": title, "url": url,
            "webSocketDebuggerUrl": "ws://127.0.0.1/window", **extra}


class ShellTargetStateTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        root = Path(self.folder.name)
        # Exercise the shell function's actual embedded Python, with /json
        # supplied in-process so these checks never contact a running app.
        (root / "sitecustomize.py").write_text(
            "import io, os, urllib.request\n"
            "urllib.request.urlopen = lambda *args, **kwargs: "
            "io.BytesIO(os.environ['CODEX_SPEED_TEST_TARGETS'].encode())\n"
        )
        fallback = root / "python3"
        fallback.write_text("#!/bin/bash\nexit 97\n")
        fallback.chmod(0o755)
        self.env = {**os.environ, "CODEX_SPEED_PYTHON": sys.executable,
                    "PYTHONPATH": str(root), "PATH": str(root) + ":/bin:/usr/bin"}

    def state(self, targets, mode="ready"):
        env = {**self.env, "CODEX_SPEED_TEST_TARGETS": json.dumps(targets)}
        result = subprocess.run(
            ["/bin/bash", "-c", 'source "$1"; "codex_monitor_devtools_$3" "$2"',
             "test-target-state", str(SCRIPTS / "port_utils.sh"), "9222", mode],
            env=env, capture_output=True, text=True, timeout=5,
        )
        self.assertIn(result.returncode, (0, 1), result.stderr)
        return result.returncode == 0

    def test_native_main_window_and_thread_deep_link_are_ready_using_configured_python(self):
        self.assertTrue(self.state([target()]))
        self.assertTrue(self.state([target("app://codex/index.html")]))
        self.assertTrue(self.state([target(
            "app://-/index.html?initialRoute=%2Fthreads%2Fthread-a", "Review changes")]))
        # initialRoute stays unchanged after the user moves from Settings into
        # a chat. Window eligibility must not freeze the initial page choice.
        self.assertTrue(self.state([target("app://-/index.html?initialRoute=%2Fsettings", "Settings")]))

    def test_browser_worker_and_utility_targets_are_not_ready(self):
        for item in (
            target("https://example.test", "Codex documentation"),
            target(type="worker"),
            target("app://-/index.html?initialRoute=%2Favatar-overlay"),
            target("app://-/index.html?initialRoute=%2Fspotlight"),
            target("app://-/detached-window.html?initialRoute=%2Fdetached-window"),
            target("app://-/browser.html"),
            target(webSocketDebuggerUrl=None),
        ):
            with self.subTest(item=item):
                self.assertFalse(self.state([item]))

    def test_utility_owned_endpoint_waits_for_a_real_renderer_instead_of_claiming_a_foreign_port(self):
        avatar = target("app://-/index.html?initialRoute=%2Favatar-overlay")
        self.assertTrue(self.state([avatar], "owned"))
        self.assertFalse(self.state([avatar]))
        deep_link = target("app://-/index.html?initialRoute=%2Fthreads%2Fthread-a")
        self.assertTrue(self.state([avatar, deep_link]))
        self.assertFalse(self.state([target("https://example.test", "Codex")], "owned"))


if __name__ == "__main__":
    unittest.main()
