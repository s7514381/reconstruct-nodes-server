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
        long gold = await Gold(http);
        int initialWeapons = (await Post(http, "Weapon/GetInstanceList", new { })).Data!.AsArray().Count;
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
                Progress = new() { Version = 8, Wave = 1 },
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
            Require(data.Weapons!.Count == initialWeapons + i + 1 && data.Acquisitions.Count == 1,
                "background new weapon inventory");
            Require(data.Progress!.Revision == i + 1 && data.Talent!.Revision == i + 1, "background two revision advances");
            return result.Sample;
        });
        Require(await Gold(http) == gold, "background final gold readback");
        Require(await repository.QueryFirstAsync<int>(
            "SELECT Amount FROM Item WHERE CreateUserId=@Player AND ItemKey='background-fixture' AND Status != -1",
            new { Player = player }) == 303, "background persisted item amount");
        Require(await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@Player AND Operation='background.sync'",
            new { Player = player }) == 101, "101 distinct persisted background receipts");
        TrackRegressionRequests = true;
        try { await BackgroundRegressions(http, repository, player, lastBatch!, lastResult!); }
        finally { TrackRegressionRequests = false; }
    }

    private static async Task BackgroundRegressions(HttpClient http, DapperRepository repository, Guid player,
        BackgroundSyncRequest lastBatch, JsonNode lastResult)
    {
        const string route = "GameProgress/BackgroundSync";
        AssertSnapshotEqual(lastResult, (await Post(http, route, lastBatch)).Data);
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
        for (int i = 0; i < before.Count; i++) { AssertSnapshotEqual(before[i], after[i]); }
        Require(await Gold(http) == gold && await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@Player", new { Player = player }) == receiptCount,
            "mid-write rejection rolls back every domain, gold, revision and receipt");
        rejected.Items.RemoveAt(1);
        var retried = (await Post(http, route, rejected)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        Require(retried.Gold == gold + 9 && retried.Items!.Single(i => i.ItemKey == "background-fixture").Amount == 306,
            "same rejected request succeeds after fixing invalid item");
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(retried.Progress),
            (await Post(http, "GameProgress/Get", new { })).Data);
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(retried.Talent),
            (await Post(http, "Talent/GetSnapshot", new { })).Data);
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(retried.Weapons),
            (await Post(http, "Weapon/GetInstanceList", new { })).Data);

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
            JsonSerializer.SerializeToNode(richReadback.Single(w => w.Id == "rich-background-weapon")));
        Require(richReadback.Single(w => w.Id == "rich-background-weapon").Affixes[0]
            .GetProperty("values").GetArrayLength() == 2, "rich weapon children persisted");

        var first = new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            Items = [new() { ItemKey = "background-fixture", Delta = 1 }], Progress = new() { Version = 8 } };
        var second = Clone(first);
        second.RequestId = Guid.NewGuid();
        await Task.WhenAll(Post(http, route, first), Post(http, route, second));
        Require(await repository.QueryFirstAsync<int>(
            "SELECT Amount FROM Item WHERE CreateUserId=@Player AND ItemKey='background-fixture' AND Status != -1",
            new { Player = player }) == 308, "same-player concurrent batches retain both deltas");
        await Task.WhenAll(Post(http, route, new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            Progress = new() { Version = 8 }, Talent = new() { Version = 3, TalentPoints = 6 } }),
            Post(http, "Weapon/Disassemble", new WeaponDisassembleRequest { RequestId = Guid.NewGuid(),
                WeaponEntryIds = ["rich-background-weapon"] }));
        Require(!(await Post(http, "Weapon/GetInstanceList", new { })).Data!.Deserialize<List<WeaponInstanceModel>>(Json)!
            .Any(w => w.Id == "rich-background-weapon"), "foreground disassembly survives concurrent background sync");
        var tombstone = Clone(rich);
        tombstone.RequestId = Guid.NewGuid();
        tombstone.WeaponAcquisitions[0].RequestId = Guid.NewGuid();
        tombstone.Items = [new() { ItemKey = "background-fixture", Delta = 4 }];
        tombstone.Progress = new() { Version = 8 };
        await Rejected(http, route, tombstone, System.Net.HttpStatusCode.Conflict, ApiMessageKeys.EconomyRequestConflict);
        Require(await repository.QueryFirstAsync<int>(
            "SELECT Amount FROM Item WHERE CreateUserId=@Player AND ItemKey='background-fixture' AND Status != -1",
            new { Player = player }) == 308, "late tombstone conflict rolls back earlier item update");

        await Post(http, "AchievementProgress/ClaimReward", new AchievementClaimRequest { AchievementItemId = "kill-test" });
        var claimed = (await Post(http, "AchievementProgress/Get", new { })).Data!.Deserialize<AchievementProgressModel>(Json)!;
        var claim = claimed.Groups.Single(g => g.Name == "kills").Items.Single(i => i.Name == "kill-test");
        Require(claim.IsReward && claim.RewardDate != null, "foreground achievement claim established");
        var retained = (await Post(http, "AchievementProgress/Get", new { })).Data!.Deserialize<AchievementProgressModel>(Json)!
            .Groups.Single(g => g.Name == "kills").Items.Single(i => i.Name == "kill-test");
        Require(retained.IsReward && retained.RewardDate == claim.RewardDate, "background preserves claimed reward and date");
        var empty = (await Post(http, route, new BackgroundSyncRequest { RequestId = Guid.NewGuid() })).Data!
            .Deserialize<BackgroundSyncResponse>(Json)!;
        Require(empty.Progress == null && empty.Talent == null &&
            empty.Items == null && empty.Weapons == null && empty.Gold == await Gold(http),
            "empty batch preserves absent sections");
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
        Guid wave1 = Guid.NewGuid(), wave2 = Guid.NewGuid();
        long beforeWaves = await Gold(http);
        var waves = new BackgroundSyncRequest { RequestId = Guid.NewGuid(),
            WaveStarts = [new() { RequestId = wave1, Wave = 1 }, new() { RequestId = wave2, Wave = 2 }],
            Kills = [new() { RequestId = Guid.NewGuid(), SessionId = wave1, EnemyIndex = 0 },
                new() { RequestId = Guid.NewGuid(), SessionId = wave2, EnemyIndex = 0 }] };
        Stage = "background multiple waves regression";
        var wavesResult = (await Post(http, route, waves)).Data!.Deserialize<BackgroundSyncResponse>(Json)!;
        Require(wavesResult.Sessions.Count == 2 && wavesResult.Gold == beforeWaves + 19,
            "multiple new waves and kills keep session ordering and cumulative rewards");
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
