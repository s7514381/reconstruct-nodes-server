# Fixture-only HTTP latency diagnostics

Set `API_REQUEST_TIMING=1` only on an owned backend process. Default is off.
The existing middleware stopwatch boundary remains after request buffering and before
Azure transfer. The additional structured console event has only server-generated
correlation, elapsed time, HTTP status and aggregate counts/times. Match its RequestId
to `X-Request-Timing-Id`; no SQL, parameters, token, body or player identity is added.
Existing Azure request logging behavior is unchanged (not made safe by this switch).
Use the existing CoreHttpIntegrationTests owned in-memory Azurite pattern, not real Azure.

Metrics overlap and must not be summed: Sql counts attempted repository commands,
including failures; QueryMultiple measures initial execution, not deferred reader consumption.
GateWait, ConnectionLimitWait, OperationLimitWait and ConnectionOpen are acquisition
times. PlayerLockCommand is the elapsed SQL command matching the existing
`FROM Player WITH (UPDLOCK...)` pattern, **not an exact SQL Server lock wait**:
it includes execution/network time. TransactionLifetime is begin-success through disposal
(including application gaps/commit); it is not just active SQL time.
Direct use of repository.Connection bypasses these command measurements.

## Runner

No production config is read. No login, fixture creation, seeding or cleanup is implicit.
First provision an explicitly authorized local/dedicated fixture using the existing
CoreHttpIntegrationTests safety/owned-process/precise-cleanup pattern; keep it alive
for the run and guarantee cleanup in the provisioning harness's finally.
Never use an existing player/session. Do not enable the SQL fixtures against an unknown DB.

Create an **untracked** plan containing fixture credentials only:

```json
{
  "base_url": "http://127.0.0.1:OWNED_PORT",
  "fixture_id": "core-http-fixture-OWNED_ID",
  "endpoints": [
    {
      "path": "/EXACT_ROUTE_FROM_CLIENT_LOG",
      "body": {},
      "status": 200,
      "expect": [{"path": ["Success"], "equals": true}]
    }
  ]
}
```

Supply all **15 distinct actual client-log routes**, correct fixture-only bodies,
and explicit response assertions per route. A body string can contain `{sample}`
(0 for first request, 1..100 measured) for operations requiring unique request IDs.
Provision enough resources for all 101 calls per endpoint; do not replace business
operations with rejected requests or replayed deduplication receipts to meet the budget.
Operations requiring state-dependent bodies need a dedicated fixture adapter before use.

```powershell
$env:LATENCY_DATABASE_APPROVAL = "dedicated-test-only"
python tools/latency_runner.py <untracked-fixture-plan.json>
```

The runner uses real HTTP, validates every response, separates the first request per
endpoint, and takes exactly **100 additional samples per endpoint**. Each mean must be
strictly below 100 ms; no pooled mean. Exit 1 means budget failure, 2 means invalid
fixture/HTTP/business response. No retry after uncertain writes. Output contains
timings/correlation IDs only, never bodies or credentials. First-request measurements
are not guaranteed process-cold (use separate backend restarts to establish true cold start).

## Captured baseline fixture runner (Windows)

`tools/LatencyBaseline` is the concrete, disposable-player runner for the subsequently
authorized SQL target `tcp:timwu19950429.database.windows.net,1433` / `reconstruct-nodes`.
It references DLLs in an existing captured build, not domain source projects, so host
optimizations cannot silently change the baseline. Its default capture path is
`artifacts/latency-baseline-20261015-225043`; override MSBuild `Baseline` for another capture.
Captured build directories can contain private deployment configuration and must remain
local; do not publish or register the capture itself.

```powershell
# Read-only: loads backend config/User Secrets/environment privately, verifies the target,
# then reports target metadata and ten SELECT 1 RTTs. No credentials are printed.
dotnet run --project tools/LatencyBaseline -- artifacts/latency-baseline-20261015-225043

# Only after explicit authorization for disposable-player writes on that exact database:
dotnet run --project tools/LatencyBaseline -- artifacts/latency-baseline-20261015-225043 --run

# Supplemental contention scenario in a fresh disposable fixture, same captured DLL:
dotnet run --project tools/LatencyBaseline -- artifacts/latency-baseline-20261015-225043 --run --concurrent-only
```

Every run creates a random `core-http-fixture-*` guest and isolated loopback backend /
in-memory Azurite processes. All POSTs carry a fresh real `Encode.New()` header and,
after login, the fixture's Authorization token. No production API server is used.
The seed uses CORE3 with its valid starting skill, an eight-tile entrance-to-castle
road, a castle-only (no build-spot deployment) layout, one staff, a diamond inventory
row, talent version 1 and all six client achievement groups (33 item targets).
CORE3 is deliberately not put in `deployment_log`: only kinds 0..2 may be deployed.
Fixture funding comes from a unique legal wave/kill, not arbitrary gold SQL updates.

The exact 15 routes are Session/Validate, Character/GetList, Character/Save,
Shop/GetGold, Shop/GetCatalog, GameProgress/Get, GameProgress/Save,
Talent/GetSnapshot, Item/GetInStockList, Item/AdjustAmount, Weapon/GetInstanceList,
AchievementProgress/Get, AchievementProgress/Save, Economy/StartWave, Economy/ReportKill.
Every call checks HTTP and business success; additional assertions cover owned core ID,
saved revision, road count, nonempty seeded collections, exact inventory increments,
fresh wave session/count and exact gold rewards. Revision-bearing saves advance their
expected revision; wave/kill/progress requests use fresh IDs rather than replaying receipts.

Reports are persisted after each completed route and after cleanup as
`artifacts/latency-report-<UTC timestamp>/baseline.json`: first request separately,
100 samples per endpoint, independent mean/budget result, ten direct SQL RTTs,
request-correlated breakdown and cleanup confirmation. Raw subprocess output is drained
without logging; only known diagnostic events are retained.

Cleanup stops only owned processes and deletes only the random fixture's rows, inside
one transaction. It verifies every `CreateUserId` table and the exact ClientId has zero
residue before commit. Unknown ownership tables with residue cause rollback, not broad
deletion. No migrations or schema changes are performed.

The baseline is sequential per route. `--concurrent-only` runs a separate scenario:
GameProgress/Save plus ReportKill, then AchievementProgress/Save plus ReportKill,
each with one first pair and ten measured concurrent pairs. It verifies exact rewards
and final authoritative gold / achievement item preservation. Its report has no
100-sample endpoint results and must not be pooled with the independent-route baseline.
Neither mode measures exact server lock waits. Partial reports are explicitly
incomplete; a protocol test against a synthetic server is not backend performance evidence.

Render the independent-route JSON into a readable report without loading secrets:

```powershell
python -B tools/summarize_latency.py artifacts/latency-report-<timestamp>/baseline.json artifacts/latency-baseline-20261015-225043
```
