import copy
import unittest
import tempfile
import json
from pathlib import Path

from summarize_latency import correlated_stats, timing_stats, summarize


class TimingTests(unittest.TestCase):
    def setUp(self):
        self.data = {
            "Routes": [{"Route": "test", "First": {"RequestId": "first", "Ms": 999},
                        "Samples": [{"RequestId": "sample", "Ms": 110}]}],
            "Breakdown": {
                "first": {"RequestId": "first", "StatusCode": 200, "ElapsedMs": 999},
                "sample": {"RequestId": "sample", "StatusCode": 200, "ElapsedMs": 99}},
        }

    def test_nearest_rank(self):
        self.assertEqual(timing_stats(range(1, 101)), (50.5, 50, 95, 100))
        self.assertEqual(timing_stats([100])[2], 100)

    def test_first_excluded_and_http_separate(self):
        self.assertEqual(correlated_stats(self.data),
                         [("test", (99, 99, 99, 99), (110, 110, 110, 110))])

    def test_invalid_correlation(self):
        for field, value in (("RequestId", "wrong"), ("StatusCode", 400),
                             ("ElapsedMs", float("nan")), ("ElapsedMs", -1)):
            data = copy.deepcopy(self.data)
            data["Breakdown"]["sample"][field] = value
            with self.assertRaises(ValueError):
                correlated_stats(data)
        del self.data["Breakdown"]["first"]
        with self.assertRaises(ValueError):
            correlated_stats(self.data)

    def test_duplicate(self):
        self.data["Routes"][0]["Samples"].append(self.data["Routes"][0]["Samples"][0])
        with self.assertRaises(ValueError):
            correlated_stats(self.data)

    def test_duplicate_middleware_events(self):
        self.data["DuplicateTimingEvents"] = 1
        with self.assertRaises(ValueError):
            correlated_stats(self.data)

    def test_item_rejection_correlation(self):
        self.data["ItemCorrelations"] = {"rejected": 400}
        self.data["Breakdown"]["rejected"] = {
            "RequestId": "rejected", "StatusCode": 400, "ElapsedMs": 1}
        correlated_stats(self.data)
        self.data["Breakdown"]["rejected"]["StatusCode"] = 200
        with self.assertRaises(ValueError):
            correlated_stats(self.data)

    def test_item_only_report(self):
        data = dict(Mode="item", Stage="item regressions completed", CleanupVerified=True,
                    CorrelationVerified=True, ItemRegressionsComplete=True,
                    ItemCommandChecks=["Seven command checks passed."], Breakdown={})
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "reconstruct-nodes-server.dll").write_bytes(b"test-only")
            path = root / "baseline.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            summarize(path, root)
            report = (root / "item-regressions.md").read_text(encoding="utf-8")
            self.assertIn("not an endpoint latency baseline", report)
            self.assertIn("Item regressions complete: **True**", report)

    def test_legacy_report(self):
        data = self.data
        data.update(CapturedBackend="capture", SelectOneRtt=[1], Complete=True,
                    CleanupVerified=True, Concurrency="sequential")
        data["Routes"][0].update(MeanMs=110, Below100Ms=False)
        data["Breakdown"]["sample"]["Metrics"] = {}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "reconstruct-nodes-server.dll").write_bytes(b"test-only")
            path = root / "baseline.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            summarize(path, root)
            report = (root / "report.md").read_text(encoding="utf-8")
            self.assertIn("| test | 99.00 | 99.00 | 99.00 | 99.00 | PASS |", report)
            self.assertIn("| test | 110.00 | 110.00 | 110.00 | 110.00 |", report)
            self.assertNotIn("SQL target", report)

    def test_background_workload_concurrent_report(self):
        route = "GameProgress/BackgroundSync (progress)"
        data = copy.deepcopy(self.data)
        data.update(Routes=[], Stage="completed", CleanupVerified=True,
                    ConcurrentScenarios=[{
                        "SaveRoute": route,
                        "Save": {"RequestId": "first", "Ms": 999},
                        "Kill": {"RequestId": "sample", "Ms": 110},
                    }])
        for value in data["Breakdown"].values():
            value["Metrics"] = {}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "reconstruct-nodes-server.dll").write_bytes(b"test-only")
            path = root / "baseline.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            summarize(path, root)
            report = (root / "concurrent.md").read_text(encoding="utf-8")
            self.assertIn(f"| {route} | 1 |", report)
            self.assertIn(f"| {route} / Kill |", report)

    def test_reads_only_report(self):
        data = copy.deepcopy(self.data)
        data.update(Mode="reads", CapturedBackend="capture", SelectOneRtt=[1],
                    Complete=False, CleanupVerified=True, Concurrency="sequential",
                    SelectedWorkloadsComplete=True, ReadRegressionsComplete=True,
                    ReadRegressionChecks=["Five rich HTTP snapshots match legacy services."])
        data["Routes"][0].update(MeanMs=110, Below100Ms=False)
        data["Breakdown"]["sample"]["Metrics"] = {}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "reconstruct-nodes-server.dll").write_bytes(b"test-only")
            path = root / "baseline.json"
            for complete in (True, False):
                data["SelectedWorkloadsComplete"] = complete
                data["ReadRegressionsComplete"] = complete
                path.write_text(json.dumps(data), encoding="utf-8")
                summarize(path, root)
                report = (root / "report.md").read_text(encoding="utf-8")
                self.assertIn("# Five snapshot reads baseline", report)
                self.assertIn("Full 18-workload baseline complete: **False**", report)
                self.assertIn(f"Selected five reads complete: **{complete}**", report)
                self.assertIn("## Rich read consistency checks", report)
                self.assertNotIn("fresh single/ten draws", report)
                self.assertNotIn("Shop/Draw counts", report)

    def test_selected_command_reports(self):
        for mode, title, count in (("economy", "Economy commands", 2), ("saves", "Snapshot saves", 3),
                                   ("shop", "Shop single/ten draws", 2)):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as directory:
                data = copy.deepcopy(self.data)
                data.update(Mode=mode, CapturedBackend="capture", SelectOneRtt=[1], Complete=False,
                            CleanupVerified=True, Concurrency="sequential", ExpectedWorkloads=count,
                            SelectedWorkloadsComplete=True, SelectedRegressionsComplete=True,
                            SelectedRegressionChecks=["PASS: command regression."])
                data["Routes"][0].update(MeanMs=110, Below100Ms=False)
                data["Breakdown"]["sample"]["Metrics"] = {}
                root = Path(directory)
                (root / "reconstruct-nodes-server.dll").write_bytes(b"test-only")
                path = root / "baseline.json"
                for complete in (True, False):
                    data["SelectedWorkloadsComplete"] = complete
                    path.write_text(json.dumps(data), encoding="utf-8")
                    summarize(path, root)
                    report = (root / "report.md").read_text(encoding="utf-8")
                    self.assertIn(f"# {title} baseline", report)
                    self.assertIn("Full 18-workload baseline complete: **False**", report)
                    self.assertIn(f"Selected workloads complete: **{complete}**", report)
                    self.assertIn(f"Expected selected workloads: {count}", report)
                    self.assertIn("PASS: command regression.", report)
                    self.assertNotIn("fresh single/ten draws", report)

    def test_regression_rejection_statuses(self):
        self.data["RegressionCorrelations"] = {"stale": 400, "conflict": 409}
        self.data["Breakdown"].update({
            "stale": {"RequestId": "stale", "StatusCode": 400, "ElapsedMs": 2},
            "conflict": {"RequestId": "conflict", "StatusCode": 409, "ElapsedMs": 3},
        })
        correlated_stats(self.data)
        self.data["Breakdown"]["conflict"]["StatusCode"] = 400
        with self.assertRaises(ValueError):
            correlated_stats(self.data)

    def test_regression_correlations_not_reused_as_measured_samples(self):
        self.data["RegressionCorrelations"] = {"sample": 200}
        with self.assertRaises(ValueError):
            correlated_stats(self.data)


if __name__ == "__main__":
    unittest.main()
