using System.Text.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

internal static partial class Fixture
{
    private static async Task SnapshotCommandRegressions(HttpClient http, DapperRepository repository,
        Guid player, List<string> checks)
    {
        async Task Merge<T>(string getRoute, Action<T> change) where T : IRevisionedSnapshot
        {
            var before = (await Post(http, getRoute, new { })).Data!.Deserialize<T>(Json)!;
            var fresh = Clone(before);
            fresh.ExpectedRevision = before.Revision;
            change(fresh);
            await SaveBackground(http, fresh);
            Require(fresh.Revision == before.Revision + 1, "background snapshot advances exactly once");
            long balance = await Gold(http);
            before.ExpectedRevision = before.Revision;
            if (before is GameProgressModel stale) stale.RequestId = Guid.NewGuid();
            await SaveBackground(http, before);
            T merged = (await Post(http, getRoute, new { })).Data!.Deserialize<T>(Json)!;
            Require(merged.Revision == fresh.Revision + 1, "stale background input merges instead of whole-domain CAS rejection");
            switch ((merged, fresh))
            {
                case (GameProgressModel p, GameProgressModel sent):
                    Require(p.LifetimeEnemiesDefeated == sent.LifetimeEnemiesDefeated,
                        "stale background progress cannot decrease counters");
                    break;
                case (TalentModel t, TalentModel sent):
                    Require(t.TalentPoints == sent.TalentPoints,
                        "stale background talent cannot revoke earned points");
                    break;
                case (AchievementProgressModel a, AchievementProgressModel sent):
                    Require(a.Groups[0].Total == sent.Groups[0].Total,
                        "stale background achievement cannot revoke evidence");
                    break;
            }
            Require(await Gold(http) == balance, "background merge leaves gold unchanged");
        }
        await Merge<GameProgressModel>("GameProgress/Get",
            p => { p.RequestId = Guid.NewGuid(); p.LifetimeEnemiesDefeated++; });
        await Merge<AchievementProgressModel>("AchievementProgress/Get", a => a.Groups[0].Total++);
        await Merge<TalentModel>("Talent/GetSnapshot", t => t.TalentPoints++);
        checks.Add("PASS: all three background snapshots merge stale counters without revoking newer state.");

        var progress = (await Post(http, "GameProgress/Get", new { })).Data!.Deserialize<GameProgressModel>(Json)!;
        progress.ExpectedRevision = progress.Revision;
        progress.RequestId = Guid.NewGuid();
        var wire = Clone(progress);
        await SaveBackground(http, progress);
        var afterSave = (await Post(http, "GameProgress/Get", new { })).Data!;
        long gold = await Gold(http);
        var replay = await PostBackground(http, wire);
        // Save's Sample carries timing only; the authoritative header on replay must equal the advanced revision.
        Require(replay.Revision == progress.Revision, "progress replay retains original revision receipt");
        AssertSnapshotEqual(afterSave, (await Post(http, "GameProgress/Get", new { })).Data);
        Require(await Gold(http) == gold, "progress replay never debits twice");
        await AssertReceiptCount(repository, player, wire.RequestId!.Value, 1);
        wire.Wave++;
        await Rejected(http, SaveRoutes[0], wire, messageKey: ApiMessageKeys.EconomyRequestConflict);
        AssertSnapshotEqual(afterSave, (await Post(http, "GameProgress/Get", new { })).Data);
        Require(await Gold(http) == gold, "conflicting progress payload no debit");
        checks.Add("PASS: progress request replay retains revision/snapshot/gold; changed payload with the same ID is rejected.");

        progress = afterSave.Deserialize<GameProgressModel>(Json)!;
        progress.ExpectedRevision = progress.Revision;
        progress.RequestId = Guid.NewGuid();
        progress.DeploymentLog =
        [
            new GameProgressDeploymentModel { SpotIndex = 0, CharacterId = Guid.NewGuid(), Wave = 1, Cell = [2, 0] },
        ];
        await SaveBackground(http, progress);
        var protectedProgress = (await Post(http, "GameProgress/Get", new { })).Data!.Deserialize<GameProgressModel>(Json)!;
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(afterSave.Deserialize<GameProgressModel>(Json)!.DeploymentLog),
            JsonSerializer.SerializeToNode(protectedProgress.DeploymentLog));
        Require(await Gold(http) == gold, "background deployment input cannot debit gold");
        checks.Add("PASS: background ignores client deployment replacements; deployment remains a foreground command.");
    }

    private static async Task MeasureSaves(HttpClient http, Func<string, Func<int, Task<Sample>>, Task> measure)
    {
        // Re-read after regressions: never reuse stale pre-regression revisions.
        var progress = (await Post(http, "GameProgress/Get", new { })).Data!.Deserialize<GameProgressModel>(Json)!;
        var achievements = (await Post(http, "AchievementProgress/Get", new { })).Data!.Deserialize<AchievementProgressModel>(Json)!;
        var talent = (await Post(http, "Talent/GetSnapshot", new { })).Data!.Deserialize<TalentModel>(Json)!;
        long gold = await Gold(http);
        await measure(SaveRoutes[0], async _ =>
        {
            progress.RequestId = Guid.NewGuid();
            progress.LifetimeEnemiesDefeated++;
            return await SaveBackground(http, progress);
        });
        var actualProgress = (await Post(http, "GameProgress/Get", new { })).Data!.Deserialize<GameProgressModel>(Json)!;
        AssertProgressRoundtrip(Clone(progress), actualProgress);
        Require(actualProgress.Revision == progress.Revision && await Gold(http) == gold, "measured progress revision and gold");
        await measure(SaveRoutes[1], async _ =>
        {
            achievements.Groups[0].Total++;
            return await SaveBackground(http, achievements);
        });
        var actualAchievements = (await Post(http, "AchievementProgress/Get", new { })).Data!.Deserialize<AchievementProgressModel>(Json)!;
        AssertAchievementOrder(achievements, actualAchievements);
        Require(actualAchievements.Revision == achievements.Revision, "measured achievement revision");
        foreach (var (expected, actual) in achievements.Groups.SelectMany(g => g.Items)
            .Zip(actualAchievements.Groups.SelectMany(g => g.Items)))
            Require(expected.IsReward == actual.IsReward && expected.RewardDate == actual.RewardDate,
                "measured saves retain claims and dates");
        await measure(SaveRoutes[2], async _ =>
        {
            talent.TalentPoints++;
            return await SaveBackground(http, talent);
        });
        var actualTalent = (await Post(http, "Talent/GetSnapshot", new { })).Data!.Deserialize<TalentModel>(Json)!;
        talent.ExpectedRevision = actualTalent.ExpectedRevision;
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(talent), JsonSerializer.SerializeToNode(actualTalent));
    }
}
