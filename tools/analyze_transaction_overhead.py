"""Estimate transaction overhead residuals from existing request-correlated diagnostics."""
import argparse
import json
import math
from pathlib import Path
import statistics


def analyze(path):
    report = json.loads(path.read_text(encoding="utf-8"))
    rtt = statistics.mean(report["SelectOneRtt"])
    lines = [
        "# Observed transaction overhead residuals", "",
        f"- Existing direct SELECT 1 average RTT: **{rtt:.2f} ms**.",
        "- No database or benchmark was rerun for this analysis.",
        "- TransactionLifetime starts after BeginTransaction succeeds and ends at transaction disposal.",
        "- SQL command timings exclude begin/commit/rollback. Commit is inside TransactionLifetime.",
        "- HTTP-minus-transaction includes begin and all work outside the transaction, including HTTP overhead.",
        "- Transaction-minus-SQL includes commit, application gaps, reader consumption and other unmeasured work.",
        "- Thus these are **residuals**, not isolated begin/commit measurements or proven latency floors.", "",
        "| Endpoint | HTTP−transaction mean ms | Transaction−SQL mean ms | "
        "HTTP−SQL mean ms | HTTP−SQL observed min ms | HTTP−SQL p05 ms |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for route in report["Routes"]:
        rows = []
        for sample in route["Samples"]:
            metrics = report["Breakdown"][sample["RequestId"]]["Metrics"]
            if "TransactionLifetime" not in metrics:
                continue
            sql = metrics.get("Sql", {}).get("Milliseconds", 0)
            transaction = metrics["TransactionLifetime"]["Milliseconds"]
            rows.append((sample["Ms"] - transaction, transaction - sql, sample["Ms"] - sql))
        if not rows:
            continue
        residuals = sorted(row[2] for row in rows)
        values = [statistics.mean(row[index] for row in rows) for index in range(3)]
        values += [residuals[0], residuals[max(0, math.ceil(len(residuals) * .05) - 1)]]
        lines.append(f"| {route['Route']} | " + " | ".join(f"{v:.2f}" for v in values) + " |")
    lines += [
        "", "## Can an explicit client transaction with at least one command meet 100 ms?", "",
        f"A simple three-serial-round-trip model (BEGIN + one command + COMMIT) at {rtt:.2f} ms "
        f"predicts about **{3 * rtt:.2f} ms** before additional work. The two-RTT component alone "
        f"would be **{2 * rtt:.2f} ms**. This model is consistent with the observed residuals, "
        "but does not measure the individual protocol operations.",
        "",
        "At this measured network latency, batching domain rows alone is therefore unlikely to "
        "deliver a robust per-endpoint mean below 100 ms while preserving the current separate "
        "client transaction round trips. An actual one-command transaction measurement or "
        "begin/commit-specific instrumentation would be needed to establish its mean directly.",
        "",
        "**No mathematical impossibility is claimed:** SELECT 1 average RTT is not a per-request "
        "lower bound; wire behavior, connection state, latency distribution and execution paths "
        "can differ. Observed minima/p05 are finite-sample statistics, not guaranteed floors.",
        "",
    ]
    output = path.with_name("transaction-overhead.md")
    output.write_text("\n".join(lines), encoding="utf-8")
    print(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path)
    analyze(parser.parse_args().report)
