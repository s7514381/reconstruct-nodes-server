"""Explicitly authorized fixture-only HTTP runner. No production configuration is loaded."""
import argparse
from contextlib import closing
import http.client
import json
import os
import statistics
import time
from urllib.parse import urlsplit


def validate(plan):
    if os.environ.get("LATENCY_DATABASE_APPROVAL") != "dedicated-test-only":
        raise ValueError("Explicit dedicated fixture database authorization is required")
    address = urlsplit(plan["base_url"])
    if (address.scheme != "http" or address.hostname not in ("127.0.0.1", "localhost")
            or address.username or address.password or address.path not in ("", "/")
            or address.query or address.fragment):
        raise ValueError("Only an explicitly owned loopback HTTP backend is permitted")
    if not plan["fixture_id"].startswith(("core-http-fixture-", "latency-fixture-")):
        raise ValueError("An owned disposable fixture is required")
    endpoints = plan["endpoints"]
    if len(endpoints) != 15 or len({e["path"] for e in endpoints}) != 15:
        raise ValueError("Exactly 15 distinct log-derived endpoints are required")
    for endpoint in endpoints:
        path = endpoint["path"]
        if not path.startswith("/") or path.startswith("//") or "?" in path or "#" in path:
            raise ValueError("Only fixed endpoint paths without query strings are allowed")
        if not endpoint.get("expect"):
            raise ValueError("Every endpoint needs explicit business response assertions")
        for assertion in endpoint["expect"]:
            if not isinstance(assertion["path"], list):
                raise ValueError("Assertion path must be a list of JSON keys")
    return address


def request(connection, endpoint, index):
    # Unique request IDs can be templated; retrying an unknown write is intentionally unsupported.
    body = json.loads(json.dumps(endpoint["body"]).replace("{sample}", str(index)))
    started = time.perf_counter()
    connection.request("POST", endpoint["path"], json.dumps(body),
                       {"Content-Type": "application/json"})
    response = connection.getresponse()
    raw = response.read()
    elapsed = (time.perf_counter() - started) * 1000
    if response.status != endpoint.get("status", 200):
        raise ValueError("HTTP status mismatch (response intentionally not logged)")
    value = json.loads(raw)
    for assertion in endpoint["expect"]:
        actual = value
        for key in assertion["path"]:
            actual = actual[key]
        if actual != assertion["equals"]:
            raise ValueError("Business assertion failed (response intentionally not logged)")
    correlation = response.getheader("X-Request-Timing-Id")
    if not correlation or len(correlation) != 32 or any(c not in "0123456789abcdef" for c in correlation):
        raise ValueError("Opt-in backend request correlation is required")
    return elapsed, correlation


def run(plan):
    address = validate(plan)
    results = []
    with closing(http.client.HTTPConnection(address.hostname, address.port, timeout=30)) as connection:
        for endpoint in plan["endpoints"]:
            cold, cold_id = request(connection, endpoint, 0)
            samples = [request(connection, endpoint, i) for i in range(1, 101)]
            mean = statistics.mean(ms for ms, _ in samples)
            results.append({"endpoint": endpoint["path"], "first_request_ms": cold,
                            "first_request_id": cold_id, "samples": 100, "mean_ms": mean,
                            "pass": mean < 100,
                            "requests": [{"ms": ms, "id": key} for ms, key in samples]})
    print(json.dumps({"endpoints": results, "pass": all(r["pass"] for r in results)}))
    return 0 if all(r["pass"] for r in results) else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("plan", help="Untracked fixture-only JSON request plan")
    args = parser.parse_args()
    try:
        with open(args.plan, encoding="utf-8") as source:
            exit_code = run(json.load(source))
    except (ValueError, KeyError, TypeError, OSError, http.client.HTTPException):
        # Exception text can include URLs, headers or payloads: never print it.
        parser.exit(2, "Fixture validation or HTTP execution failed; no payload was logged.\n")
    raise SystemExit(exit_code)
