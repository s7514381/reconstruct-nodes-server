using System.Data.SqlClient;
using System.Text.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

internal static partial class Fixture
{
    private sealed record RefineObservation(string Scenario, int Iteration, string Route, Sample Sample);
    private sealed record OwnedWait(int Iteration, int SessionId, int BlockingSessionId, string WaitType, int WaitTimeMs);
    private sealed class RefineDiagnostics
    {
        public int Iterations { get; set; } = 5;
        public List<RefineObservation> Observations { get; } = [];
        public List<OwnedWait> Waits { get; } = [];
        public string WaitSamplingStatus { get; set; } = "not-started";
        public int? WaitPermissionError { get; set; }
        public int WaitPolls { get; set; }
        public bool ReadbacksVerified { get; set; }
    }
    private sealed class WaitRow
    {
        public int SessionId { get; set; }
        public int BlockingSessionId { get; set; }
        public string WaitType { get; set; } = "";
        public int WaitTimeMs { get; set; }
    }
    private static readonly string[] RefineRoutes =
    [
        "Weapon/Refine", "GameProgress/Get", "GameProgress/BackgroundSync (kills)",
        "GameProgress/BackgroundSync (items)", "GameProgress/BackgroundSync (wave_starts)", "Shop/GetGold",
    ];

    private static async Task InitializeRefineFixture(HttpClient http, DapperRepository repository,
        Guid player, string clientId, RefineDiagnostics diagnostics)
    {
        await Post(http, "GameProgress/Initialize", new { });
        Stage = "advance new refine fixture";
        // Only the freshly created, exact-owned fixture is advanced for the legal wave-100 workload.
        Require(await repository.ExecuteAsync("""
            UPDATE p SET Value = '100'
            FROM PlayerParams p INNER JOIN Player owner ON owner.Id = p.CreateUserId
            WHERE owner.Id = @Player AND owner.ClientId = @ClientId AND owner.IsClientLogin = 1
                AND p.[Group] = 'game_progress' AND p.Name IN ('wave', 'max_wave_reached') AND p.Status != -1;
            """, new { Player = player, ClientId = clientId }) == 2, "advance only owned fixture progression");
        Stage = "seed refine session";
        var wave = await PostBackground(http, new WaveStartRequest
            { RequestId = Guid.NewGuid(), Wave = 100, Difficulty = 0 });
        Guid session = wave.Data!["session_id"]!.GetValue<Guid>();
        Stage = "seed refine kill";
        await PostBackground(http, new EnemyKillRequest
            { RequestId = Guid.NewGuid(), SessionId = session, EnemyIndex = 306 });
        await PostBackground(http, new ItemDeltaRequest { ItemKey = "diamond", Delta = 1, Category = "currency" });
        var progress = (await Post(http, "GameProgress/Get", new { })).Data!.Deserialize<GameProgressModel>(Json)!;
        Stage = "refine diagnostics";
        TrackRegressionRequests = true;
        try { await DiagnoseRefine(http, repository, player, clientId, progress, session, await Gold(http), diagnostics); }
        finally { TrackRegressionRequests = false; }
    }

    private static WeaponInstanceModel RefineWeapon() => new()
    {
        Id = "latency-refine-sword", WeaponId = "sword", WeaponFamilyId = "sword", Rarity = "rare",
        Affixes = new[]
        {
            ("global_damage_t6", new[] { 5 }),
            ("physical_damage_t6", new[] { 5 }),
            ("added_physical_t6", new[] { 1, 3 }),
            ("range_t6", new[] { 4 }),
            ("recovery_t6", new[] { 4 }),
            ("critical_chance_t6", new[] { 1 }),
        }.Select(affix => JsonSerializer.SerializeToElement(new { id = affix.Item1, values = affix.Item2 })).ToList(),
    };

    private static bool RefineComplete(int observations, string stage, bool cleaned, bool correlated, int iterations = 5) =>
        observations == iterations * 15 && stage == "refine completed" && cleaned && correlated;

    public static void RefineSelfTest()
    {
        var seed = RefineWeapon();
        var tuned = RefinementRules.Refine(seed, "tuning_crystal");
        Require(tuned.Affixes.Count == 6 && tuned.Rarity == "rare" && tuned.Id == seed.Id,
            "legal six-affix tuning fixture");
        Require(RefineRoutes.Distinct().Count() == 6 &&
            RefineComplete(75, "refine completed", true, true) &&
            !RefineComplete(74, "refine completed", true, true) &&
            !RefineComplete(75, "refine diagnostics", true, true) &&
            !RefineComplete(75, "refine completed", false, true) &&
            !RefineComplete(75, "refine completed", true, false) &&
            RefineComplete(1515, "refine completed", true, true, 101) &&
            !RefineComplete(75, "refine completed", true, true, 101),
            "refine diagnostics completion requires all observations and cleanup/correlation");
        Console.WriteLine("Refine fixture and completion self-tests passed; no database or HTTP access.");
    }

    private static async Task DiagnoseRefine(HttpClient http, DapperRepository repository, Guid player,
        string clientId, GameProgressModel progress, Guid session, long gold, RefineDiagnostics diagnostics)
    {
        Require(await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM Player WHERE Id=@Player AND ClientId=@ClientId AND IsClientLogin=1",
            new { Player = player, ClientId = clientId }) == 1, "exact new refine fixture identity");
        await PostBackground(http, new WeaponAcquireRequest
            { RequestId = Guid.NewGuid(), Weapon = RefineWeapon() });
        string? configured = Environment.GetEnvironmentVariable("REFINE_DIAGNOSTIC_ITERATIONS");
        if (configured != null)
        {
            Require(int.TryParse(configured, out int iterations) && iterations is >= 5 and <= 101,
                "refine iterations must be between 5 and 101");
            diagnostics.Iterations = iterations;
        }
        int seedMaterials = 3 * diagnostics.Iterations + 4;
        int materials = (await PostBackground(http, new ItemDeltaRequest
            { ItemKey = "tuning_crystal", Delta = seedMaterials, Category = "crystal" })).Data!.GetValue<int>();
        Require(materials == seedMaterials, "fresh tuning material seed");
        int diamond = 1, enemy = 0;
        var receipts = new List<Guid>();
        WeaponInstanceModel? expectedWeapon = null;

        var duplicate = new WeaponRefineRequest
        {
            RequestId = Guid.NewGuid(), WeaponEntryId = RefineWeapon().Id, CrystalId = "tuning_crystal",
        };
        Stage = "simultaneous duplicate refine";
        var replays = await Task.WhenAll(Post(http, RefineRoutes[0], duplicate), Post(http, RefineRoutes[0], duplicate));
        AssertSnapshotEqual(replays[0].Data, replays[1].Data);
        materials--;
        Require(replays[0].Data!.Deserialize<CommandReceipt<WeaponRefineSnapshot>>(Json)!.Snapshot.MaterialCount == materials,
            "simultaneous same-ID refine debits only once");
        receipts.Add(duplicate.RequestId);
        var overlapping = Enumerable.Range(0, 2).Select(_ => new WeaponRefineRequest
        {
            RequestId = Guid.NewGuid(), WeaponEntryId = RefineWeapon().Id, CrystalId = "tuning_crystal",
        }).ToArray();
        Stage = "simultaneous distinct refine";
        var distinct = await Task.WhenAll(overlapping.Select(request => Post(http, RefineRoutes[0], request)));
        Require(distinct.Select(result => result.Data!.Deserialize<CommandReceipt<WeaponRefineSnapshot>>(Json)!.Snapshot.MaterialCount)
            .Order().SequenceEqual(new[] { materials - 2, materials - 1 }), "distinct concurrent refines retain both debits");
        receipts.AddRange(overlapping.Select(request => request.RequestId));
        materials -= 2;

        for (int phase = 0; phase < 3; phase++)
        {
            string scenario = phase == 0 ? "sequential" : phase == 1 ? "concurrent" : "background-batch";
            Stage = "refine " + scenario;
            for (int iteration = 0; iteration < diagnostics.Iterations; iteration++)
            {
                var refine = new WeaponRefineRequest
                {
                    RequestId = Guid.NewGuid(), WeaponEntryId = RefineWeapon().Id, CrystalId = "tuning_crystal",
                };
                var kill = new EnemyKillRequest
                    { RequestId = Guid.NewGuid(), SessionId = session, EnemyIndex = enemy++ };
                var wave = new WaveStartRequest { RequestId = Guid.NewGuid(), Wave = 100, Difficulty = 0 };
                // Kills are tracked in the wave bitmap and leave no per-kill receipt row.
                receipts.AddRange([refine.RequestId, wave.RequestId]);
                // Kills are accepted only for the newest few waves, so the next iteration targets this wave.
                session = wave.RequestId;
                var actions = new Func<Task<Sample>>[]
                {
                    async () =>
                    {
                        var result = await Post(http, RefineRoutes[0], refine);
                        var receipt = result.Data!.Deserialize<CommandReceipt<WeaponRefineSnapshot>>(Json)!;
                        expectedWeapon = receipt.Snapshot.Weapon;
                        Require(receipt.RequestId == refine.RequestId &&
                            receipt.Snapshot.Weapon.Id == refine.WeaponEntryId &&
                            receipt.Snapshot.Weapon.Rarity == "rare" &&
                            receipt.Snapshot.Weapon.Affixes.Count == 6 &&
                            receipt.Snapshot.MaterialCount == materials - 1, "exact refine debit and snapshot");
                        return result.Sample;
                    },
                    async () => (await Post(http, "GameProgress/Get", new { })).Sample,
                    async () =>
                    {
                        var result = await PostBackground(http, kill);
                        Require(result.Data!["gold"]!.GetValue<long>() == gold + 108, "exact diagnostic kill reward");
                        return result.Sample;
                    },
                    async () =>
                    {
                        var result = await PostBackground(http,
                            new ItemDeltaRequest { ItemKey = "diamond", Delta = 1, Category = "currency" });
                        Require(result.Data!.GetValue<int>() == diamond + 1, "exact concurrent item increment");
                        return result.Sample;
                    },
                    async () =>
                    {
                        var result = await PostBackground(http, wave);
                        Require(result.Data!["session_id"]!.GetValue<Guid>() == wave.RequestId &&
                            result.Data["enemy_count"]!.GetValue<int>() == 307, "fresh diagnostic wave");
                        return result.Sample;
                    },
                    async () => (await Post(http, RefineRoutes[5], new { })).Sample,
                };
                string[] routes = RefineRoutes;
                if (phase == 2)
                {
                    var batch = new BackgroundSyncRequest
                    {
                        RequestId = Guid.NewGuid(), Kills = [Kills(kill.SessionId, kill.EnemyIndex)], WaveStart = wave,
                        Items = [new() { ItemKey = "diamond", Delta = 1, Category = "currency" }],
                    };
                    actions =
                    [
                        actions[0],
                        async () =>
                        {
                            var result = await Post(http, "GameProgress/BackgroundSync", batch);
                            var data = result.Data!.Deserialize<BackgroundSyncResponse>(Json)!;
                            Require(data.Gold == gold + 108 &&
                                data.Session!.SessionId == wave.RequestId && data.Progress != null,
                                "one normal background batch preserves all resource changes");
                            progress.Revision = data.Progress!.Revision;
                            progress.ExpectedRevision = data.Progress.Revision;
                            return result.Sample;
                        },
                        actions[5],
                    ];
                    routes = ["Weapon/Refine", "GameProgress/BackgroundSync (batch)", "Shop/GetGold"];
                }
                Sample[] samples;
                if (phase == 0)
                {
                    var sequential = new List<Sample>();
                    foreach (var action in actions) sequential.Add(await action());
                    samples = sequential.ToArray();
                }
                else
                {
                    var pending = Task.WhenAll(actions.Select(action => action()));
                    await Task.WhenAll(pending, ObserveOwnedWaits(repository, clientId, iteration, pending, diagnostics));
                    samples = await pending;
                }
                for (int route = 0; route < samples.Length; route++)
                    diagnostics.Observations.Add(new(scenario, iteration, routes[route], samples[route]));
                materials--;
                diamond++;
                gold += 108;
            }
        }

        Require(await Gold(http) == gold, "diagnostic final authoritative gold");
        var persistedWeapons = (await Post(http, "Weapon/GetInstanceList", new { })).Data!
            .Deserialize<List<WeaponInstanceModel>>(Json)!;
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(expectedWeapon),
            JsonSerializer.SerializeToNode(persistedWeapons.Single(weapon => weapon.Id == RefineWeapon().Id)));
        var items = (await Post(http, "Item/GetInStockList", new { })).Data!.AsArray();
        Require(items.Single(row => row!["ItemKey"]!.GetValue<string>() == "tuning_crystal")!["Amount"]!.GetValue<int>() == materials &&
            items.Single(row => row!["ItemKey"]!.GetValue<string>() == "diamond")!["Amount"]!.GetValue<int>() == diamond,
            "diagnostic final material and item balances");
        var saved = (await Post(http, "GameProgress/Get", new { })).Data!.Deserialize<GameProgressModel>(Json)!;
        Require(saved.Revision >= progress.Revision && saved.Wave == 100 &&
            saved.Battlefield.Roads.Count == progress.Battlefield.Roads.Count,
            "diagnostic saved revision and roads preserved");
        var achievement = (await Post(http, "AchievementProgress/Get", new { })).Data!.Deserialize<AchievementProgressModel>(Json)!;
        Require(achievement.Groups.Single(g => g.Name == "enemy_kills").Total >= enemy &&
            achievement.Groups.Single(g => g.Name == "weapons_collected").Collected.Contains("sword"),
            "achievement progress and collection are derived by the server");
        var firstClaim = await Post(http, "AchievementProgress/ClaimReward", new AchievementClaimRequest
            { RequestId = Guid.NewGuid(), ExpectedRevision = achievement.Revision, AchievementItemId = "enemy_kills_10" });
        var claimReceipt = firstClaim.Data!["snapshot"]!.Deserialize<AchievementClaimSnapshot>(Json)!;
        Require(claimReceipt.Progress.Groups.Single(g => g.Name == "enemy_kills").Items.Single(i => i.Name == "enemy_kills_10").IsReward &&
            claimReceipt.Items.Single(i => i.ItemKey == "diamond").Amount == diamond + 1,
            "server-derived achievement claim returns progress and in-stock diamond in one receipt");
        // 第二次領取不含首次 JIT；兩次都印出 HTTP 毫秒與伺服器端計時事件（SQL 次數／耗時）。
        var secondClaim = await Post(http, "AchievementProgress/ClaimReward", new AchievementClaimRequest
            { RequestId = Guid.NewGuid(), ExpectedRevision = claimReceipt.Progress.Revision, AchievementItemId = "weapons_collected_1" });
        var secondReceipt = secondClaim.Data!["snapshot"]!.Deserialize<AchievementClaimSnapshot>(Json)!;
        Require(secondReceipt.Items.Single(i => i.ItemKey == "diamond").Amount == diamond + 2, "second claim adds one more diamond");
        foreach (var (label, claimResult) in new[] { ("first", firstClaim), ("second", secondClaim) })
        {
            await Task.Delay(500);
            Console.WriteLine($"ClaimTiming {label}: http={claimResult.Sample.Ms:F0}ms " +
                $"{(Breakdown.TryGetValue(claimResult.Sample.RequestId, out var timing) ? timing.ToJsonString() : "no server timing")}");
        }
        Require(await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@Player AND RequestId IN @Requests",
            new { Player = player, Requests = receipts }) == receipts.Count, "one receipt per fresh diagnostic intent");
        await PostBackground(http, new ItemDeltaRequest
            { ItemKey = "tuning_crystal", Delta = -materials, Category = "crystal" });
        var insufficient = new WeaponRefineRequest
            { RequestId = Guid.NewGuid(), WeaponEntryId = RefineWeapon().Id, CrystalId = "tuning_crystal" };
        await Rejected(http, RefineRoutes[0], insufficient, messageKey: ApiMessageKeys.ItemInsufficient);
        var unchanged = (await Post(http, "Weapon/GetInstanceList", new { })).Data!
            .Deserialize<List<WeaponInstanceModel>>(Json)!.Single(weapon => weapon.Id == RefineWeapon().Id);
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(expectedWeapon), JsonSerializer.SerializeToNode(unchanged));
        Require(await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@Player AND RequestId=@RequestId",
            new { Player = player, insufficient.RequestId }) == 0, "failed short write preserves weapon/revision and no receipt");
        await PostBackground(http, new ItemDeltaRequest
            { ItemKey = "tuning_crystal", Delta = 1, Category = "crystal" });
        var recovered = await Post(http, RefineRoutes[0], insufficient);
        var recoveredReceipt = recovered.Data!.Deserialize<CommandReceipt<WeaponRefineSnapshot>>(Json)!;
        Require(recoveredReceipt.Snapshot.MaterialCount == 0 && recoveredReceipt.Revision == unchanged.Revision + 1,
            "same failed ID succeeds once after restoring its material");
        AssertSnapshotEqual(recovered.Data, (await Post(http, RefineRoutes[0], insufficient)).Data);
        diagnostics.ReadbacksVerified = true;
    }

    private static async Task ObserveOwnedWaits(DapperRepository repository, string applicationName,
        int iteration, Task pending, RefineDiagnostics diagnostics)
    {
        if (diagnostics.WaitSamplingStatus == "permission-denied") return;
        try
        {
            for (int poll = 0; poll < 30 && !pending.IsCompleted; poll++)
            {
                var rows = await repository.QueryAsync<WaitRow>("""
                    SELECT r.session_id AS SessionId, r.blocking_session_id AS BlockingSessionId,
                        COALESCE(r.wait_type, '') AS WaitType, r.wait_time AS WaitTimeMs
                    FROM sys.dm_exec_requests r
                    JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id
                    LEFT JOIN sys.dm_exec_sessions b ON b.session_id=r.blocking_session_id
                    WHERE r.database_id=DB_ID() AND s.program_name=@ApplicationName
                        AND r.wait_type IS NOT NULL
                        AND (r.blocking_session_id <= 0 OR b.program_name=@ApplicationName);
                    """, new { ApplicationName = applicationName });
                diagnostics.WaitPolls++;
                diagnostics.WaitSamplingStatus = "available";
                diagnostics.Waits.AddRange(rows.Select(row =>
                    new OwnedWait(iteration, row.SessionId, row.BlockingSessionId, row.WaitType, row.WaitTimeMs)));
                if (!pending.IsCompleted) await Task.Delay(100);
            }
        }
        catch (SqlException error) when (error.Number is 229 or 297 or 300)
        {
            diagnostics.WaitSamplingStatus = "permission-denied";
            diagnostics.WaitPermissionError = error.Number;
            Console.Error.WriteLine($"Owned-session wait sampling unavailable: SQL permission error {error.Number}.");
        }
    }
}
