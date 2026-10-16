using System.Net;
using System.Text.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

internal static partial class Fixture
{
    private static readonly string[] SkillRoutes = ["Character/ConfigureSkills"];
    public static bool SkillBudgetMet;

    private static SkillLoadoutRequest SkillRequest(Guid character, long revision, Guid? id = null, bool withNodes = true) => new()
    {
        RequestId = id ?? Guid.NewGuid(), CharacterId = character, ExpectedRevision = revision,
        SkillTrees =
        [
            new GameProgressSkillLoadoutModel
            {
                Kind = 0,
                AuxiliaryNodes = withNodes
                    ? [new() { Type = "auxiliary", LootId = "hit_trigger_gem" }, new() { Type = "active", Kind = 1 },
                        new() { Type = "auxiliary", LootId = "auxiliary_gem" }]
                    : [],
            },
        ],
    };

    private static async Task<CharacterModel> SkillCore(HttpClient http) =>
        (await Post(http, "Character/GetList", new { })).Data!.Deserialize<List<CharacterModel>>(Json)!.Single();

    private static async Task SkillRegressions(HttpClient http, DapperRepository repository, Guid player,
        CharacterModel core, List<string> checks)
    {
        // 解鎖資料直接 seed：ConfigureSkills 只讀取這兩個群組。
        foreach (string loot in new[] { "hit_trigger_gem", "auxiliary_gem" })
            await repository.ExecuteAsync("""
                INSERT INTO PlayerParams (Id, CreateDate, CreateUserId, Status, [Group], Name, Value, Sequence)
                VALUES (NEWID(), GETDATE(), @Player, 1, 'game_progress.unlocked_auxiliary_kinds', @Name, 'true', 0)
                """, new { Player = player, Name = loot });
        core = await SkillCore(http);
        Guid characterId = core.Id!.Value;

        var request = SkillRequest(characterId, core.Revision);
        var first = await Post(http, "Character/ConfigureSkills", request);
        var receipt = first.Data!.Deserialize<CommandReceipt<CharacterModel>>(Json)!;
        Require(receipt.RequestId == request.RequestId && receipt.Revision == core.Revision + 1 &&
            receipt.Snapshot.Revision == receipt.Revision, "skill receipt advances revision exactly once");
        var nodes = receipt.Snapshot.SkillTrees.Single().AuxiliaryNodes;
        Require(receipt.Snapshot.SkillTrees.Single().Kind == 0 && nodes.Count == 3 &&
            nodes[0].LootId == "hit_trigger_gem" && nodes[1].Type == "active" && nodes[1].Kind == 1 &&
            nodes[2].LootId == "auxiliary_gem", "skill receipt carries the configured tree in order");
        var readback = await SkillCore(http);
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(receipt.Snapshot), JsonSerializer.SerializeToNode(readback));
        await AssertReceiptCount(repository, player, request.RequestId, 1);
        checks.Add("PASS: ConfigureSkills receipt equals the authoritative Character/GetList readback.");

        var replay = await Post(http, "Character/ConfigureSkills", request);
        AssertSnapshotEqual(first.Data, replay.Data);
        Require((await SkillCore(http)).Revision == readback.Revision, "replay never advances the revision again");
        await AssertReceiptCount(repository, player, request.RequestId, 1);
        checks.Add("PASS: same request ID and payload replays the original receipt with no new write.");

        var changed = SkillRequest(characterId, core.Revision, request.RequestId, withNodes: false);
        await Rejected(http, "Character/ConfigureSkills", changed, HttpStatusCode.Conflict, ApiMessageKeys.EconomyRequestConflict);
        checks.Add("PASS: same request ID with a different payload is rejected with 409.");

        await Rejected(http, "Character/ConfigureSkills", SkillRequest(characterId, core.Revision, withNodes: false),
            HttpStatusCode.Conflict, "concurrency.revision_conflict");
        var locked = SkillRequest(characterId, readback.Revision);
        locked.SkillTrees[0].AuxiliaryNodes[0].LootId = "critical_trigger_gem";
        await Rejected(http, "Character/ConfigureSkills", locked, HttpStatusCode.BadRequest, ApiMessageKeys.SkillSupportNodeLocked);
        await Rejected(http, "Character/ConfigureSkills", SkillRequest(Guid.NewGuid(), readback.Revision),
            HttpStatusCode.BadRequest, ApiMessageKeys.CharacterNotOwned);
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(readback), JsonSerializer.SerializeToNode(await SkillCore(http)));
        checks.Add("PASS: stale revision, locked support gem and foreign character are rejected without changing state.");
    }

    private static async Task MeasureSkills(HttpClient http, Guid characterId,
        Func<string, Func<int, Task<Sample>>, Task> measure)
    {
        long revision = (await SkillCore(http)).Revision;
        await measure(SkillRoutes[0], async i =>
        {
            // 交替配置，確保每次都真的替換子列。
            var result = await Post(http, "Character/ConfigureSkills",
                SkillRequest(characterId, revision, withNodes: i % 2 == 0));
            var receipt = result.Data!.Deserialize<CommandReceipt<CharacterModel>>(Json)!;
            Require(receipt.Revision == revision + 1 && receipt.Snapshot.Revision == receipt.Revision, "measured skill revision");
            revision = receipt.Revision;
            return result.Sample;
        });
    }
}
