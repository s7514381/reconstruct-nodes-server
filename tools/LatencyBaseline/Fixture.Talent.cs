using System.Net;
using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

internal static partial class Fixture
{
    private static readonly string[] TalentRoutes = ["Talent/LearnTalent"];
    public static bool TalentBudgetMet;

    private static TalentNodeCommandRequest TalentRequest(long revision, string node, Guid? id = null) => new()
    {
        RequestId = id ?? Guid.NewGuid(), ExpectedRevision = revision, TreeId = "special_mana", NodeId = node,
    };

    private static async Task<TalentModel> TalentRead(HttpClient http) =>
        (await Post(http, "Talent/GetSnapshot", new { })).Data!.Deserialize<TalentModel>(Json)!;

    private static async Task SeedTalent(DapperRepository repository, Guid player, long points)
    {
        using var transaction = repository.OpenTransaction();
        await repository.InsertAsync(new Talent { CreateUserId = player, Version = 1, TalentPoints = points });
        transaction.Commit();
    }

    private static async Task TalentRegressions(HttpClient http, DapperRepository repository, Guid player, List<string> checks)
    {
        var start = await TalentRead(http);
        var request = TalentRequest(start.Revision, "mana_02");
        var first = await Post(http, "Talent/LearnTalent", request);
        var receipt = first.Data!.Deserialize<CommandReceipt<TalentModel>>(Json)!;
        Require(receipt.RequestId == request.RequestId && receipt.Revision == start.Revision + 1 &&
            receipt.Snapshot.Revision == receipt.Revision && receipt.Snapshot.TalentPoints == start.TalentPoints - 1 &&
            receipt.Snapshot.TotalSpentPoints == start.TotalSpentPoints + 1 &&
            receipt.Snapshot.AcquiredTalents["special_mana"]["mana_02"].GetInt32() == 1,
            "talent learn receipt advances revision once and spends one point");
        var readback = await TalentRead(http);
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(receipt.Snapshot), JsonSerializer.SerializeToNode(readback));
        await AssertReceiptCount(repository, player, request.RequestId, 1);
        checks.Add("PASS: LearnTalent receipt equals the authoritative Talent/GetSnapshot readback.");

        var replay = await Post(http, "Talent/LearnTalent", request);
        AssertSnapshotEqual(first.Data, replay.Data);
        Require((await TalentRead(http)).Revision == readback.Revision, "replay never advances the revision again");
        await AssertReceiptCount(repository, player, request.RequestId, 1);
        checks.Add("PASS: same request ID and payload replays the original receipt with no new write.");

        await Rejected(http, "Talent/LearnTalent", TalentRequest(readback.Revision, "mana_03", request.RequestId),
            HttpStatusCode.Conflict, ApiMessageKeys.EconomyRequestConflict);
        await Rejected(http, "Talent/LearnTalent", TalentRequest(start.Revision, "mana_03"),
            HttpStatusCode.Conflict, "concurrency.revision_conflict");
        await Rejected(http, "Talent/LearnTalent", TalentRequest(readback.Revision, "mana_09"),
            HttpStatusCode.BadRequest, "talent.learn_unavailable");
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(readback), JsonSerializer.SerializeToNode(await TalentRead(http)));
        checks.Add("PASS: changed payload, stale revision and locked node are rejected without changing state.");

        var refund = await Post(http, "Talent/RefundTalent", TalentRequest(readback.Revision, "mana_02"));
        var refunded = refund.Data!.Deserialize<CommandReceipt<TalentModel>>(Json)!;
        Require(refunded.Snapshot.TalentPoints == start.TalentPoints && refunded.Snapshot.TotalSpentPoints == start.TotalSpentPoints &&
            !refunded.Snapshot.AcquiredTalents.ContainsKey("special_mana"), "refund restores points and removes node");
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(refunded.Snapshot), JsonSerializer.SerializeToNode(await TalentRead(http)));

        await Post(http, "Talent/LearnTalent", TalentRequest(refunded.Revision, "mana_01"));
        await Post(http, "Talent/LearnTalent", TalentRequest(refunded.Revision + 1, "mana_01"));
        var reset = await Post(http, "Talent/ResetTalents", new TalentResetRequest
        { RequestId = Guid.NewGuid(), ExpectedRevision = refunded.Revision + 2 });
        var resetReceipt = reset.Data!.Deserialize<CommandReceipt<TalentModel>>(Json)!;
        Require(resetReceipt.Snapshot.TalentPoints == start.TalentPoints && resetReceipt.Snapshot.AcquiredTalents.Count == 0,
            "reset refunds every point");
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(resetReceipt.Snapshot), JsonSerializer.SerializeToNode(await TalentRead(http)));
        checks.Add("PASS: RefundTalent and ResetTalents restore points and match the readback.");
    }

    private static async Task MeasureTalent(HttpClient http, Func<string, Func<int, Task<Sample>>, Task> measure)
    {
        long revision = (await TalentRead(http)).Revision;
        await measure(TalentRoutes[0], async i =>
        {
            var learn = await Post(http, "Talent/LearnTalent", TalentRequest(revision, "mana_01"));
            var receipt = learn.Data!.Deserialize<CommandReceipt<TalentModel>>(Json)!;
            Require(receipt.Revision == revision + 1, "measured talent revision");
            // Undo so every sample learns from the same state (untimed).
            var undo = await Post(http, "Talent/RefundTalent", TalentRequest(receipt.Revision, "mana_01"));
            revision = undo.Data!.Deserialize<CommandReceipt<TalentModel>>(Json)!.Revision;
            return learn.Sample;
        });
    }
}
