using System.Text.Json;
using System.Text.Json.Nodes;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

internal static partial class Fixture
{
    private static readonly string[] ReadRoutes =
    [
        "Character/GetList", "GameProgress/Get", "Talent/GetSnapshot",
        "Weapon/GetInstanceList", "AchievementProgress/Get",
    ];

    private static bool ReadsComplete(IEnumerable<string> routes, bool correlated, bool cleaned,
        string stage, int checks) =>
        SelectionComplete(routes, ReadRoutes, correlated, cleaned,
            stage == "reads completed" && checks == ReadRoutes.Length);

    private static WeaponInstanceModel RichWeapon(string id, int offset) => new()
    {
        Id = id, WeaponId = "staff", WeaponFamilyId = "staff", Rarity = "rare",
        Affixes =
        [
            JsonSerializer.SerializeToElement(new { id = "latency-affix-a", values = new[] { offset + 3, offset + 4 } }),
            JsonSerializer.SerializeToElement("latency-unrolled"),
            JsonSerializer.SerializeToElement(new { id = "latency-affix-b", values = new[] { offset + 7, offset + 8 } }),
        ],
        Unique = new()
        {
            ["unique_id"] = JsonSerializer.SerializeToElement("latency-unique"),
            ["values"] = JsonSerializer.SerializeToElement(new[] { offset + 11, offset + 12 }),
        },
    };

    private static TalentModel RichTalent() => new()
    {
        Version = 1, TalentPoints = 7, TotalSpentPoints = 3, PointsRecalculationVersion = 1,
        AcquiredTalents = new()
        {
            ["utility"] = new()
            {
                ["gold_1"] = JsonSerializer.SerializeToElement(1),
                ["gold_2"] = JsonSerializer.SerializeToElement(2),
                ["gold_3"] = JsonSerializer.SerializeToElement(3),
            },
        },
        LegacyCategoryTalents = new()
        {
            ["legacy"] = new()
            {
                ["first"] = JsonSerializer.SerializeToElement(true),
                ["second"] = JsonSerializer.SerializeToElement(false),
            },
        },
    };

    private static async Task SeedRichReads(HttpClient http, DapperRepository repository,
        Guid player, string clientId, CharacterModel core,
        AchievementProgressModel achievements)
    {
        foreach (var weapon in new[] { RichWeapon("latency-rich-equipped", 0), RichWeapon("latency-rich-spare", 100) })
            await PostBackground(http, new WeaponAcquireRequest { RequestId = Guid.NewGuid(), Weapon = weapon });
        // Character/Save only accepts owned characters; seed a second owned row, not a
        // rejected create or randomized draw. All rows belong to this exact new guest.
        using (var transaction = repository.OpenTransaction())
        {
            Require(await repository.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM Player WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Player AND ClientId=@ClientId AND IsClientLogin=1",
                new { Player = player, ClientId = clientId }) == 1, "exact rich fixture identity");
            var unarmed = new Character { CreateUserId = player, Kind = CharacterKind.Warrior };
            // Kind 0's default active skill is 5; active 0 is always available.
            // Do not seed locked skill 2 or an unknown/locked auxiliary loot ID.
            var tree = new CharacterSkillTree { CreateUserId = player, CharacterId = unarmed.Id, Kind = 5, Sequence = 0 };
            await repository.InsertAsync(unarmed);
            await repository.InsertAsync(tree);
            await repository.InsertAsync(new CharacterSkillNode
            {
                CreateUserId = player, CharacterSkillTreeId = tree.Id, Sequence = 0,
                NodeType = GameProgressSkillNodeModel.ActiveType, Kind = 0,
            });
            await repository.InsertAsync(new CharacterSkillNode
            {
                CreateUserId = player, CharacterSkillTreeId = tree.Id, Sequence = 1,
                NodeType = GameProgressSkillNodeModel.ActiveType, Kind = 5,
            });
            transaction.Commit();
        }
        await Post(http, "Character/EquipWeapon", new WeaponEquipRequest
        {
            RequestId = Guid.NewGuid(), CharacterId = core.Id!.Value, WeaponEntryId = "latency-rich-equipped",
        });
        var talent = RichTalent();
        talent.ExpectedRevision = (await Post(http, "Talent/GetSnapshot", new { })).Data!
            .Deserialize<TalentModel>(Json)!.Revision;
        await SaveBackground(http, talent);
        achievements.Groups.Single(g => g.Name == "weapons_collected").Collected = ["staff", "sword", "bow"];
        achievements.Groups.Single(g => g.Name == "affixes_seen").Collected = ["latency-affix-a", "latency-affix-b"];
        await SaveBackground(http, achievements);
        await ClaimAchievement(http, "enemy_kills_10");
    }

    private static async Task<Dictionary<string, JsonNode?>> LegacyReadSnapshots(
        DapperRepository repository, Guid player, GameProgressModel seededProgress)
    {
        // Never perform HTTP while holding the player lock: the command owns its transaction.
        using var transaction = repository.OpenTransaction();
        var argument = CreateServiceArgument(repository, player);
        var characters = await new CharacterService(argument, new CharacterSkillTreeService(argument), new CharacterSkillNodeService(argument)).GetList();
        var progress = await new GameProgressCommand(argument, new DeploymentService(argument), new BattlefieldRoadService(argument), new PlayerParamsService(argument), new RevisionCommand(argument)).Get();
        var talent = await new TalentService(argument, new TalentAcquiredService(argument), new TalentLegacyCategoryService(argument)).GetSnapshot();
        var weapons = (await new WeaponService(argument, new WeaponAffixService(argument), new WeaponAffixValueService(argument), new WeaponUniqueValueService(argument)).GetInstanceList()).ToList();
        var achievements = await new AchievementProgressCommand(argument, new AchievementService(argument), new AchievementDetailService(argument), new AchievementCollectedService(argument), new RevisionCommand(argument)).Get();

        Require(weapons.Count == 3, "rich equipped and unequipped inventory");
        foreach (var seed in new[] { RichWeapon("latency-rich-equipped", 0), RichWeapon("latency-rich-spare", 100) })
        {
            var weapon = weapons.Single(w => w.Id == seed.Id);
            AssertSnapshotEqual(JsonSerializer.SerializeToNode(seed.Affixes), JsonSerializer.SerializeToNode(weapon.Affixes));
            AssertSnapshotEqual(JsonSerializer.SerializeToNode(seed.Unique), JsonSerializer.SerializeToNode(weapon.Unique));
        }
        Require(characters.Count == 2, "rich character count");
        var equipped = characters.Single(c => c.Kind == CharacterService.CoreKind);
        var unarmed = characters.Single(c => c.Kind == CharacterKind.Warrior);
        var equippedWeapon = weapons.Single(w => w.Id == "latency-rich-equipped");
        AssertUnarmed(unarmed);
        Require(equipped.WeaponEntryId == equippedWeapon.Id, "rich equipped weapon entry");
        Require(equipped.WeaponId == equippedWeapon.WeaponId &&
            equipped.WeaponRarity == equippedWeapon.Rarity, "rich equipped weapon identity and rarity");
        Require(equipped.SkillTrees.Count > 0, "rich core skills preserved by equip");
        Require(unarmed.SkillTrees.Single().Kind == 5 &&
            unarmed.SkillTrees[0].AuxiliaryNodes.Count == 2 &&
            unarmed.SkillTrees[0].AuxiliaryNodes[0].Type == GameProgressSkillNodeModel.ActiveType &&
            unarmed.SkillTrees[0].AuxiliaryNodes[0].Kind == 0 &&
            unarmed.SkillTrees[0].AuxiliaryNodes[1].Type == GameProgressSkillNodeModel.ActiveType &&
            unarmed.SkillTrees[0].AuxiliaryNodes[1].Kind == 5, "rich ordered unlocked skill children");
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(equippedWeapon.Affixes), JsonSerializer.SerializeToNode(equipped.WeaponAffixes));
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(equippedWeapon.Unique), JsonSerializer.SerializeToNode(equipped.WeaponUnique));
        Require(talent != null && talent.TalentPoints == 7 && talent.TotalSpentPoints == 3 &&
            talent.PointsRecalculationVersion == 1, "rich talent scalar values");
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(RichTalent().AcquiredTalents), JsonSerializer.SerializeToNode(talent!.AcquiredTalents));
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(RichTalent().LegacyCategoryTalents), JsonSerializer.SerializeToNode(talent.LegacyCategoryTalents));
        Require(progress?.Battlefield?.Roads.Count == 8, "rich road rows");
        AssertSnapshotEqual(JsonSerializer.SerializeToNode(seededProgress.Battlefield), JsonSerializer.SerializeToNode(progress!.Battlefield));
        Require(achievements != null && achievements.Groups.Count == 6 &&
            achievements.Groups.Sum(g => g.Items.Count) == 33 &&
            achievements.Groups.Single(g => g.Name == "weapons_collected").Collected.SequenceEqual(["staff", "sword", "bow"]) &&
            achievements.Groups.Single(g => g.Name == "affixes_seen").Collected.SequenceEqual(["latency-affix-a", "latency-affix-b"]),
            "rich achievement items and collections");
        var claimed = achievements!.Groups.SelectMany(g => g.Items).Single(i => i.Name == "enemy_kills_10");
        Require(claimed.IsReward && claimed.RewardDate.HasValue, "rich claimed achievement date");

        var result = new Dictionary<string, JsonNode?>
        {
            [ReadRoutes[0]] = JsonSerializer.SerializeToNode(characters, Json),
            [ReadRoutes[1]] = JsonSerializer.SerializeToNode(progress, Json),
            [ReadRoutes[2]] = JsonSerializer.SerializeToNode(talent, Json),
            [ReadRoutes[3]] = JsonSerializer.SerializeToNode(weapons, Json),
            [ReadRoutes[4]] = JsonSerializer.SerializeToNode(achievements, Json),
        };
        transaction.Rollback();
        return result;
    }

    private static void AssertSnapshotEqual(JsonNode? expected, JsonNode? actual, string? label = null) =>
        Require(expected != null && actual != null && JsonNode.DeepEquals(expected, actual),
            "HTTP command / legacy service rich snapshot equality" + (label == null ? "" : $" ({label})"));

    private static void AssertUnarmed(CharacterModel character)
    {
        // GameProgressEquipmentModel explicitly represents no equipment with empty
        // strings/collections, not null. Do not reject the legacy mapper's defaults.
        Require(character.WeaponEntryId == string.Empty && character.WeaponId == string.Empty &&
            character.WeaponRarity == string.Empty, "rich unarmed empty equipment identifiers");
        Require(character.WeaponAffixes.Count == 0 && character.WeaponUnique.Count == 0 &&
            character.WeaponQualityVersion == null, "rich unarmed empty equipment attributes");
    }

    public static void ReadsSelfTest()
    {
        AssertUnarmed(new CharacterModel { Kind = CharacterKind.Warrior });
        foreach (var invalid in new[]
        {
            new CharacterModel { WeaponEntryId = "unexpected-equipped" },
            new CharacterModel { WeaponId = "staff" },
            new CharacterModel { WeaponRarity = "rare" },
            new CharacterModel { WeaponQualityVersion = 2 },
            new CharacterModel { WeaponAffixes = [JsonSerializer.SerializeToElement("unexpected-affix")] },
            new CharacterModel { WeaponUnique = new() { ["unique_id"] = JsonSerializer.SerializeToElement("unexpected-unique") } },
        })
        {
            bool rejected = false;
            try { AssertUnarmed(invalid); }
            catch (FixtureAssertion) { rejected = true; }
            Require(rejected, "unexpected unarmed equipment rejected");
        }
        Require(ReadsComplete(ReadRoutes, true, true, "reads completed", 5), "five reads complete");
        Require(!ReadsComplete(ReadRoutes.Take(4), true, true, "reads completed", 5) &&
            !ReadsComplete(ReadRoutes.Take(4).Append("wrong/route"), true, true, "reads completed", 5) &&
            !ReadsComplete(ReadRoutes.Append(ReadRoutes[0]), true, true, "reads completed", 5) &&
            !ReadsComplete(ReadRoutes, false, true, "reads completed", 5) &&
            !ReadsComplete(ReadRoutes, true, false, "reads completed", 5) &&
            !ReadsComplete(ReadRoutes, true, true, "Character/GetList", 5) &&
            !ReadsComplete(ReadRoutes, true, true, "reads completed", 4), "partial reads incomplete");
        AssertSnapshotEqual(JsonNode.Parse("""{"a":[1,2],"b":true}"""),
            JsonNode.Parse("""{"b":true,"a":[1,2]}"""));
        foreach (string changed in new[] { """{"a":[2,1],"b":true}""", """{"a":[1,2]}""",
            """{"a":[1,3],"b":true}""", "null" })
        {
            bool rejected = false;
            try { AssertSnapshotEqual(JsonNode.Parse("""{"a":[1,2],"b":true}"""), JsonNode.Parse(changed)); }
            catch (FixtureAssertion) { rejected = true; }
            Require(rejected, "changed snapshot rejected");
        }
        Require(!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(RichWeapon("equipped", 0).Affixes),
            JsonSerializer.SerializeToNode(RichWeapon("spare", 100).Affixes)), "distinct rich child values");
        Console.WriteLine("Read selection and rich snapshot comparator self-tests passed; no database or HTTP access.");
    }
}
