"""Render a sanitized captured baseline JSON into a persistent Markdown report."""
import argparse
import hashlib
import json
from pathlib import Path
import statistics
import math


def timing_stats(values):
    values = sorted(values)
    if not values or any(isinstance(v, bool) or not math.isfinite(v) or v < 0 for v in values):
        raise ValueError("Invalid timing samples")
    return (statistics.mean(values), values[math.ceil(len(values) * .5) - 1],
            values[math.ceil(len(values) * .95) - 1], values[-1])


def correlated_stats(data):
    if data.get("DuplicateTimingEvents", 0):
        raise ValueError("Duplicate middleware timing events")
    seen = set()
    for correlations in ("ItemCorrelations", "RegressionCorrelations"):
        for request_id, status in data.get(correlations, {}).items():
            if request_id in seen:
                raise ValueError("Duplicate regression timing correlation")
            record = data["Breakdown"].get(request_id)
            if not record or record.get("RequestId") != request_id or record.get("StatusCode") != status:
                raise ValueError("Missing or mismatched regression timing correlation")
            timing_stats([record["ElapsedMs"]])
            seen.add(request_id)
    result = []
    groups = [(r["Route"], [r["First"]], r["Samples"]) for r in data.get("Routes", [])]
    groups += [(p["SaveRoute"], [], [p["Save"], p["Kill"]])
               for p in data.get("ConcurrentScenarios", [])]
    for name, first, samples in groups:
        elapsed = []
        for sample in first + samples:
            request_id = sample["RequestId"]
            if request_id in seen:
                raise ValueError("Duplicate timing correlation")
            seen.add(request_id)
            record = data["Breakdown"].get(request_id)
            if (not record or record.get("RequestId") != request_id
                    or record.get("StatusCode") != 200):
                raise ValueError("Missing or unsuccessful timing correlation")
            timing_stats([record["ElapsedMs"]])
            if sample not in first:
                elapsed.append(record["ElapsedMs"])
        result.append((name, timing_stats(elapsed), timing_stats([s["Ms"] for s in samples])))
    return result


def summarize(path, baseline):
    data = json.loads(path.read_text(encoding="utf-8"))
    stats = correlated_stats(data)
    digest = hashlib.sha256((baseline / "reconstruct-nodes-server.dll").read_bytes()).hexdigest()
    if data.get("Mode") == "item":
        lines = ["# Item command regression evidence", "",
                 f"- Backend DLL SHA-256: `{digest}`",
                 f"- Stage: {data['Stage']}",
                 f"- Item regressions complete: **{data['ItemRegressionsComplete']}**",
                 f"- Exact fixture cleanup verified: **{data['CleanupVerified']}**",
                 f"- Correlation verified: **{data['CorrelationVerified']}**",
                 "- This is command regression evidence, not an endpoint latency baseline.", ""]
        lines.extend(f"- {check}" for check in data.get("ItemCommandChecks", []))
        output = path.with_name("item-regressions.md")
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(output)
        return
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
                  f"<absolute-capture-path> --run --regression-only",
                  "```", ""]
        output = path.with_name("regressions.md")
        output.write_text("\n".join(lines), encoding="utf-8")
        print(output)
        return
    if data.get("ConcurrentScenarios"):
        return summarize_concurrent(path, data, digest)
    reads_only = data.get("Mode") == "reads"
    selected_mode = data.get("Mode") in ("economy", "saves", "shop")
    selected_title = {"economy": "Economy commands", "saves": "Snapshot saves",
                      "shop": "Shop single/ten draws"}.get(data.get("Mode"))
    lines = [
        f"# {selected_title} baseline" if selected_mode else "# Five snapshot reads baseline" if reads_only else "# Real HTTP baseline",
        "",
        f"- Captured backend: `{data['CapturedBackend']}`",
        f"- Backend DLL SHA-256: `{digest}`",
        f"- Direct SELECT 1 RTT: {statistics.mean(data['SelectOneRtt']):.2f} ms (10 samples).",
        f"- Full 18-workload baseline complete: **{data['Complete']}**; exact fixture cleanup verified: **{data['CleanupVerified']}**.",
        *([f"- Selected five reads complete: **{data['SelectedWorkloadsComplete']}**.",
           f"- Rich read regressions complete: **{data['ReadRegressionsComplete']}**.",
           "- Only the five batch snapshot GET actions are timed; no write latency is measured."]
          if reads_only else []),
        *([f"- Selected workloads complete: **{data['SelectedWorkloadsComplete']}**.",
           f"- Selected regressions complete: **{data['SelectedRegressionsComplete']}**.",
           f"- Expected selected workloads: {data['ExpectedWorkloads']}.",
           "- Regression/replay/rejection calls are outside the timing samples."]
          if selected_mode else []),
        "- Each endpoint: first request separately, then **100** real HTTP requests.",
        "- First request is per-route, not guaranteed process-cold.",
        "- Isolated loopback backend and in-memory Azurite; disposable guest only.",
        *(["- Rich workload: equipped CORE3 and an unarmed character with ordered skill children;",
           "  three weapons (two with distinct affix/unique values), eight roads, acquired/legacy talent nodes,",
           "  six achievement groups / 33 targets, collected entries and a real claimed reward date.",
           "- Every first/measured HTTP snapshot must equal the same fixture's legacy service Get",
           "  captured inside an outer repository transaction, released before sending HTTP."]
          if reads_only else [
           "- Economy workload: unique wave sessions and unique non-boss kill indices; no active gold talent bonus.",
          ] if data.get("Mode") == "economy" else [
           "- Snapshot save workload: eight roads, six achievement groups / 33 targets with retained claims,",
           "  three acquired talent nodes plus legacy true/false values; all saves advance expected revisions."
          ] if data.get("Mode") == "saves" else [
           "- Shop workload: separate count=1/count=10 draws, funded before timing;",
           "  first acquisitions, default skills/equipment, weapon ownership and same-batch shard counts",
           "  are checked against authoritative readbacks; measured requests always use fresh IDs."
          ] if data.get("Mode") == "shop" else [
           "- Workload: CORE3, eight road tiles, no deployments; staff and diamond inventory,",
           "  talent version 1 (three utility nodes), six achievement groups with 33 targets;",
           "  fresh single/ten draws funded by unmeasured unique kill rewards.",
           "- Saves maintain revision contracts; each wave/kill/progress operation has a unique request ID."]),
        f"- Concurrency: {data['Concurrency']}",
        "",
        "| Endpoint | First ms | Samples | Mean ms | Mean <100 ms |",
        "|---|---:|---:|---:|---|",
    ]
    for route in data["Routes"]:
        lines.append(f"| {route['Route']} | {route['First']['Ms']:.2f} | "
                     f"{len(route['Samples'])} | {route['MeanMs']:.2f} | "
                     f"{'PASS' if route['Below100Ms'] else 'FAIL'} |")
    if reads_only:
        lines += ["", "## Rich read consistency checks", ""]
        lines.extend(f"- {check}" for check in data.get("ReadRegressionChecks", []))
    if selected_mode:
        lines += ["", "## Selected command regressions", ""]
        lines.extend(f"- {check}" for check in data.get("SelectedRegressionChecks", []))
    lines += ["", "## Middleware ElapsedMs (primary threshold)", "",
              "First requests excluded. P50/P95 use nearest rank; each workload must have P95 <100 ms.",
              "Each selected route is independent, never pooled." if reads_only or selected_mode
              else "Shop/Draw counts are independent workloads, not pooled.", "",
              "| Endpoint/workload | Mean ms | P50 ms | P95 ms | Max ms | P95 <100 ms |",
              "|---|---:|---:|---:|---:|---|"]
    for name, backend, _ in stats:
        lines.append(f"| {name} | " + " | ".join(f"{v:.2f}" for v in backend)
                     + f" | {'PASS' if backend[2] < 100 else 'FAIL'} |")
    lines += ["", "## HTTP round-trip (separate; first excluded)", "",
              "| Endpoint/workload | Mean ms | P50 ms | P95 ms | Max ms |",
              "|---|---:|---:|---:|---:|"]
    for name, _, http in stats:
        lines.append(f"| {name} | " + " | ".join(f"{v:.2f}" for v in http) + " |")
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
    save_workloads = sorted({pair["SaveRoute"] for pair in data["ConcurrentScenarios"]
                             if not pair["SaveRoute"].endswith("(first pair)")})
    for route in save_workloads:
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
    lines += ["", "## Middleware ElapsedMs (supplemental, first pairs excluded)", "",
              "| Save route / side | Mean ms | P50 ms | P95 ms | Max ms | P95 <100 ms |",
              "|---|---:|---:|---:|---:|---|"]
    for route in save_workloads:
        for side in ("Save", "Kill"):
            values = [data["Breakdown"][p[side]["RequestId"]]["ElapsedMs"]
                      for p in data["ConcurrentScenarios"] if p["SaveRoute"] == route]
            stats = timing_stats(values)
            lines.append(f"| {route} / {side} | " + " | ".join(f"{v:.2f}" for v in stats)
                         + f" | {'PASS' if stats[2] < 100 else 'FAIL'} |")
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
