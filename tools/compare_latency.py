"""Compare complete independent-route reports without rerunning any request."""
import argparse
import json
from pathlib import Path


def compare(before_path, after_path):
    before = json.loads(before_path.read_text(encoding="utf-8"))
    after = json.loads(after_path.read_text(encoding="utf-8"))
    for report in (before, after):
        if not report["Complete"] or not report["CleanupVerified"]:
            raise ValueError("Both reports must be complete and cleanup-verified")
        if len(report["Routes"]) != 15 or any(len(r["Samples"]) != 100 for r in report["Routes"]):
            raise ValueError("Both reports must have 15 routes with 100 samples each")
    original = {r["Route"]: r for r in before["Routes"]}
    if set(original) != {r["Route"] for r in after["Routes"]}:
        raise ValueError("Route sets differ")
    lines = ["# Captured backend comparison", "",
             f"- Before: `{before['CapturedBackend']}`",
             f"- After: `{after['CapturedBackend']}`",
             "- Each mean independently uses 100 samples; first requests excluded.",
             "- Same fixture workload, separate guests and runs. Network/runtime variation is not controlled.",
             "- Positive change below means lower latency, not a guaranteed causal attribution.", "",
             "| Endpoint | Before ms | After ms | Reduction % | After <100 ms |",
             "|---|---:|---:|---:|---|"]
    for route in after["Routes"]:
        initial = original[route["Route"]]["MeanMs"]
        final = route["MeanMs"]
        lines.append(f"| {route['Route']} | {initial:.2f} | {final:.2f} | "
                     f"{100 * (initial - final) / initial:.1f}% | "
                     f"{'PASS' if final < 100 else 'FAIL'} |")
    output = after_path.with_name("comparison.md")
    output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    args = parser.parse_args()
    compare(args.before, args.after)
