using System.Data.SqlClient;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Utility;

internal static partial class Fixture
{
    // Shared by --regression-only and --saves-only; always uses Run's NEW disposable fixture.
    // No triggers, schema changes or existing-player data are needed.
    private static async Task Regressions(HttpClient http, DapperRepository repository, Guid player,
        GameProgressModel progress, AchievementProgressModel achievements, List<string> checks)
    {
        const string claim = "enemy_kills_10";
        await Post(http, "AchievementProgress/ClaimReward",
            new AchievementClaimRequest { AchievementItemId = claim });
        achievements = (await Post(http, "AchievementProgress/Get", new { })).Data!
            .Deserialize<AchievementProgressModel>(Json)!;
        var claimed = achievements.Groups.SelectMany(g => g.Items).Single(i => i.Name == claim);
        Require(claimed.IsReward && claimed.RewardDate.HasValue, "claim persisted by server");
        DateTime claimDate = claimed.RewardDate!.Value;
        for (int pass = 0; pass < 3; pass++)
        {
            achievements.Groups.Reverse();
            foreach (var group in achievements.Groups)
            {
                group.Items.Reverse();
                group.Collected = pass % 2 == 0 ? ["staff", "sword", "bow"] : ["bow", "staff", "sword"];
                foreach (var item in group.Items)
                {
                    // Client cannot revoke the real claim or forge another claim/date.
                    item.IsReward = item.Name != claim;
                    item.RewardDate = new DateTime(2001, 1, 1);
                }
            }
            await SaveBackground(http, achievements);
            var actual = (await Post(http, "AchievementProgress/Get", new { })).Data!
                .Deserialize<AchievementProgressModel>(Json)!;
            AssertAchievementOrder(achievements, actual);
            foreach (var item in actual.Groups.SelectMany(g => g.Items))
            {
                Require(item.IsReward == (item.Name == claim), "server claim flags survive saves");
                Require(item.RewardDate == (item.Name == claim ? claimDate : null),
                    "server claim dates survive saves");
            }
            await AssertSequences(repository, player, actual);
            achievements = actual;
        }
        await Rejected(http, "AchievementProgress/ClaimReward",
            new AchievementClaimRequest { AchievementItemId = claim });
        checks.Add("PASS: three saves preserve real claim/date, ignore forged claims, reject repeat claim, " +
            "and preserve group/item/collected order with zero-based per-parent SQL Sequence.");

        await Post(http, "Economy/PurchaseSkill", new SkillPurchaseRequest
            { RequestId = Guid.NewGuid(), Type = "active", Kind = 1 });
        await Post(http, "Economy/PurchaseSkill", new SkillPurchaseRequest
            { RequestId = Guid.NewGuid(), Type = "auxiliary", LootId = "explosion_gem" });
        progress = (await Post(http, "GameProgress/Get", new { })).Data!.Deserialize<GameProgressModel>(Json)!;
        long gold = (await Post(http, "Shop/GetGold", new { })).Data!.GetValue<long>();
        var protectedBullets = Clone(progress.UnlockedBulletKinds);
        var protectedAuxiliary = Clone(progress.UnlockedAuxiliaryKinds);
        Require(protectedBullets["1"].GetBoolean() && protectedAuxiliary["explosion_gem"].GetBoolean(),
            "authoritative purchased unlocks exist");
        for (int pass = 0; pass < 3; pass++)
        {
            progress.Gold = 9_999_999; // Must not overwrite the authoritative economy balance.
            progress.RequestId = Guid.NewGuid();
            progress.UnlockedBulletKinds = new()
            {
                ["1"] = JsonSerializer.SerializeToElement(false),
                ["6"] = JsonSerializer.SerializeToElement(true),
            };
            progress.UnlockedAuxiliaryKinds = new()
            {
                ["explosion_gem"] = JsonSerializer.SerializeToElement(false),
                ["tracking_gem"] = JsonSerializer.SerializeToElement(true),
            };
            progress.Wave = 2 + pass;
            progress.MaxWaveReached = 7 + pass;
            progress.LifetimeEnemiesDefeated = 70 + pass;
            progress.ClearedWaves = [JsonSerializer.SerializeToElement(3), JsonSerializer.SerializeToElement(1)];
            progress.Progression = new()
            {
                ["normal"] = JsonSerializer.SerializeToElement(new { completed = pass, flags = new[] { 3, 1 } }),
                ["intro"] = JsonSerializer.SerializeToElement(pass),
            };
            var authoritativeBattlefield = Clone(progress.Battlefield);
            progress.Battlefield.Roads.Reverse();
            var expected = Clone(progress);
            expected.Battlefield = authoritativeBattlefield;
            expected.Gold = gold;
            expected.UnlockedBulletKinds = Clone(protectedBullets);
            expected.UnlockedAuxiliaryKinds = Clone(protectedAuxiliary);
            await SaveBackground(http, progress);
            var actual = (await Post(http, "GameProgress/Get", new { })).Data!
                .Deserialize<GameProgressModel>(Json)!;
            AssertProgressRoundtrip(expected, actual);
            var roadSequences = (await repository.QueryAsync<int>(
                "SELECT Sequence FROM BattlefieldRoad WHERE CreateUserId=@PlayerId ORDER BY Sequence",
                new { PlayerId = player })).ToList();
            Require(roadSequences.SequenceEqual(Enumerable.Range(0, expected.Battlefield.Roads.Count)),
                "road sequences reset for each replacement");
            progress = actual;
        }
        checks.Add("PASS: three saves preserve authoritative gold/purchased unlocks, reject forged unlock " +
            "state, and exactly roundtrip scalars/settings/cleared-wave order/castle/road values and order.");

        // A real second INSERT failure with no schema changes: duplicate the new fixture-owned PK.
        // This tests repository transaction rollback, NOT injected failure inside a domain HTTP save.
        Guid rollbackId = Guid.NewGuid();
        bool duplicateFailed = false;
        using (var transaction = repository.OpenTransaction())
        {
            var row = new PlayerParams
            {
                Id = rollbackId, CreateUserId = player,
                Group = "latency.regression", Name = "rollback-probe", Value = "1",
            };
            await repository.InsertAsync(row);
            try { await repository.InsertAsync(row); }
            catch (SqlException error) when (error.Number is 2601 or 2627) { duplicateFailed = true; }
            Require(duplicateFailed, "second insert failed with duplicate key");
            // Deliberately no Commit: scope disposal must roll back the successful first INSERT.
        }
        Require(await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM PlayerParams WHERE Id=@Id AND CreateUserId=@PlayerId",
            new { Id = rollbackId, PlayerId = player }) == 0, "first insert rolled back atomically");
        checks.Add("PASS: real fixture-owned second INSERT PK failure rolls back first INSERT on transaction disposal.");

        // Where the actual schema has a bounded detail-name column, exercise a domain HTTP failure
        // after parent replacement starts. Do not create constraints/triggers or modify the schema.
        int? maxCharacters = await repository.QueryFirstOrDefaultAsync<int?>(
            """
            SELECT CASE WHEN c.max_length=-1 THEN NULL
                        WHEN ty.name IN ('nvarchar','nchar') THEN c.max_length/2
                        ELSE c.max_length END
            FROM sys.columns c JOIN sys.types ty ON c.user_type_id=ty.user_type_id
            WHERE c.object_id=OBJECT_ID('dbo.AchievementDetail') AND c.name='Name'
            """);
        if (maxCharacters is > 0 and < 100000)
        {
            var before = (await Post(http, "AchievementProgress/Get", new { })).Data!;
            var invalid = before.Deserialize<AchievementProgressModel>(Json)!;
            invalid.ExpectedRevision = invalid.Revision;
            invalid.Groups[0].Items[0].Name = new string('x', maxCharacters.Value + 1);
            await Rejected(http, "GameProgress/BackgroundSync (achievement)", invalid);
            var after = (await Post(http, "AchievementProgress/Get", new { })).Data!;
            Require(JsonNode.DeepEquals(before, after), "rejected detail insert leaves full snapshot/revision unchanged");
            checks.Add("PASS: oversized detail HTTP save rejected with snapshot/revision unchanged; " +
                "exact second-INSERT execution point not proven by aggregate instrumentation.");
        }
        else checks.Add("NOT EXERCISED: domain mid-write SQL failure injection requires a bounded column " +
            "or dedicated fault-injecting repository; no schema/trigger changes made.");
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private static void AssertAchievementOrder(AchievementProgressModel expected, AchievementProgressModel actual)
    {
        Require(expected.Groups.Count == actual.Groups.Count, "group count");
        foreach (var (left, right) in expected.Groups.Zip(actual.Groups))
        {
            Require(left.Name == right.Name && left.Total == right.Total, "group values and order");
            Require(left.Items.Select(i => (i.Name, i.Target)).SequenceEqual(
                right.Items.Select(i => (i.Name, i.Target))), "item values and order");
            Require(left.Collected.SequenceEqual(right.Collected), "collected values and order");
        }
    }

    private static void AssertProgressRoundtrip(GameProgressModel expected, GameProgressModel actual)
    {
        // Request/caller CAS metadata is not persisted snapshot content.
        expected.RequestId = actual.RequestId;
        expected.ExpectedRevision = actual.ExpectedRevision;
        expected.Revision = actual.Revision;
        Require(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected),
            JsonSerializer.SerializeToNode(actual)), "exact protected progress roundtrip");
    }

    private sealed class SequenceRow
    {
        public string ParentName { get; set; } = "";
        public string Name { get; set; } = "";
        public int Sequence { get; set; }
    }

    private static async Task AssertSequences(DapperRepository repository, Guid player, AchievementProgressModel model)
    {
        var groups = (await repository.QueryAsync<SequenceRow>(
            "SELECT Name,Sequence FROM Achievement WHERE CreateUserId=@PlayerId ORDER BY Sequence",
            new { PlayerId = player })).ToList();
        Require(groups.Select(g => g.Name).SequenceEqual(model.Groups.Select(g => g.Name)) &&
            groups.Select(g => g.Sequence).SequenceEqual(Enumerable.Range(0, groups.Count)),
            "group SQL sequence");
        foreach (string table in new[] { "AchievementDetail", "AchievementCollected" })
        {
            var rows = (await repository.QueryAsync<SequenceRow>($"""
                SELECT p.Name ParentName,c.Name,c.Sequence FROM [{table}] c
                JOIN Achievement p ON p.Id=c.AchievementId AND p.CreateUserId=@PlayerId
                WHERE c.CreateUserId=@PlayerId ORDER BY p.Sequence,c.Sequence
                """, new { PlayerId = player })).ToList();
            foreach (var group in model.Groups)
            {
                var children = rows.Where(r => r.ParentName == group.Name).ToList();
                var names = table == "AchievementDetail" ? group.Items.Select(i => i.Name) : group.Collected;
                Require(children.Select(c => c.Name).SequenceEqual(names) &&
                    children.Select(c => c.Sequence).SequenceEqual(Enumerable.Range(0, children.Count)),
                    "per-parent SQL child sequence");
            }
        }
    }

    private static async Task Rejected(HttpClient http, string route, object body,
        HttpStatusCode status = HttpStatusCode.BadRequest, string? messageKey = null)
    {
        if (body is GameProgressModel or TalentModel or AchievementProgressModel or BackgroundSyncRequest
            or WaveStartRequest or EnemyKillRequest or ItemDeltaRequest or WeaponAcquireRequest)
        {
            route = "GameProgress/BackgroundSync";
            body = BackgroundBatch(body);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
        request.Headers.Add("Encode", Encode.New());
        using var response = await http.SendAsync(request);
        Require(response.StatusCode == status, $"expected rejection {route}");
        var envelope = JsonNode.Parse(await response.Content.ReadAsByteArrayAsync())!;
        Require(envelope["Success"]!.GetValue<bool>() == false, $"business rejection {route}");
        if (messageKey != null)
            Require(envelope["MessageKey"]!.GetValue<string>() == messageKey, $"rejection key {route}");
        if (TrackRegressionRequests)
            Require(RegressionCorrelations.TryAdd(response.Headers.GetValues("X-Request-Timing-Id").Single(),
                (int)status), "unique rejected regression correlation");
    }

    public static void RegressionSelfTest()
    {
        var achievements = new AchievementProgressModel { Groups = AchievementGroups() };
        AssertAchievementOrder(achievements, Clone(achievements));
        var reordered = Clone(achievements);
        reordered.Groups.Reverse();
        bool detected = false;
        try { AssertAchievementOrder(achievements, reordered); }
        catch (FixtureAssertion) { detected = true; }
        Require(detected, "order comparator detects reordering");
        var progress = new GameProgressModel { Version = 1, Gold = 100 };
        AssertProgressRoundtrip(Clone(progress), Clone(progress));
        var wrong = Clone(progress);
        wrong.Gold++;
        detected = false;
        try { AssertProgressRoundtrip(Clone(progress), wrong); }
        catch (FixtureAssertion) { detected = true; }
        Require(detected, "roundtrip comparator detects protected value corruption");
        Console.WriteLine("Regression comparator self-test passed; no database or HTTP access.");
    }
}
