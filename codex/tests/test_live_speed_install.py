import contextlib
import io
import os
from pathlib import Path
import plistlib
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import manage_live_speed as manager


class InstallationTests(unittest.TestCase):
    def test_incomplete_or_unlaunchable_payload_does_not_stop_existing_service(self):
        with tempfile.TemporaryDirectory() as folder:
            source, installed = Path(folder) / "source", Path(folder) / "installed"
            (source / "bin").mkdir(parents=True)
            (source / "scripts").mkdir()
            (source / "bin/AgentSpeedCodexMeter").write_text("new binary")
            (installed / "bin").mkdir(parents=True)
            (installed / "bin/AgentSpeedCodexMeter").write_text("old binary")
            with patch.object(manager, "ROOT", source), patch.object(manager, "INSTALLED_ROOT", installed), \
                 patch.object(manager, "PLIST", Path(folder) / "test.plist"), \
                 patch.object(manager, "launchctl") as launch, \
                 patch.object(manager.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)) as validate:
                with self.assertRaises(FileNotFoundError):
                    manager.install(9222)
                launch.assert_not_called()
                for name in ("README.md", "LICENSE-Agent-Speed", "LICENSE-Codex-Monitor", "LICENSE-DeepSeek-Harness", "UPSTREAM.md"):
                    (source / name).write_text("fixture")
                validate.side_effect = [subprocess.CompletedProcess([], 0), subprocess.CalledProcessError(-9, [])]
                with self.assertRaises(subprocess.CalledProcessError):
                    manager.install(9222)
                launch.assert_not_called()
                self.assertEqual((installed / "bin/AgentSpeedCodexMeter").read_text(), "old binary")

    def test_invalid_port_is_rejected_before_installation(self):
        with patch.object(manager, "launchctl") as launch:
            for port in (0, -1, 65536):
                with self.assertRaises(ValueError):
                    manager.install(port)
            launch.assert_not_called()

    def test_custom_codex_home_is_forwarded_to_launchd_and_preserved_on_upgrade(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            installed = root / "installed"
            (installed / "bin").mkdir(parents=True)
            (installed / "bin/AgentSpeedCodexMeter").touch()
            plist = root / "LaunchAgents/test.plist"
            home = (root / "custom codex home").resolve()
            with patch.object(manager, "ROOT", installed), \
                 patch.object(manager, "INSTALLED_ROOT", installed), \
                 patch.object(manager, "PLIST", plist), \
                 patch.object(manager, "launchctl", return_value=subprocess.CompletedProcess([], 0)), \
                 patch.object(manager.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)), \
                 contextlib.redirect_stdout(io.StringIO()):
                with patch.dict(os.environ, {"CODEX_HOME": str(home)}):
                    manager.install(9222)
                self.assertEqual(plistlib.loads(plist.read_bytes())["EnvironmentVariables"]["CODEX_HOME"], str(home))
                # Upgrades launched without shell configuration keep the chosen data root.
                with patch.dict(os.environ, {}, clear=True):
                    manager.install(9222)
                self.assertEqual(plistlib.loads(plist.read_bytes())["EnvironmentVariables"]["CODEX_HOME"], str(home))

    def test_update_installs_meter_and_display_outside_source_folder_without_copying_state(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source, installed = root / "Documents/project", root / "Library/Application Support/CodexLiveSpeed"
            for directory in ("scripts", "bin", "runtime"):
                (source / directory).mkdir(parents=True, exist_ok=True)
            (source / "scripts/start_live_speed.sh").write_text("version one")
            (source / "bin/AgentSpeedCodexMeter").write_text("test executable")
            (source / "runtime/completed-calls.json").write_text("do not install test state")
            for name in ("README.md", "LICENSE-Agent-Speed", "LICENSE-Codex-Monitor", "LICENSE-DeepSeek-Harness", "UPSTREAM.md"):
                (source / name).write_text("test fixture")
            plist = root / "Library/LaunchAgents/test.plist"
            with patch.object(manager, "ROOT", source), \
                 patch.object(manager, "INSTALLED_ROOT", installed), \
                 patch.object(manager, "PLIST", plist), \
                 patch.object(manager, "launchctl", return_value=subprocess.CompletedProcess([], 0)), \
                 patch.object(manager.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)) as validate, \
                 contextlib.redirect_stdout(io.StringIO()):
                manager.install(9223)
                old_binary = (installed / "bin/AgentSpeedCodexMeter").open()
                self.addCleanup(old_binary.close)
                old_inode = (installed / "bin/AgentSpeedCodexMeter").stat().st_ino
                (source / "scripts/start_live_speed.sh").write_text("version two")
                (source / "bin/AgentSpeedCodexMeter").write_text("updated executable")
                manager.install(9223)
                self.assertNotEqual((installed / "bin/AgentSpeedCodexMeter").stat().st_ino, old_inode)
                self.assertEqual(old_binary.read(), "test executable")
            self.assertEqual((installed / "scripts/start_live_speed.sh").read_text(), "version two")
            self.assertTrue((installed / "bin/AgentSpeedCodexMeter").is_file())
            self.assertEqual((installed / "bin/AgentSpeedCodexMeter").read_text(), "updated executable")
            self.assertFalse((installed / "runtime/completed-calls.json").exists())
            config = plistlib.loads(plist.read_bytes())
            self.assertEqual(config["WorkingDirectory"], str(installed))
            self.assertEqual(config["ProgramArguments"], ["/bin/bash", str(installed / "scripts/start_live_speed.sh"), "9223"])
            self.assertNotIn(str(source), repr(config))
            self.assertEqual(validate.call_args.kwargs["cwd"], installed)


if __name__ == "__main__":
    unittest.main()
