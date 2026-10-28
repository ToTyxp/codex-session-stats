from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from completed_meter import CompletedCallStore, validated_sample, validated_stats


def sample(thread="a", *, host="local", response="r1", end=2000, rate=100.0):
    return {"type": "sample", "hostId": host, "threadId": thread, "responseId": response,
            "model": "test-model", "outputTokens": 100, "reasoningTokens": 80,
            "startMs": end - 1000, "endMs": end, "durationMs": 1000, "rate": rate,
            "source": "agent-speed/1.4.1", "metric": "response-wall"}


def stats(thread="a", host="local", updated=3000, **changes):
    return {"type": "stats", "hostId": host, "threadId": thread, "turns": 2,
            "steps": 3, "inputTokens": 1000, "cachedInputTokens": 800, "outputTokens": 200,
            "updatedAtMs": updated, "complete": True, "source": "agent-speed/1.4.1", **changes}


class CompletedMeterTests(unittest.TestCase):
    def make_store(self):
        store = CompletedCallStore()
        store.ingest({"type": "ready"})
        return store

    def test_a_b_a_restores_each_response_without_zero_or_adding_reasoning_twice(self):
        store = self.make_store()
        view = store.view()
        view.start()
        store.ingest(sample("a"))
        store.ingest(sample("b", response="r2", rate=120))
        view.select("local:a")
        a = view.snapshot()
        self.assertEqual((a["rate"], a["outputTokens"], a["reasoningTokens"]), (100, 100, 80))
        self.assertEqual(a["responseId"], "r1")
        view.select("b")
        self.assertEqual(view.snapshot()["rate"], 120)
        view.select("a")
        self.assertEqual(view.snapshot(), a)
        # Another prompt/round does not replace a completed result.
        view.select("a")
        self.assertEqual(view.snapshot(), a)

    def test_background_completion_is_ready_when_returning_to_its_conversation(self):
        store = self.make_store()
        view = store.view()
        view.start()
        view.select("b")
        store.ingest(sample("a", response="background", end=6000, rate=140))
        self.assertIsNone(view.snapshot()["rate"])
        view.select("a")
        self.assertEqual((view.snapshot()["responseId"], view.snapshot()["rate"]), ("background", 140))

    def test_late_duplicate_and_invalid_records_do_not_overwrite_latest(self):
        store = self.make_store()
        view = store.view()
        view.start()
        view.select("a")
        store.ingest(sample(end=8000))
        expected = view.snapshot()
        for value in (sample(response="older", end=4000), sample(end=9000, rate=999),
                      sample(response="invalid", end=9000) | {"durationMs": 0},
                      sample(response="invalid2", end=9000) | {"reasoningTokens": 101},
                      sample(response="invalid3", end=9000) | {"rate": float("nan")},
                      sample(response="zero", end=9000) | {"outputTokens": 0, "reasoningTokens": 0, "rate": 0}):
            store.ingest(value)
            self.assertEqual(view.snapshot(), expected)

    def test_backend_restart_or_missing_new_data_keeps_last_valid_sample(self):
        store = self.make_store()
        view = store.view()
        view.start()
        view.select("a")
        store.ingest(sample())
        store._ready = False
        self.assertEqual((view.snapshot()["status"], view.snapshot()["rate"]), ("cached", 100))
        store.ingest({"type": "ready"})
        store.ingest({"type": "unavailable", "hostId": "local", "threadId": "a"})
        self.assertEqual((view.snapshot()["status"], view.snapshot()["rate"]), ("cached", 100))
        store.ingest(sample(response="new", end=9000, rate=80))
        self.assertEqual((view.snapshot()["status"], view.snapshot()["rate"]), ("connected", 80))

    def test_windows_hosts_and_pending_conversations_do_not_share_values(self):
        store = self.make_store()
        a, b = store.view(), store.view()
        a.start(); b.start()
        store.ingest(sample("same", host="local"))
        store.ingest(sample("same", host="remote", response="remote-r", rate=50))
        a.select("same", "local"); b.select("same", "remote")
        self.assertEqual((a.snapshot()["rate"], b.snapshot()["rate"]), (100, 50))
        a.select("local:client-new-thread:placeholder")
        self.assertIsNone(a.snapshot()["threadId"])
        self.assertIsNone(a.snapshot()["rate"])
        self.assertEqual(b.snapshot()["rate"], 50)
        b.stop()
        self.assertNotIn(b.key, store._views)

    def test_successful_replay_clears_unavailable_without_replacing_the_result(self):
        store = self.make_store()
        view = store.view()
        view.start()
        view.select("a")
        record = sample()
        store.ingest(record)
        expected = view.snapshot()
        store.ingest({"type": "unavailable", "hostId": "local", "threadId": "a"})
        self.assertEqual(view.snapshot()["status"], "cached")
        store.ingest({"type": "ready"})
        # A changed duplicate must not be accepted as a successful replay.
        store.ingest(record | {"rate": 999})
        self.assertEqual(view.snapshot()["status"], "cached")
        store.ingest(record)
        self.assertEqual(view.snapshot(), expected)

    def test_protocol_uses_numeric_completion_only(self):
        for value in (None, [], {"type": "sample"}, sample() | {"source": "old-visible-tokenizer"},
                      sample() | {"metric": "prediction"}, sample() | {"rate": True}):
            self.assertIsNone(validated_sample(value))
        value = validated_sample(sample() | {"text": "must never enter display cache"})
        self.assertNotIn("text", value)

    def test_stats_are_independent_of_speed_and_restore_per_window_and_host(self):
        store = self.make_store()
        a, b = store.view(), store.view()
        a.start(); b.start()
        a.select("a"); b.select("a", "remote")
        store.ingest(stats())
        store.ingest(stats(host="remote", inputTokens=500, cachedInputTokens=0))
        self.assertIsNone(a.snapshot()["rate"])
        self.assertEqual(a.snapshot()["stats"]["inputTokens"], 1000)
        self.assertEqual(b.snapshot()["stats"]["inputTokens"], 500)
        store.ingest(sample())
        a.select("new")
        self.assertIsNone(a.snapshot()["stats"])
        a.select("a")
        self.assertEqual(a.snapshot()["stats"]["steps"], 3)
        self.assertEqual(a.snapshot()["rate"], 100)

    def test_stats_revision_replaces_totals_instead_of_accumulating_or_blocking_decreases(self):
        store = self.make_store()
        view = store.view(); view.start(); view.select("a")
        store.ingest(stats())
        expected = view.snapshot()["stats"]
        for event in (stats(), stats(updated=2999), stats(inputTokens=9999)):
            store.ingest(event)
            self.assertEqual(view.snapshot()["stats"], expected)
        # A reconciled history can be smaller; revision, not magnitude, decides.
        store.ingest(stats(updated=4000, inputTokens=900, turns=None, complete=False))
        self.assertEqual(view.snapshot()["stats"]["inputTokens"], 900)
        self.assertIsNone(view.snapshot()["stats"]["turns"])
        self.assertFalse(view.snapshot()["stats"]["complete"])

    def test_stats_survive_backend_reconnect_and_successful_replay_clears_unavailable(self):
        store = self.make_store()
        view = store.view(); view.start(); view.select("a")
        store.ingest(stats())
        store._ready = False
        self.assertEqual(view.snapshot()["status"], "cached")
        self.assertEqual(view.snapshot()["stats"]["inputTokens"], 1000)
        store.ingest({"type": "ready"})
        store.ingest({"type": "unavailable", "hostId": "local", "threadId": "a"})
        store.ingest(stats())
        self.assertEqual(view.snapshot()["status"], "connected")

    def test_invalid_stats_cannot_replace_numeric_snapshot(self):
        store = self.make_store()
        view = store.view(); view.start(); view.select("a")
        store.ingest(stats())
        expected = view.snapshot()
        for bad in (stats(updated=5000, steps=True), stats(updated=5000, turns=4),
                    stats(updated=5000, inputTokens=-1), stats(updated=5000, cachedInputTokens=1001),
                    stats(updated=5000, complete="yes"), stats(updated=5000, outputTokens=1.5)):
            self.assertIsNone(validated_stats(bad))
            store.ingest(bad)
            self.assertEqual(view.snapshot(), expected)
        self.assertNotIn("text", validated_stats(stats(text="do not forward")))


if __name__ == "__main__":
    unittest.main()
