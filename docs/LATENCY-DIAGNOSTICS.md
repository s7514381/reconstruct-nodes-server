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

## Offline unit-test evidence

`WeaponLatencyTests` executes the refine command with the real Dapper repository
and an in-memory ADO.NET provider, without backend configuration or Azure access.
The former recording baseline was **30 commands** for one affix and **41 commands**
for six affixes/seven values, including two full-inventory reads.
The optimized path uses **two SQL commands**, independent of affix/value count:
a short Snapshot target-only multi-result read and a conditional atomic write/receipt
batch. No Player row lock or extra provider-begin round trip is used on the standalone
path. The write rechecks the ledger and internal revision before committing; a stale
preparation is retried with the original request ID, at most three attempts.
Unchanged affix structure updates only changed values instead of recreating children.
An existing outer transaction remains owned by its caller. Replay needs only the read,
without rolls/debits. `RefineReadSnapshot` and `RefineWriteCommit` measure both phases.

These tests check command shape, caller-owned transaction, replay, conflict, and failure
propagation, not actual SQL execution, persisted values, network latency or SQL locks.
True SQL tests separately cover replay and a failure injected after the material debit.
Do not convert command counts into claimed measured API milliseconds.

`RequestTimingTests` separately verifies that connection/operation semaphore limits
can queue a request before its SQL command starts and record the respective acquisition
metric. It uses explicit completion signals rather than simulated multi-second sleeps.
This tests application-side queuing, not the SQL Player-row lock.

```powershell
dotnet test .\reconstruct-nodes-server.Tests\reconstruct-nodes-server.Tests.csproj --no-restore --filter "FullyQualifiedName~WeaponLatencyTests|FullyQualifiedName~RequestTimingTests"
```

These tests establish an avoidable round-trip risk and the meaning of the queue
metrics. Confirming the cause of a particular Azure latency spike still requires
runtime request timing and, where necessary, separately authorized SQL diagnostics.

## Manual local reproduction

The `latency` launch profile enables the existing request timing on the local backend;
the other launch profiles remain unchanged. Select `latency` in the IDE, or run:

```powershell
dotnet run --project .\reconstruct-nodes-server\reconstruct-nodes-server.csproj --launch-profile latency
```

Stop your existing backend before restarting on the same port. This profile uses the
same configuration sources and database as the normal `http` profile; it does not
create an isolated database or player. Normal startup and gameplay can still write to
the configured Azure services. Do not run automated fixtures or replay gameplay
requests against that database without separate authorization.

Using the game client, collect these scenarios separately:

1. Normal background saves, kill reports and polling, without refining.
2. A refine with those background requests temporarily paused, if the client supports it.
3. A refine with the normal background requests enabled.

Use comparable weapon data and note affix/value counts. Refining consumes materials
and changes the weapon; do not retry an uncertain write just to obtain a sample.
If background requests cannot be paused, report that limitation rather than treating
the run as an isolated measurement.

For each relevant request, capture its route, start time/duration, HTTP status and
`X-Request-Timing-Id` from the client's network inspector. Match that ID to the
console JSON event with `Kind: "RequestTiming"` and preserve its `Metrics`, including
nearby normal requests. The timing event intentionally contains no route or player ID;
adjacent console lines are not a reliable correlation method under concurrency.
Do not share Authorization/Encode headers, bodies, credentials or raw player IDs.

Compare SQL counts and transaction duration first, then acquisition waits and
connection-open duration. A slow `PlayerLockCommand` is only evidence for further
investigation, not proof of SQL blocking: it measures the entire matching command.
For SQL batches containing `BEGIN TRAN`, the repository's `TransactionLifetime`
metric may be absent even though the batch uses a transaction. Confirm real blocking
with separately authorized SQL wait/session diagnostics if needed.

After capture, restart using your usual profile to return to the default timing-off
behavior, provided `API_REQUEST_TIMING` is not also set in the parent environment.
No transaction, locking or concurrency-limit behavior changes in this profile.

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
By default the tool builds against the current backend source. For an immutable capture,
publish the current source first and compile the tool with the absolute MSBuild
`Baseline` path pointing to that same capture. No historical artifacts are required.
Captured build directories can contain private deployment configuration and must remain
local; do not publish or register the capture itself.

```powershell
# Offline tool verification only: no configuration, DB or HTTP access.
dotnet run --project tools/LatencyBaseline -- --self-test
python -B -m unittest discover -s tools -p test_summarize_latency.py -v

# Build a private capture from current source (does not start the backend).
$capture = Join-Path $PWD "tools/LatencyBaseline/bin/capture"
dotnet publish reconstruct-nodes-server -c Release -o $capture
# Check publish succeeded before proceeding; never copy/print capture configuration.

# Read-only: loads backend config/User Secrets/environment privately, verifies the target,
# then reports target metadata and ten SELECT 1 RTTs. No credentials are printed.
dotnet run --project tools/LatencyBaseline -p:Baseline="$capture" -- "$capture"

# Only after explicit authorization for disposable-player writes on that exact database:
dotnet run --project tools/LatencyBaseline -p:Baseline="$capture" -- "$capture" --run

# Supplemental contention scenario in a fresh disposable fixture, same captured DLL:
dotnet run --project tools/LatencyBaseline -p:Baseline="$capture" -- "$capture" --run --concurrent-only

# Item command regressions only; requires the same explicit DB write authorization.
# Rebuild the capture from current source after ItemService changes.
dotnet run --project tools/LatencyBaseline -p:Baseline="$capture" -- "$capture" --run --item-only

# Only the five snapshot read workloads, on a rich fresh fixture:
dotnet run --project tools/LatencyBaseline -p:Baseline="$capture" -- "$capture" --run --reads-only

# Only the two economy workloads, with separate command regressions:
dotnet run --project tools/LatencyBaseline -p:Baseline="$capture" -- "$capture" --run --economy-only

# Only the three snapshot Save workloads, with separate snapshot regressions:
dotnet run --project tools/LatencyBaseline -p:Baseline="$capture" -- "$capture" --run --saves-only

# Only single/ten Shop draws, with separate replay/receipt regressions:
dotnet run --project tools/LatencyBaseline -p:Baseline="$capture" -- "$capture" --run --shop-only

# Small, targeted refine/background contention diagnostic, NOT a 100-sample baseline:
dotnet run --project .\tools\LatencyBaseline -p:Baseline="$capture" -- "$capture" --run --refine-only

```

### Targeted refine contention diagnostics

`--run --refine-only` requires the same explicit authorization for disposable-player
writes on the verified SQL target and is mutually exclusive with the other modes.
It uses the captured backend, owned loopback HTTP process and in-memory Azurite,
and Run's exact-player cleanup with zero-residue verification. No schema changes,
existing-player writes or synthetic lock holds are performed. Product-level bounded
resource conflict retries remain enabled, with their original request IDs.

The new player is initialized through GameProgress/Initialize; only this exact-owned
disposable fixture's wave/max-wave parameters are advanced to 100 for legal kill load.
It receives a legal six-affix/seven-value rare sword and iteration-scaled tuning crystals.
The runner first verifies concurrent same-ID replay and distinct-ID overlapping refines.
It then performs sequential groups and a five-request background/read contention case
(Refine, GetProgress, ReportKill, AdjustAmount, StartWave, GetGold), followed by a normal
single BackgroundSync batch (kill, wave start, item delta) alongside Refine and GetGold.
Every write uses a fresh intent where applicable. Responses verify exact material
debits, item increments and kill rewards; final readbacks verify balances, saved
roads, exact persisted weapon values and exactly one receipt per distinct intent.

The refine-only backend receives a unique SQL Application Name. A bounded observer
reads DMV wait metadata only for that owned backend. If a positive blocking session
exists, it must also belong to that backend; unblocked/nonpositive-blocker waits such
as WRITELOG are included. No SQL text, parameters or player identity is retrieved.
Permission errors are explicitly recorded as `permission-denied`, not interpreted as
zero waits. A successful query with no rows also cannot prove no transient waits.
The observer adds diagnostic query load; this is not a clean throughput benchmark.

The safe `baseline.json` report contains `RefineDiagnostics` (75 route observations at five iterations,
wait samples, sampling status and readback status), `RefineSummary` and
`RefineDiagnosticsComplete`. Summaries exclude the first group in each scenario and
contain only four samples per route, so they are diagnostic evidence rather than
stable p95 estimates or proof of meeting a latency budget. Wait samples are snapshots,
not distinct blocking incidents; do not sum repeated wait durations.
The legacy full-18-workload completion/budget flags remain false.

For a larger API-only diagnostic, set `REFINE_DIAGNOSTIC_ITERATIONS=101` before
running `--refine-only`, then remove it afterwards. Allowed values are 5 through 101.
Each scenario excludes its first iteration, yielding 100 fresh successful refines
per scenario at 101 iterations. Material provisioning scales accordingly; the
`concurrent` case deliberately dispatches five separate background/read requests and
samples owned SQL waits. The `background-batch` case dispatches one normal combined
BackgroundSync plus GetGold. Neither is a click-to-render benchmark.
Use `RefineDiagnostics.Iterations` when interpreting completion and sample counts.
No 200ms success claim may be made from the completion flag: inspect measured p95
and perform the actual frontend click-to-render validation separately.

On the authorized development database, the original six-affix path had about
2,067ms mean / 2,333ms maximum over four warmed sequential diagnostic samples.
After batching, a separate 100-sample warmed HTTP run measured 163.1ms mean,
156.6ms p50, **214.5ms p95**, and 245.0ms maximum for sequential refines.
The concurrent five-request stress case measured 357.2ms mean / 778.3ms p95.
The latter is not the normal Godot single-background-batch workload. Provider
begin averaged 45.5ms, locked read/prepare 49.5ms, and write/commit 67.3ms in
the sequential run. These measurements demonstrate improvement but **not**
the requested 200ms click-to-render p95. All requests used fresh IDs; final
material/receipt readbacks and exact fixture cleanup passed.

After removing Player locks and using separate short snapshot-read/conditional-write
batches, a 100-sample-per-scenario HTTP run measured sequential p95 167.6ms and normal
single-background-batch p95 628.4ms. A subsequent changed-values-only version measured:

| Scenario | Samples after warm-up | HTTP mean | HTTP p95 |
|---|---:|---:|---:|
| Sequential | 100 | 128.1ms | 242.3ms |
| Five-request background/read stress | 100 | 397.6ms | 782.9ms |
| One BackgroundSync batch plus GetGold | 100 | 193.8ms | 479.1ms |

These are separate runs, not paired samples proving a regression or stable percentile.
The changed-values version's normal-batch read averaged 56.1ms and write/commit
136.9ms (maximum 630.4ms); no refine resource retries occurred. A later five-iteration
diagnostic observed WRITELOG waits on owned requests, but this small sample cannot
attribute every refine tail to log I/O. The latest value-update runs verified exact material debits,
same-ID replay, distinct-ID overlap, persisted weapon values and zero fixture residue.
**The 200ms p95 goal remains unmet; actual Godot click-to-render is still unmeasured.**

Use this mode's JSON fields directly; the general report summarizer's existing modes
do not render the refine-specific observations. Passing self-tests checks the legal
refine fixture and rejects incomplete diagnostic completion without contacting SQL.

Every run creates a random `core-http-fixture-*` guest and isolated loopback backend /
in-memory Azurite processes. All POSTs carry a fresh real `Encode.New()` header and,
after login, the fixture's Authorization token. No production API server is used.
The seed uses CORE3 with its valid starting skill, an eight-tile entrance-to-castle
road, a castle-only (no build-spot deployment) layout, one staff, a diamond inventory
row, talent version 1 and all six client achievement groups (33 item targets).
CORE3 is deliberately not put in `deployment_log`: only kinds 0..2 may be deployed.
Fixture funding comes from unique legal waves/kills, not arbitrary gold SQL updates.
Outside measured samples, fresh sessions/enemy indices fund at least 101,000 gold
for 101 single draws (100 each) and 101 ten-draws (900 each). Every funding reward
and draw debit is checked exactly; there are no retries or replayed receipts.

The original 15 routes are Session/Validate, Character/GetList, Character/Save,
Shop/GetGold, Shop/GetCatalog, GameProgress/Get, GameProgress/Save,
Talent/GetSnapshot, Item/GetInStockList, Item/AdjustAmount, Weapon/GetInstanceList,
AchievementProgress/Get, AchievementProgress/Save, Economy/StartWave, Economy/ReportKill.
The runner now measures **18 independent workloads**: those 15 plus Shop/Draw
count=1, Shop/Draw count=10 and Talent/Save with utility gold_1/gold_2/gold_3.
Draws run after the original read assertions, since draws change owned inventory.
Talent saves advance revisions and read back all three nodes.
Every call checks HTTP and business success; additional assertions cover owned core ID,
saved revision, road count, nonempty seeded collections, exact inventory increments,
fresh wave session/count and exact gold rewards. Revision-bearing saves advance their
expected revision; wave/kill/progress requests use fresh IDs rather than replaying receipts.

Reports are persisted after each completed route and after cleanup as
`artifacts/latency-report-<UTC timestamp>/baseline.json`: first request separately,
100 samples per endpoint, independent mean/budget result, ten direct SQL RTTs,
request-correlated breakdown and cleanup confirmation. Raw subprocess output is drained
without logging; only known diagnostic events are retained.
Legacy `MeanMs`, `Below100Ms`, `AllMeansBelow100Ms` remain HTTP-mean fields.
New per-route `Backend` and `Http` contain mean/P50/P95/max (nearest rank);
`Backend.P95Below100Ms` and `AllBackendP95Below100Ms` use strict **P95 <100 ms**.
Both distributions exclude the first request. HTTP is transport evidence, not the
middleware objective. Each draw count must pass independently; never pool workloads.
After owned-process output is drained, first and measured samples must have unique,
successful matching timing records and finite nonnegative ElapsedMs. Missing/duplicate
correlation fails the run; partial evidence is still persisted and `Complete` stays false.

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
python -B tools/summarize_latency.py tools/LatencyBaseline/bin/latency-report-<timestamp>/baseline.json "$capture"
```

The summarizer derives statistics from raw samples and validates correlation (including
first requests), so legacy JSON needs no new aggregate fields. It rejects incomplete
correlation rather than substituting HTTP timings or zero middleware time. Reports do
not include the database target/configuration. Captures themselves remain private.

### Item command regressions

`--run --item-only` provisions a fresh disposable guest and runs only Item regressions,
without the 18-workload timing run or unrelated snapshot seeding. It is mutually
exclusive with `--concurrent-only` and `--regression-only`. Checks cover missing-item
decrement rejection, Unicode blank category replacement, insufficient decrement,
zero/tombstone recreation, int overflow rollback, ten concurrent `List<Task<int>>`
increments, and `AdjustAmount` participation in an outer SQL transaction rollback.
The HTTP route uses the current product `AdjustAmountCommand`; the composed check
calls `AdjustAmount` inside the fixture repository transaction. No product code is replaced.

`baseline.json` persists `Mode`, `ItemCommandChecks`, expected HTTP status by correlation
ID (including intentional 400 rejections), `ItemRegressionsComplete` and cleanup status.
The same summarizer produces `item-regressions.md`. `Complete` is reserved for all
18 latency workloads **plus final readbacks, valid correlation and successful cleanup**;
an Item-only run never claims that unmeasured workloads are complete. Intentional
rejections are command-contract evidence, never timing samples for the latency budget.

### Five snapshot read workloads

`--run --reads-only` is mutually exclusive with the other `--*-only` modes. It measures
only Character/GetList, GameProgress/Get, Talent/GetSnapshot, Weapon/GetInstanceList
and AchievementProgress/Get: one first request plus 100 measured calls per route.
It still provisions/writes a disposable fixture; it is **not a read-only DB permission mode**.
Run it only after explicit authorization, with a capture rebuilt from the current source.

The representative fixture has an equipped CORE3, an unarmed character with ordered
skill tree/node rows, an ordinary spare staff and two rich weapons (one equipped,
one unequipped) with distinct multi-value rolled affixes, an unrolled affix and unique
values. The additional character/skill seed rows are inserted in one transaction after
checking the exact newly created guest identity; they use the existing precise cleanup.
Talent includes three acquired nodes and true/false legacy nodes; achievements include
six groups, 33 targets, two nonempty collected lists and an actually claimed reward date;
the game progress includes eight ordered road rows.

Before timing, the tool verifies these rich values and obtains all five **legacy service
Get** snapshots inside an outer repository transaction. It materializes/serializes them
before rollback releases the player lock; no HTTP is sent while that lock is held.
Every first and measured HTTP response must deep-equal its corresponding legacy snapshot,
including revisions, equipment, nested values, array ordering, collections and null fields.
Only safe check labels and timings are persisted, never fixture payloads or configuration.
The Item and read regressions share the same lazy service-argument initializer.

`Mode=reads`, `ExpectedWorkloads=5`, `ReadRegressionChecks`,
`ReadRegressionsComplete` and `SelectedWorkloadsComplete` distinguish this run from
the full baseline. Both completion flags require all five exact routes, all five checks,
valid timing correlation and successful cleanup. Legacy `Complete` stays false because
the full 18-workload run was not performed. `AllSelectedBackendP95Below100Ms` covers
the five selected routes; the existing all-18 aggregate stays false. The summarizer
renders these distinctions and the consistency checks in `report.md`.

Offline `--self-test` checks route selection/completion and the deep comparator
(changed/missing/null values and changed array ordering must fail), without DB or HTTP.

Unarmed characters follow `GameProgressEquipmentModel`'s actual wire contract:
`weapon_entry_id`, `weapon_id` and `weapon_rarity` are empty strings (not null),
affixes/unique are empty collections, and weapon quality version is null.
The rich seed's Kind 0 character uses its default skill 5 and always-available active
skill 0, not locked skill 2 or a fabricated auxiliary loot ID. Separate fixture
assertions distinguish count, unarmed state, equipped identity/rarity and preserved
skills before HTTP comparison; these assertions do not change product rules.

### Economy-only and saves-only

These modes use the same capture, explicit write authorization, owned processes, fresh
guest and exact transactional cleanup as the other modes. All `--*-only` modes are
mutually exclusive and require `--run`. No extra outer transaction is added around HTTP
commands. Rebuild the private backend capture to include current product changes before use.

Current background workloads all POST `GameProgress/BackgroundSync`. Report labels
include the section, such as `GameProgress/BackgroundSync (kills)`, not a separate route.
Earlier measurements below referring to the removed Save/StartWave/ReportKill/AdjustAmount
routes are historical evidence, not current endpoints or equivalent batch performance claims.

`--economy-only` times separate wave_start and kills BackgroundSync workloads (first + 100 each).
Outside measurement it verifies:

- StartWave and ReportKill exact receipt replay, one ledger row and no extra gold.
- Same enemy under a different request ID rejected with no gold or receipt.
- Same ID/different payload returns **409** with `backend.economy.request_conflict`.
- A legal unclaimed kill in the old session still rewards after starting a new wave.
- Gold talents support true/false and positive/zero/negative integer values, counting
  each enabled node's bonus once rather than multiplying it by the level.
- Malformed string/fraction/object/JSON talent values reject without committing gold
  or a receipt. Repairing the fixture then resubmitting the exact rejected intent works.

Boolean/malformed talent rows are seeded directly, only under the exact new guest and
its fixture-owned Talent ID, since background talent input rejects noninteger acquired
nodes. The fixture repairs them to zero before timing. Measured kills use unique IDs and
indices 0..100 of a wave-100 session, so each rewards exactly 108 without a boss/talent bonus.
Replays and intentional rejections are never included in latency samples.

`--saves-only` times progress, achievement and talent BackgroundSync workloads
(first + 100 each). It reuses `--regression-only`'s claim/date/ordering, protected
gold/unlock, and repository rollback checks, including the existing explicit
NOT EXERCISED label if no bounded column permits the optional HTTP mid-write failure.
Additional checks require:

- All three background snapshots advance revisions once; stale input merges evidence
  without decreasing counters or revoking newer foreground-owned state.
- Exact progress request replay returns
  the original revision receipt without another debit or revision advance.
- Same progress ID/different payload is rejected with **409** and
  `backend.economy.request_conflict`, leaving snapshot/revision/gold unchanged.
- Background deployment replacements are ignored; deployment remains a foreground command.
  The background input cannot move characters or debit deployment gold.

The three measured saves reread current revisions after regressions, advance expected
revisions on every call, use unique progress request IDs, and verify final readbacks
including retained claim flags/dates and three acquired plus two legacy talent nodes.
No write retries are performed except deliberate, asserted regression replays.

`Mode=economy` / `saves` and `ExpectedWorkloads=2` / `3` identify the selection.
`SelectedWorkloadsComplete` requires all exact selected routes, completed regressions
and final readbacks, valid correlation, and successful cleanup; legacy `Complete`
remains false because the full 18-workload baseline was not run.
`SelectedRegressionChecks`, `SelectedRegressionsComplete` and `RegressionCorrelations`
persist safe check labels and expected HTTP statuses (including intentional 400/409).
The existing summarizer validates these correlations and renders a selection-specific
`report.md`, retaining separate middleware P95 and HTTP distributions.

### Shop-only (final selected mode)

`--run --shop-only` measures only `Shop/Draw (count=1)` and `Shop/Draw (count=10)`,
one first plus 100 measured calls each. It uses the same selected-mode completion,
safe correlation/reporting and precise fixture cleanup as economy/saves. It is mutually
exclusive with the other `--*-only` modes and still requires explicit DB write authorization.

Before timing, unique real non-boss wave-100 kills fund
`102 * (SingleCost + TenCost)` (currently 102,000 gold): 101 measured/first requests plus
one regression request of each count. Replays and conflicts do not consume the budget.
Funding is shared with the full baseline and excludes boss index 306, whose reward is
864 rather than the 108 asserted for non-boss indices. No SQL balance override or rejected
draw is used for funding. The capture's catalog supplies both costs.

An initial ten-draw over three possible kinds guarantees both a first acquisition and
same-batch duplicates, without controlling randomness or retrying successful rolls.
For both count=10 and count=1 the regressions verify:

- The full replay receipt equals the original result; gold/characters/weapons/shards
  remain unchanged and exactly one command ledger row exists.
- Reusing the request ID with changed Count returns 409 / `backend.economy.request_conflict`,
  with the same authoritative state and single receipt.
- First-acquired character IDs, default skills (0→5, 1→0, 2→3), equipped weapon IDs,
  common rarity/quality version, and corresponding weapon inventory entries read back correctly.
- Duplicate results reference the existing character, do not create another weapon, and
  increment the correct shard. Repeated kinds within one batch contribute to the final
  absolute `state.shards` amount exactly.

Every measured receipt is validated with a running expected inventory; authoritative
character/weapon/item/gold readback follows each completed workload, outside timing.
Replays, conflicts, funding and readbacks never become latency samples.
`Mode=shop`, `ExpectedWorkloads=2`, `SelectedRegressionChecks`,
`SelectedRegressionsComplete`, `SelectedWorkloadsComplete` and the selected backend-P95
aggregate follow the existing modes; legacy full-baseline `Complete` remains false.
