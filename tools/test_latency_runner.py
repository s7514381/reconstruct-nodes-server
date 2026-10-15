import contextlib
import io
import json
import os
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from unittest.mock import patch

import latency_runner


class RunnerTests(unittest.TestCase):
    def plan(self, port=12345):
        return {"base_url": f"http://127.0.0.1:{port}",
                "fixture_id": "latency-fixture-unit",
                "endpoints": [{"path": f"/route{i}", "body": {"sample": "{sample}"},
                               "expect": [{"path": ["Success"], "equals": True}]}
                              for i in range(15)]}

    def test_safety_guards(self):
        with patch.dict(os.environ, {}, clear=True):
            with self.assertRaises(ValueError):
                latency_runner.validate(self.plan())
        with patch.dict(os.environ, {"LATENCY_DATABASE_APPROVAL": "dedicated-test-only"}):
            plan = self.plan()
            plan["base_url"] = "http://example.com"
            with self.assertRaises(ValueError):
                latency_runner.validate(plan)
            plan = self.plan()
            plan["endpoints"].pop()
            with self.assertRaises(ValueError):
                latency_runner.validate(plan)

    def test_real_http_counts_and_first_request_separation(self):
        calls = {}

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                calls.setdefault(self.path, []).append(body["sample"])
                self.send_response(200)
                self.send_header("X-Request-Timing-Id", "a" * 32)
                self.end_headers()
                self.wfile.write(b'{"Success":true}')

        with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
            thread = threading.Thread(target=server.serve_forever)
            thread.start()
            try:
                output = io.StringIO()
                with patch.dict(os.environ, {"LATENCY_DATABASE_APPROVAL": "dedicated-test-only"}):
                    with contextlib.redirect_stdout(output):
                        # This is protocol validation against a synthetic server, NOT benchmark evidence.
                        latency_runner.run(self.plan(server.server_port))
                report = json.loads(output.getvalue())
                self.assertEqual(15, len(report["endpoints"]))
                for samples in calls.values():
                    self.assertEqual([str(i) for i in range(101)], samples)
                for endpoint in report["endpoints"]:
                    self.assertEqual(100, endpoint["samples"])
                    self.assertEqual(100, len(endpoint["requests"]))
            finally:
                server.shutdown()
                thread.join()


if __name__ == "__main__":
    unittest.main()
