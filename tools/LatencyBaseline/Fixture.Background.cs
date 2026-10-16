using System.Text.Json.Nodes;
using System.Text.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

internal static partial class Fixture
{
    public static bool BackgroundBudgetMet { get; private set; }
    private static async Task MeasureBackground(HttpClient http, DapperRepository repository, Guid player,
        Func<string, Func<int, Task<Sample>>, Task> measure)
    {
        await Post(http, "GameProgress/Initialize", new { });
        long gold = await Gold(http);
        int initialWeapons = (await Post(http, "Weapon/GetInstanceList", new { })).Data!.AsArray().Count;
        long progressBase = (await Post(http, "GameProgress/Get", new { })).Data!.Deserialize<GameProgressModel>(Json)!.Revision;
        long talentBase = (await Post(http, "Talent/GetSnapshot", new { })).Data!.Deserialize<TalentModel>(Json)!.Revision;
        BackgroundSyncRequest? lastBatch = null;
        JsonNode? lastResult = null;
        await measure("GameProgress/BackgroundSync (all)", async i =>
        {
            Guid wave = Guid.NewGuid();
            var batch = new BackgroundSyncRequest
            {
                RequestId = Guid.NewGuid(),
                WaveStarts = [new() { RequestId = wave, Wave = 1, Difficulty = 0 }],
                Kills = [new() { RequestId = Guid.NewGuid(), SessionId = wave, EnemyIndex = 0 }],
                Talent = new() { Version = 3, TalentPoints = 5 },
                Items = [new() { ItemKey = "background-fixture", Delta = 3, Category = "material" }],
                WeaponAcquisitions = [new() { RequestId = Guid.NewGuid(), Weapon = new()
                    { Id = $"background-weapon-{i}", WeaponId = "sword", Rarity = "common" } }],
            };
            var result = await Post(http, "GameProgress/BackgroundSync", batch);
            var data = result.Data!.Deserialize<BackgroundSyncResponse>(Json)!;
            lastBatch = batch;
            lastResult = result.Data!.DeepClone();
            gold += 9;
            Require(data.RequestId == batch.RequestId && data.Gold == gold, "background identity and earned gold");
            Require(data.Sessions.Single().SessionId == wave && data.Items!.Single().Amount == (i + 1) * 3,
                "background session and item delta");
            Require(data.Weapons == null && data.Acquisitions.Single().Snapshot.Id == $"background-weapon-{i}",
                "background acquisition receipt replaces the full weapon list");
            Require(data.Progress!.Revision == progressBase + i + 1 && data.Talent!.Revision == talentBase + i + 1,
                "background two revision advances");
            return result.Sample;
        });
        Require(await Gold(http) == gold, "background final gold readback");
        Require((await Post(http, "Weapon/GetInstanceList", new { })).Data!.AsArray().Count == initialWeapons + 101,
            "background persisted every acquired weapon");
        Require(await repository.QueryFirstAsync<int>(
            "SELECT Amount FROM Item WHERE CreateUserId=@Player AND ItemKey='background-fixture' AND Status != -1",
            new { Player = player }) == 303, "background persisted item amount");
        Require(await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@Player AND Operation='background.sync'",
            new { Player = player }) == 0, "sync receipts no longer add one EconomyRequest row each");
        Require(await repository.QueryFirstAsync<int>("""
            SELECT (SELECT COUNT(*) FROM OPENJSON(Value)) FROM PlayerParams
            WHERE CreateUserId=@Player AND [Group]='economy' AND Name='sync_receipts' AND Status != -1
            """, new { Player = player }) == 2, "one bounded sync receipt row keeping the latest two batches");
        Require(await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@Player AND Operation='enemy_kill'",
            new { Player = player }) == 0, "kills no longer persist one receipt row each");
        TrackRegressionRequests = true;
        try { await BackgroundRegressions(http, repository, player, lastBatch!, lastResult!); }
        finally { TrackRegressionRequests = false; }
    }

    private static async Task BackgroundRegressions(HttpClient http, DapperRepository repository, Guid player,
        BackgroundSyncRequest lastBatch, JsonNode lastResult)
    {
        const string route = "GameProgress/BackgroundSync";
        AssertSnapshotEqual(lastResult, (await Post(http, route, lastBatch)).Data, "sync replay");
        var conflict = Clone(lastBatch);
        conflict.Items[0].Delta++;
        await Rejected(http, route, conflict, System.Net.HttpStatusCode.Conflict, ApiMessageKeys.EconomyRequestConflict);

        long gold = await Gold(http);
        // A new envelope must still replay its children, without another kill or acquisition.
        var children = new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            WaveStarts = lastBatch.WaveStarts, Kills = lastBatch.Kills,
            WeaponAcquisitions = lastBatch.WeaponAcquisitions };
        var replay = (await Post(http, route, children)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        Require(replay.Gold == gold && replay.Acquisitions.Single().Revision ==
            lastResult["acquisitions"]![0]!["revision"]!.GetValue<long>(), "child receipts replay across envelopes");
        // Kills are deduplicated by the active wave's bitmap, so a new request id for the same enemy awards nothing.
        var duplicateKill = new BackgroundSyncRequest { RequestId = Guid.NewGuid(), Kills =
            [new() { RequestId = Guid.NewGuid(), SessionId = lastBatch.Kills[0].SessionId, EnemyIndex = lastBatch.Kills[0].EnemyIndex }] };
        var duplicate = (await Post(http, route, duplicateKill)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        Require(duplicate.Gold == gold && duplicate.Progress == null, "duplicate kill with a new request id awards nothing");

        string[] reads = ["GameProgress/Get", "Talent/GetSnapshot", "AchievementProgress/Get",
            "Item/GetInStockList", "Weapon/GetInstanceList"];
        async Task<List<JsonNode?>> State()
        {
            var state = new List<JsonNode?>();
            foreach (string read in reads) { state.Add((await Post(http, read, new { })).Data); }
            return state;
        }
        var before = await State();
        int receiptCount = await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@Player", new { Player = player });
        var rejected = Clone(lastBatch);
        Guid wave = Guid.NewGuid();
        rejected.RequestId = Guid.NewGuid();
        rejected.WaveStarts[0].RequestId = wave;
        rejected.Kills[0].RequestId = Guid.NewGuid();
        rejected.Kills[0].SessionId = wave;
        rejected.WeaponAcquisitions[0].RequestId = Guid.NewGuid();
        rejected.WeaponAcquisitions[0].Weapon.Id = "rollback-weapon";
        rejected.Items.Add(new() { ItemKey = "absent-background-item", Delta = -1 });
        await Rejected(http, route, rejected, messageKey: ApiMessageKeys.ItemInsufficient);
        var after = await State();
        for (int i = 0; i < before.Count; i++) { AssertSnapshotEqual(before[i], after[i], "rollback " + reads[i]); }
        Require(await Gold(http) == gold && await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@Player", new { Player = player }) == receiptCount,
            "mid-write rejection rolls back every domain, gold, revision and receipt");
        rejected.Items.RemoveAt(1);
        var retried = (await Post(http, route, rejected)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        Require(retried.Gold == gold + 9 && retried.Items!.Single(i => i.ItemKey == "background-fixture").Amount == 306,
            "same rejected request succeeds after fixing invalid item");
        var progressRead = (await Post(http, "GameProgress/Get", new { })).Data;
        var progressSent = JsonSerializer.SerializeToNode(retried.Progress);
        // BackgroundSync builds progress without battlefield roads (BuildSnapshot is given empty road/deployment
        // lists), so only the fields it actually computes are comparable with GameProgress/Get.
        progressSent!.AsObject().Remove("battlefield");
        progressRead!.AsObject().Remove("battlefield");
        AssertSnapshotEqual(progressSent, progressRead, "retried progress");
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(retried.Talent),
            (await Post(http, "Talent/GetSnapshot", new { })).Data, "retried talent");
        var persistedWeapons = (await Post(http, "Weapon/GetInstanceList", new { })).Data!
            .Deserialize<List<WeaponInstanceModel>>(Json)!;
        var acquired = retried.Acquisitions.Single().Snapshot;
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(acquired),
            JsonSerializer.SerializeToNode(persistedWeapons.Single(w => w.Id == acquired.Id)), "acquired weapon");

        var rich = new BackgroundSyncRequest
        {
            RequestId = Guid.NewGuid(),
            WeaponAcquisitions = [new() { RequestId = Guid.NewGuid(), Weapon = new()
            {
                Id = "rich-background-weapon", WeaponId = "staff", Rarity = "rare",
                Affixes = [JsonSerializer.SerializeToElement(new { id = "damage", values = new[] { 3, 4 } })],
                Unique = new() { ["unique_id"] = JsonSerializer.SerializeToElement("unique"),
                    ["values"] = JsonSerializer.SerializeToElement(new[] { 5 }) },
            } }],
        };
        var richResult = (await Post(http, route, rich)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        var richReadback = (await Post(http, "Weapon/GetInstanceList", new { })).Data!.Deserialize<List<WeaponInstanceModel>>(Json)!;
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(richResult.Acquisitions.Single().Snapshot),
            JsonSerializer.SerializeToNode(richReadback.Single(w => w.Id == "rich-background-weapon")), "rich weapon");
        Require(richReadback.Single(w => w.Id == "rich-background-weapon").Affixes[0]
            .GetProperty("values").GetArrayLength() == 2, "rich weapon children persisted");

        var first = new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            Items = [new() { ItemKey = "background-fixture", Delta = 1 }] };
        var second = Clone(first);
        second.RequestId = Guid.NewGuid();
        await Task.WhenAll(Post(http, route, first), Post(http, route, second));
        Require(await repository.QueryFirstAsync<int>(
            "SELECT Amount FROM Item WHERE CreateUserId=@Player AND ItemKey='background-fixture' AND Status != -1",
            new { Player = player }) == 308, "same-player concurrent batches retain both deltas");
        await Task.WhenAll(Post(http, route, new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            Talent = new() { Version = 3, TalentPoints = 6 } }),
            Post(http, "Weapon/Disassemble", new WeaponDisassembleRequest { RequestId = Guid.NewGuid(),
                WeaponEntryIds = ["rich-background-weapon"] }));
        Require(!(await Post(http, "Weapon/GetInstanceList", new { })).Data!.Deserialize<List<WeaponInstanceModel>>(Json)!
            .Any(w => w.Id == "rich-background-weapon"), "foreground disassembly survives concurrent background sync");
        var tombstone = Clone(rich);
        tombstone.RequestId = Guid.NewGuid();
        tombstone.WeaponAcquisitions[0].RequestId = Guid.NewGuid();
        tombstone.Items = [new() { ItemKey = "background-fixture", Delta = 4 }];
        await Rejected(http, route, tombstone, System.Net.HttpStatusCode.Conflict, ApiMessageKeys.EconomyRequestConflict);
        Require(await repository.QueryFirstAsync<int>(
            "SELECT Amount FROM Item WHERE CreateUserId=@Player AND ItemKey='background-fixture' AND Status != -1",
            new { Player = player }) == 308, "late tombstone conflict rolls back earlier item update");

        await ClaimAchievement(http, "enemy_kills_10");
        var claimed = (await Post(http, "AchievementProgress/Get", new { })).Data!.Deserialize<AchievementProgressModel>(Json)!;
        var claim = claimed.Groups.SelectMany(g => g.Items).Single(i => i.Name == "enemy_kills_10");
        Require(claim.IsReward && claim.RewardDate != null, "foreground achievement claim established");
        var retained = (await Post(http, "AchievementProgress/Get", new { })).Data!.Deserialize<AchievementProgressModel>(Json)!
            .Groups.SelectMany(g => g.Items).Single(i => i.Name == "enemy_kills_10");
        Require(retained.IsReward && retained.RewardDate == claim.RewardDate, "background preserves claimed reward and date");
        var empty = (await Post(http, route, new BackgroundSyncRequest { RequestId = Guid.NewGuid() })).Data!
            .Deserialize<BackgroundSyncResponse>(Json)!;
        Require(empty.Progress == null && empty.Talent == null &&
            empty.Items == null && empty.Weapons == null && empty.Gold == await Gold(http),
            "empty batch preserves absent sections");
        Stage = "background 17-material set-based items";
        var materials = new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            Items = Enumerable.Range(0, 17).Select(i => new ItemDeltaRequest
                { ItemKey = $"material-background-{i}", Delta = 100, Category = "refine" }).ToList() };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Post(http, route, materials);
        Console.WriteLine($"17-item sync: {watch.ElapsedMilliseconds} ms");
        materials.RequestId = Guid.NewGuid();
        var materialsResult = (await Post(http, route, materials)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        var warm = new List<long>();
        string lastTiming = "";
        for (int i = 0; i < 6; i++)
        {
            materials.RequestId = Guid.NewGuid();
            watch.Restart();
            lastTiming = (await Post(http, route, materials)).Sample.RequestId;
            warm.Add(watch.ElapsedMilliseconds);
        }
        await Task.Delay(500);
        Console.WriteLine($"17-item timing: {(Breakdown.TryGetValue(lastTiming, out var itemTiming) ? itemTiming.ToJsonString() : "none")}");
        Console.WriteLine($"17-item warm updates: {string.Join(", ", warm)} ms");
        materialsResult = (await Post(http, route, new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            Items = materials.Items.Select(i => new ItemDeltaRequest { ItemKey = i.ItemKey, Delta = -600, Category = "refine" }).ToList() }))
            .Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        materials.RequestId = Guid.NewGuid();
        materialsResult = (await Post(http, route, materials)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        Require(materialsResult.Items!.Count(i => i.ItemKey.StartsWith("material-background-", StringComparison.Ordinal)
                && i.Amount == 300 && i.Category == "refine") == 17, "17 materials adjusted up, down and up");
        // Receipts keep no inventory snapshot: a replay returns the current stock, with no repeated delta.
        await Post(http, route, new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            Items = [new() { ItemKey = "material-background-0", Delta = 5, Category = "refine" }] });
        var stockReplay = (await Post(http, route, materials)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        Require(stockReplay.Items!.Single(i => i.ItemKey == "material-background-0").Amount == 305 &&
            stockReplay.Items!.Single(i => i.ItemKey == "material-background-1").Amount == 300,
            "replay returns current stock without reapplying deltas");
        var large = new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            Items = Enumerable.Range(0, 230).Select(i => new ItemDeltaRequest
                { ItemKey = $"large-background-{i}", Delta = 1, Category = "material" }).ToList() };
        large.Items.AddRange([
            new() { ItemKey = "ordered-background", Delta = 1, Category = "first" },
            new() { ItemKey = "ordered-background", Delta = -1 },
            new() { ItemKey = "ordered-background", Delta = 2, Category = "second" },
        ]);
        Stage = "background large batch regression";
        var largeResult = (await Post(http, route, large)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        var largeItems = largeResult.Items ?? throw new FixtureAssertion("large batch items present");
        Require(largeItems.Count(i => i.ItemKey.StartsWith("large-background-", StringComparison.Ordinal)) == 230 &&
            largeItems.Single(i => i.ItemKey == "ordered-background").Amount == 2 &&
            largeItems.Single(i => i.ItemKey == "ordered-background").Category == "second",
            "split SQL batches keep one transaction and preserve ordered item tombstone/recreate behavior");
        // WaveComplete counts distinct enemies from the claimed-kill bitmap.
        Guid completeWave = Guid.NewGuid();
        Stage = "background wave complete from kill bitmap";
        var partial = new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            WaveStarts = [new() { RequestId = completeWave, Wave = 1, Difficulty = 0 }],
            Kills = Enumerable.Range(0, 8).Select(i => new EnemyKillRequest
                { RequestId = Guid.NewGuid(), SessionId = completeWave, EnemyIndex = i }).ToList() };
        var partialResult = (await Post(http, route, partial)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        int enemyCount = partialResult.Sessions.Single().EnemyCount;
        await Rejected(http, "GameProgress/WaveComplete", new WaveCompleteRequest
            { RequestId = Guid.NewGuid(), SessionId = completeWave }, messageKey: ApiMessageKeys.EconomyEnemyInvalid);
        var rest = new BackgroundSyncRequest { RequestId = Guid.NewGuid(), Kills = Enumerable.Range(0, enemyCount).Select(i =>
            new EnemyKillRequest { RequestId = Guid.NewGuid(), SessionId = completeWave, EnemyIndex = i }).ToList() };
        long beforeRest = await Gold(http);
        var restResult = (await Post(http, route, rest)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        Require(restResult.Gold == beforeRest + 9, "only the unclaimed enemy is rewarded after a partial wave");
        var complete = new WaveCompleteRequest { RequestId = Guid.NewGuid(), SessionId = completeWave };
        var completed = (await Post(http, "GameProgress/WaveComplete", complete)).Data;
        AssertSnapshotEqual(completed, (await Post(http, "GameProgress/WaveComplete", complete)).Data, "wave complete replay");
        Console.WriteLine("PASS: full replay, child replay, conflicts, SQL rollback/retry, readbacks, rich weapons, concurrency, tombstones and claimed rewards.");
    }

    private static BackgroundSyncRequest BackgroundBatch(object work) => work switch
    {
        BackgroundSyncRequest batch => batch,
        WaveStartRequest wave => new() { RequestId = BackgroundRequestId(wave.RequestId), WaveStarts = [wave] },
        EnemyKillRequest kill => new() { RequestId = BackgroundRequestId(kill.RequestId), Kills = [kill] },
        WeaponAcquireRequest weapon => new() { RequestId = BackgroundRequestId(weapon.RequestId), WeaponAcquisitions = [weapon] },
        ItemDeltaRequest item => new() { RequestId = Guid.NewGuid(), Items = [item] },
        GameProgressModel progress => new() { RequestId = progress.RequestId ?? Guid.NewGuid(), Progress = progress },
        TalentModel talent => new() { RequestId = Guid.NewGuid(), Talent = talent },
        _ => throw new ArgumentException("Unsupported background work.", nameof(work)),
    };

    private static Guid BackgroundRequestId(Guid child) =>
        new(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{child:D}/background")).AsSpan(0, 16));

    private static async Task<Result> PostBackground(HttpClient http, object work)
    {
        Result result = await Post(http, "GameProgress/BackgroundSync", BackgroundBatch(work));
        JsonNode? projected = work switch
        {
            WaveStartRequest => result.Data!["sessions"]!.AsArray().Single(),
            WeaponAcquireRequest => result.Data!["acquisitions"]!.AsArray().Single(),
            ItemDeltaRequest item => JsonValue.Create(result.Data!["items"]!.AsArray()
                .SingleOrDefault(row => row!["ItemKey"]!.GetValue<string>() == item.ItemKey)?["Amount"]!.GetValue<int>() ?? 0),
            GameProgressModel => result.Data!["progress"],
            TalentModel => result.Data!["talent"],
            _ => result.Data,
        };
        long? revision = work is IRevisionedSnapshot ? projected!["revision"]!.GetValue<long>() : null;
        return result with { Data = projected?.DeepClone(), Revision = revision };
    }
}
