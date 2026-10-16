using System.Net;
using System.Text.Json.Nodes;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

internal static partial class Fixture
{
    private static async Task MeasureEconomy(HttpClient http, Func<string, Func<int, Task<Sample>>, Task> measure)
    {
        long gold = await Gold(http);
        await measure(EconomyRoutes[0], async _ =>
        {
            Guid id = Guid.NewGuid();
            var result = await PostBackground(http, new WaveStartRequest { RequestId = id, Wave = 100, Difficulty = 0 });
            Require(result.Data!["session_id"]!.GetValue<Guid>() == id &&
                result.Data["enemy_count"]!.GetValue<int>() == 307 &&
                result.Data["gold"]!.GetValue<long>() == gold, "measured fresh wave receipt");
            return result.Sample;
        });
        Guid session = Guid.NewGuid();
        await PostBackground(http, new WaveStartRequest { RequestId = session, Wave = 100, Difficulty = 0 });
        await measure(EconomyRoutes[1], async i =>
        {
            var result = await PostBackground(http, new EnemyKillRequest
                { RequestId = Guid.NewGuid(), SessionId = session, EnemyIndex = i });
            gold += 108; // Non-boss indices 0..100, no active gold talents.
            Require(result.Data!["gold"]!.GetValue<long>() == gold, "measured unique kill exact reward");
            return result.Sample;
        });
        Require(await Gold(http) == gold, "measured economy final balance");
    }

    private static async Task EconomyRegressions(HttpClient http, DapperRepository repository, Guid player,
        string clientId, List<string> checks)
    {
        long gold = await Gold(http);
        var wave = new WaveStartRequest { RequestId = Guid.NewGuid(), Wave = 10, Difficulty = 0 };
        var first = await PostBackground(http, wave);
        var replay = await PostBackground(http, wave);
        AssertSnapshotEqual(first.Data, replay.Data);
        Require(first.Data!["session_id"]!.GetValue<Guid>() == wave.RequestId &&
            first.Data["enemy_count"]!.GetValue<int>() == 37 && await Gold(http) == gold, "wave replay no gold change");
        await AssertReceiptCount(repository, player, wave.RequestId, 1);
        checks.Add("PASS: StartWave replay returns the original unique receipt without changing gold.");

        var kill = new EnemyKillRequest { RequestId = Guid.NewGuid(), SessionId = wave.RequestId, EnemyIndex = 0 };
        first = await PostBackground(http, kill);
        gold += 18;
        Require(first.Data!["gold"]!.GetValue<long>() == gold, "initial kill exact reward");
        replay = await PostBackground(http, kill);
        AssertSnapshotEqual(first.Data, replay.Data);
        Require(await Gold(http) == gold, "kill replay no additional gold");
        await AssertReceiptCount(repository, player, kill.RequestId, 1);
        checks.Add("PASS: ReportKill replay returns its unique receipt and never awards twice.");

        var duplicate = new EnemyKillRequest { RequestId = Guid.NewGuid(), SessionId = wave.RequestId, EnemyIndex = 0 };
        await Rejected(http, EconomyRoutes[1], duplicate, messageKey: ApiMessageKeys.EconomyRewardClaimed);
        await AssertReceiptCount(repository, player, duplicate.RequestId, 0);
        Require(await Gold(http) == gold, "duplicate enemy no reward");
        checks.Add("PASS: another request ID for the same enemy is rejected without gold/receipt changes.");

        await Rejected(http, EconomyRoutes[0],
            new WaveStartRequest { RequestId = wave.RequestId, Wave = 11, Difficulty = 0 },
            HttpStatusCode.Conflict, ApiMessageKeys.EconomyRequestConflict);
        await Rejected(http, EconomyRoutes[1],
            new EnemyKillRequest { RequestId = kill.RequestId, SessionId = wave.RequestId, EnemyIndex = 1 },
            HttpStatusCode.Conflict, ApiMessageKeys.EconomyRequestConflict);
        Require(await Gold(http) == gold, "payload conflict no gold change");
        await AssertReceiptCount(repository, player, wave.RequestId, 1);
        await AssertReceiptCount(repository, player, kill.RequestId, 1);
        checks.Add("PASS: same request ID / changed payload returns 409 for both commands.");

        await PostBackground(http, new WaveStartRequest { RequestId = Guid.NewGuid(), Wave = 11, Difficulty = 0 });
        int enemy = 1;
        async Task Reward(int expectedBonus, EnemyKillRequest? request = null)
        {
            request ??= new EnemyKillRequest
                { RequestId = Guid.NewGuid(), SessionId = wave.RequestId, EnemyIndex = enemy++ };
            var result = await PostBackground(http, request);
            gold += 18 + expectedBonus;
            Require(result.Data!["gold"]!.GetValue<long>() == gold && await Gold(http) == gold,
                "old session exact reward and talent bonus");
        }
        await Reward(0);
        checks.Add("PASS: a new wave does not invalidate legitimate unclaimed enemies in the old session.");

        var talent = new Talent { CreateUserId = player, Version = 1 };
        using (var transaction = repository.OpenTransaction())
        {
            Require(await repository.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM Player WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Player AND ClientId=@ClientId AND IsClientLogin=1",
                new { Player = player, ClientId = clientId }) == 1, "exact economy fixture identity");
            await repository.InsertAsync(talent);
            transaction.Commit();
        }
        async Task SetTalents(params string[] values)
        {
            Require(values.Length == 3, "three gold talent seed values");
            using var transaction = repository.OpenTransaction();
            await repository.ExecuteAsync(
                "DELETE FROM TalentAcquired WHERE CreateUserId=@Player AND TalentId=@Talent AND TreeId='utility'",
                new { Player = player, Talent = talent.Id });
            for (int i = 0; i < 3; i++)
                await repository.InsertAsync(new TalentAcquired
                {
                    CreateUserId = player, TalentId = talent.Id, TreeId = "utility",
                    NodeId = $"gold_{i + 1}", Value = values[i], Sequence = i,
                });
            transaction.Commit();
        }
        foreach (var scenario in new[]
        {
            (Values: new[] { "true", "false", "2" }, Bonus: 4),
            (Values: new[] { "false", "true", "false" }, Bonus: 2),
            (Values: new[] { "0", "-1", "0" }, Bonus: 0),
            (Values: new[] { "3", "2", "1" }, Bonus: 6),
        })
        {
            await SetTalents(scenario.Values);
            await Reward(scenario.Bonus);
        }
        checks.Add("PASS: true/false and positive/zero/negative integer gold talents award the exact bonus once per node.");
        foreach (string malformed in new[] { "\"bad\"", "1.5", "{\"bad\":1}", "{" })
        {
            await SetTalents(malformed, "0", "0");
            var failed = new EnemyKillRequest
                { RequestId = Guid.NewGuid(), SessionId = wave.RequestId, EnemyIndex = enemy++ };
            await Rejected(http, EconomyRoutes[1], failed, messageKey: ApiMessageKeys.ErrorUnexpected);
            Require(await Gold(http) == gold, "malformed talent rolls back gold");
            await AssertReceiptCount(repository, player, failed.RequestId, 0);
            await SetTalents("0", "0", "0");
            await Reward(0, failed); // Exact rejected intent remains usable after fixture repair.
            await AssertReceiptCount(repository, player, failed.RequestId, 1);
        }
        checks.Add("PASS: malformed string/fraction/object/JSON talents reject atomically; no gold or receipt, then same intent succeeds after repair.");
    }
}
