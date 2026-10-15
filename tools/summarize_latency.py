"""Render a sanitized captured baseline JSON into a persistent Markdown report."""
import argparse
import hashlib
import json
from pathlib import Path
import statistics


def summarize(path, baseline):
    data = json.loads(path.read_text(encoding="utf-8"))
    digest = hashlib.sha256((baseline / "reconstruct-nodes-server.dll").read_bytes()).hexdigest()
    if data.get("RegressionChecks"):
        lines = ["# Snapshot regression evidence", "",
                 f"- Captured backend DLL SHA-256: `{digest}`",
                 f"- Stage: {data['Stage']}",
                 f"- Exact disposable-fixture cleanup verified: **{data['CleanupVerified']}**",
                 "- Read-only OPENJSON projection and actual fixture INSERT schemas passed before these checks.",
                 "- This is regression evidence, not a 100-sample timing run.", ""]
        lines.extend(f"- {check}" for check in data["RegressionChecks"])
        lines += ["", "Reproduce with the same capture:", "",
                  "```powershell",
                  f"dotnet run --project tools/LatencyBaseline -p:Baseline=<absolute-capture-path> -- "
                  f"artifacts/{data['CapturedBackend']} --run --regression-only",
                  "```", ""]
        output = path.with_name("regressions.md")
        output.write_text("\n".join(lines), encoding="utf-8")
        print(output)
        return
    if data.get("ConcurrentScenarios"):
        return summarize_concurrent(path, data, digest)
    lines = [
        "# Real HTTP baseline",
        "",
        f"- Captured backend: `{data['CapturedBackend']}`",
        f"- Backend DLL SHA-256: `{digest}`",
        "- SQL target: `tcp:timwu19950429.database.windows.net,1433` / `reconstruct-nodes`",
        f"- Direct SELECT 1 RTT: {statistics.mean(data['SelectOneRtt']):.2f} ms (10 samples).",
        f"- Complete: **{data['Complete']}**; exact fixture cleanup verified: **{data['CleanupVerified']}**.",
        "- Each endpoint: first request separately, then **100** real HTTP requests.",
        "- First request is per-route, not guaranteed process-cold.",
        "- Isolated loopback backend and in-memory Azurite; disposable guest only.",
        "- Workload: CORE3, eight road tiles, no deployable characters; one staff, diamond inventory,",
        "  talent version 1, six achievement groups with 33 item targets.",
        "- Saves maintain revision contracts; each wave/kill/progress operation has a unique request ID.",
        f"- Concurrency: {data['Concurrency']}",
        "",
        "| Endpoint | First ms | Samples | Mean ms | Mean <100 ms |",
        "|---|---:|---:|---:|---|",
    ]
    for route in data["Routes"]:
        lines.append(f"| {route['Route']} | {route['First']['Ms']:.2f} | "
                     f"{len(route['Samples'])} | {route['MeanMs']:.2f} | "
                     f"{'PASS' if route['Below100Ms'] else 'FAIL'} |")
    lines += ["", "## Request-correlated breakdown", "",
              "Means below are per measured HTTP request, excluding the first request.",
              "SQL counts attempted repository commands, not SQL statements within batches.",
              "PlayerLockCommand includes network/execution, not isolated SQL lock waits.",
              "TransactionLifetime includes application gaps. Timings overlap; do not sum.",
              "QueryMultiple initial execution excludes deferred reader consumption.", "",
              "| Endpoint | SQL count | SQL ms | Player lock command ms | Transaction ms | "
              "Gate wait ms | Connection-limit ms | Operation-limit ms | Connection open ms |",
              "|---|---:|---:|---:|---:|---:|---:|---:|---:|"]
    for route in data["Routes"]:
        records = [data["Breakdown"].get(sample["RequestId"]) for sample in route["Samples"]]
        if any(record is None for record in records):
            lines.append(f"| {route['Route']} | Missing correlation data |")
            continue

        def metric(category, field="Milliseconds"):
            return statistics.mean(record["Metrics"].get(category, {}).get(field, 0)
                                   for record in records)

        values = [metric("Sql", "Count"), metric("Sql"), metric("PlayerLockCommand"),
                  metric("TransactionLifetime"), metric("GateWait"),
                  metric("ConnectionLimitWait"), metric("OperationLimitWait"), metric("ConnectionOpen")]
        lines.append(f"| {route['Route']} | " + " | ".join(f"{value:.2f}" for value in values) + " |")
    lines += ["", "## Interpretation", "",
              "The independent endpoint threshold is not replaced by a pooled mean. "
              "These results are baseline evidence, not proof that the latency objective is met.",
              "Raw per-request timings and matching aggregate diagnostics are in `baseline.json`.",
              "No session tokens, payloads, SQL text/parameters or credentials are in this report.",
              ""]
    output = path.with_name("report.md")
    output.write_text("\n".join(lines), encoding="utf-8")
    print(output)


def summarize_concurrent(path, data, digest):
    lines = [
        "# Supplemental concurrent save / kill baseline", "",
        f"- Captured backend DLL SHA-256: `{digest}`",
        f"- Stage: {data['Stage']}; exact fixture cleanup verified: **{data['CleanupVerified']}**.",
        "- Same captured baseline backend, a fresh disposable fixture and isolated processes.",
        "- One first pair separately, then ten concurrent pairs for each save route.",
        "- Every kill is unique and verifies the exact gold increment; final gold and 33 achievement",
        "  items were read back successfully. No retries, rejected writes or deduplication replays.",
        "- These are supplemental contention samples, not replacements for 100-per-endpoint results.",
        "- Player lock command includes SQL/network time, not isolated SQL lock wait.", "",
        "| Concurrent save route | Pairs | Save mean ms | Kill mean ms | "
        "Save player-lock command ms | Kill player-lock command ms |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for route in ("GameProgress/Save", "AchievementProgress/Save"):
        pairs = [p for p in data["ConcurrentScenarios"] if p["SaveRoute"] == route]

        def average(side, category=None):
            if category is None:
                return statistics.mean(p[side]["Ms"] for p in pairs)
            return statistics.mean(data["Breakdown"][p[side]["RequestId"]]["Metrics"]
                                   .get(category, {}).get("Milliseconds", 0) for p in pairs)

        lines.append(f"| {route} | {len(pairs)} | {average('Save'):.2f} | {average('Kill'):.2f} | "
                     f"{average('Save', 'PlayerLockCommand'):.2f} | "
                     f"{average('Kill', 'PlayerLockCommand'):.2f} |")
    lines += ["", "## First pairs (excluded above)", "",
              "| Save route | Save ms | Kill ms |", "|---|---:|---:|"]
    for pair in data["ConcurrentScenarios"]:
        if pair["SaveRoute"].endswith("(first pair)"):
            lines.append(f"| {pair['SaveRoute']} | {pair['Save']['Ms']:.2f} | {pair['Kill']['Ms']:.2f} |")
    lines += ["", "Raw pair samples, correlation IDs and aggregate timings are in `baseline.json`.", ""]
    output = path.with_name("concurrent.md")
    output.write_text("\n".join(lines), encoding="utf-8")
    print(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path)
    parser.add_argument("baseline", type=Path)
    args = parser.parse_args()
    summarize(args.report, args.baseline)
