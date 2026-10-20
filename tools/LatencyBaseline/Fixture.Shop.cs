using System.Net;
using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

internal static partial class Fixture
{
    private sealed record ShopInventory(long Gold, Dictionary<Guid, CharacterModel> Characters,
        Dictionary<string, WeaponInstanceModel> Weapons, Dictionary<string, (int Amount, string Category)> Items);
    private sealed record DrawValidation(ShopInventory Expected, int NewCharacters, int SameKindRepeats);

    private static int DrawCost(int count) => count switch
    {
        1 => ShopCommand.Catalog.SingleCost,
        10 => ShopCommand.Catalog.TenCost,
        _ => throw new InvalidOperationException("Invalid fixture draw count"),
    };

    private static async Task<long> FundShop(HttpClient http, long target)
    {
        long gold = await Gold(http);
        while (gold < target)
        {
            var wave = await PostBackground(http, new WaveStartRequest { RequestId = Guid.NewGuid(), Wave = 100, Difficulty = 0 });
            Guid session = wave.Data!["session_id"]!.GetValue<Guid>();
            Require(wave.Data["enemy_count"]!.GetValue<int>() == 307, "funding wave count");
            // All indices are unique non-boss kills. Index 306 rewards 864, not 108,
            // so do not accidentally include it in the fixed-reward funding assertion.
            for (int i = 0; i < 306 && gold < target; i++)
            {
                var kill = await PostBackground(http, new EnemyKillRequest
                    { RequestId = Guid.NewGuid(), SessionId = session, EnemyIndex = i });
                gold += 108;
                Require(kill.Data!["gold"]!.GetValue<long>() == gold, "funding exact fresh reward");
            }
        }
        return gold;
    }

    private static async Task<ShopInventory> ShopReadback(HttpClient http)
    {
        var characters = (await Post(http, "Character/GetList", new { })).Data!
            .Deserialize<List<CharacterModel>>(Json)!.ToDictionary(c => c.Id!.Value);
        var weapons = (await Post(http, "Weapon/GetInstanceList", new { })).Data!
            .Deserialize<List<WeaponInstanceModel>>(Json)!.ToDictionary(w => w.Id);
        var items = (await Post(http, "Item/GetInStockList", new { })).Data!
            .Deserialize<List<Item>>(Json)!.ToDictionary(i => i.ItemKey, i => (i.Amount, i.Category));
        return new(await Gold(http), characters, weapons, items);
    }

    private static object CharacterContent(CharacterModel c) => new
    {
        c.Id, c.Kind, c.Level, c.Personality, c.Fate, c.WeaponEntryId, c.WeaponId, c.WeaponRarity, c.WeaponAffixes,
        c.WeaponUnique, c.WeaponQualityVersion, c.SkillTrees,
    };

    private static object WeaponContent(WeaponInstanceModel w) => new
        { w.Id, w.WeaponId, w.WeaponFamilyId, w.Rarity, w.Affixes, w.Unique };

    private static void AssertShopInventory(ShopInventory expected, ShopInventory actual)
    {
        Require(expected.Gold == actual.Gold &&
            expected.Characters.Keys.Order().SequenceEqual(actual.Characters.Keys.Order()) &&
            expected.Weapons.Keys.Order().SequenceEqual(actual.Weapons.Keys.Order()) &&
            expected.Items.OrderBy(p => p.Key).SequenceEqual(actual.Items.OrderBy(p => p.Key)),
            "shop authoritative gold, ownership and item amounts/category readback");
        foreach (var pair in expected.Characters)
            AssertSnapshotEqual(JsonSerializer.SerializeToNode(CharacterContent(pair.Value)),
                JsonSerializer.SerializeToNode(CharacterContent(actual.Characters[pair.Key])));
        foreach (var pair in expected.Weapons)
            AssertSnapshotEqual(JsonSerializer.SerializeToNode(WeaponContent(pair.Value)),
                JsonSerializer.SerializeToNode(WeaponContent(actual.Weapons[pair.Key])));
    }

    private static DrawValidation ValidateDraw(ShopInventory before, ShopDrawResult receipt, Guid request, int count)
    {
        Require(receipt.RequestId == request && receipt.Gold == before.Gold - DrawCost(count) &&
            receipt.Results.Count == count && receipt.State?.Version == 1, "draw exact receipt and debit");
        var state = receipt.State!;
        var characters = new Dictionary<Guid, CharacterModel>(before.Characters);
        var weapons = new Dictionary<string, WeaponInstanceModel>(before.Weapons);
        var drawnKinds = new HashSet<CharacterKind>();
        int sameKindRepeats = 0;
        foreach (var reward in receipt.Results)
        {
            var offer = ShopCommand.Catalog.Characters.Single(o => o.Kind == reward.CharacterKind);
            // Every roll is a brand-new instance, even when the player already owns that kind.
            Require(reward.CharacterId != Guid.Empty && !string.IsNullOrEmpty(reward.WeaponEntryId), "acquisition receipt");
            var character = state.Characters.Single(c => c.Id == reward.CharacterId);
            var weapon = state.Weapons.Single(w => w.Id == reward.WeaponEntryId);
            int skill = reward.CharacterKind switch { CharacterKind.Warrior => 5, CharacterKind.Archer => 0, CharacterKind.Mage => 3, CharacterKind.Crusader => 5, CharacterKind.ShadowMage => 2, CharacterKind.IceMage => 2, CharacterKind.FireMage => 1, CharacterKind.ShadowAssassin => 5, CharacterKind.FlameSwordsman => 5, CharacterKind.WindHunter => 0, CharacterKind.Ranger => 0, CharacterKind.DesertRanger => 0, _ => -1 };
            Require(character.Kind == reward.CharacterKind && character.WeaponEntryId == weapon.Id &&
                character.WeaponId == offer.WeaponId && character.WeaponRarity == "common" &&
                character.WeaponQualityVersion == 2 && character.WeaponAffixes.Count == 0 &&
                character.WeaponUnique.Count == 0 && character.SkillTrees.Count == 1 &&
                character.SkillTrees[0].Kind == skill && character.SkillTrees[0].AuxiliaryNodes.Count == 0 &&
                character.Level == CharacterLevelRules.InitialLevel &&
                CharacterTraits.IsDefined(character.Personality) && CharacterTraits.IsDefined(character.Fate),
                "acquisition equipment, default skill and trait roll");
            Require(weapon.WeaponId == offer.WeaponId && weapon.WeaponFamilyId == offer.WeaponId &&
                weapon.Rarity == "common" && weapon.Affixes.Count == 0 && weapon.Unique.Count == 0,
                "acquisition weapon snapshot");
            Require(characters.TryAdd(character.Id!.Value, character), "new character unique id");
            Require(weapons.TryAdd(weapon.Id, weapon), "new weapon unique entry");
            if (!drawnKinds.Add(character.Kind)) sameKindRepeats++;
        }
        Require(state.Characters.Count == receipt.Results.Count && state.Weapons.Count == receipt.Results.Count,
            "draw state contains exactly its mutations");
        return new(new(receipt.Gold, characters, weapons, before.Items), receipt.Results.Count, sameKindRepeats);
    }

    private static async Task ShopRegressions(HttpClient http, DapperRepository repository,
        Guid player, List<string> checks)
    {
        var inventory = await ShopReadback(http);
        Require(inventory.Characters.Values.All(c => c.Kind == CharacterService.CoreKind), "fresh shop fixture before first acquisition");
        // Ten rolls over only four kinds guarantees at least one kind is drawn twice in one batch,
        // without controlling randomness or retrying/rejecting successful rolls.
        foreach (int count in new[] { 10, 1 })
        {
            Guid id = Guid.NewGuid();
            var request = new ShopDrawRequest { RequestId = id, Count = count };
            var first = await Post(http, "Shop/Draw", request);
            var validation = ValidateDraw(inventory, first.Data!.Deserialize<ShopDrawResult>(Json)!, id, count);
            if (count == 10)
                Require(validation.NewCharacters == 10 && validation.SameKindRepeats > 0,
                    "first ten-draw creates ten independent instances including repeated kinds");
            inventory = validation.Expected;
            AssertShopInventory(inventory, await ShopReadback(http));
            checks.Add($"PASS: count={count} receipt matches readback of character traits/skills/equipment and weapons.");
            var replay = await Post(http, "Shop/Draw", request);
            AssertSnapshotEqual(first.Data, replay.Data);
            AssertShopInventory(inventory, await ShopReadback(http));
            await AssertReceiptCount(repository, player, id, 1);
            checks.Add($"PASS: count={count} replay returns the original full result with no new debit, characters or weapons.");
            await Rejected(http, "Shop/Draw", new ShopDrawRequest { RequestId = id, Count = count == 1 ? 10 : 1 },
                HttpStatusCode.Conflict, ApiMessageKeys.EconomyRequestConflict);
            AssertShopInventory(inventory, await ShopReadback(http));
            await AssertReceiptCount(repository, player, id, 1);
            checks.Add($"PASS: count={count} request ID with changed Count returns 409 and leaves all state unchanged.");
        }
    }

    private static async Task MeasureShop(HttpClient http, Func<string, Func<int, Task<Sample>>, Task> measure)
    {
        var inventory = await ShopReadback(http);
        foreach (int count in new[] { 1, 10 })
        {
            await measure(count == 1 ? ShopRoutes[0] : ShopRoutes[1], async _ =>
            {
                Guid id = Guid.NewGuid();
                var result = await Post(http, "Shop/Draw", new ShopDrawRequest { RequestId = id, Count = count });
                inventory = ValidateDraw(inventory, result.Data!.Deserialize<ShopDrawResult>(Json)!, id, count).Expected;
                return result.Sample;
            });
            AssertShopInventory(inventory, await ShopReadback(http));
        }
    }

    public static void ShopSelfTest()
    {
        Guid id = Guid.NewGuid();
        var offer = ShopCommand.Catalog.Characters.Single(c => c.Kind == 0);
        var before = new ShopInventory(2_000, new(), new(), new());
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var receipt = new ShopDrawResult
        {
            RequestId = id, Gold = before.Gold - DrawCost(10), State = new()
            {
                Characters = ids.Select((characterId, i) => new CharacterModel
                {
                    Id = characterId, Kind = 0, WeaponEntryId = $"test-sword-{i}",
                    WeaponId = offer.WeaponId, WeaponRarity = "common", WeaponQualityVersion = 2,
                    Level = CharacterLevelRules.InitialLevel, Personality = CharacterPersonality.Jolly, Fate = CharacterFate.Tianfu,
                    SkillTrees = [new GameProgressSkillLoadoutModel { Kind = 5 }],
                }).ToList(),
                Weapons = ids.Select((_, i) => new WeaponInstanceModel { Id = $"test-sword-{i}", WeaponId = offer.WeaponId,
                    WeaponFamilyId = offer.WeaponId, Rarity = "common" }).ToList(),
            },
            Results = ids.Select((characterId, i) => new ShopDrawReward
            {
                CharacterId = characterId, CharacterKind = 0, WeaponEntryId = $"test-sword-{i}",
            }).ToList(),
        };
        var valid = ValidateDraw(before, receipt, id, 10);
        Require(valid.NewCharacters == 10 && valid.SameKindRepeats == 9 && valid.Expected.Characters.Count == 10,
            "ten independent instances of the same kind");
        AssertShopInventory(valid.Expected, valid.Expected);
        foreach (Action<ShopDrawResult> corrupt in new Action<ShopDrawResult>[]
        {
            r => r.Gold++, r => r.RequestId = Guid.NewGuid(),
            r => r.State!.Characters[0].SkillTrees[0].Kind = 0,
            r => r.State!.Weapons[0].WeaponId = "wrong",
            r => r.State!.Characters[0].Level = 2,
            r => r.State!.Characters[0].Personality = (CharacterPersonality)99,
            r => r.State!.Characters.RemoveAt(1),
        })
        {
            var invalid = Clone(receipt);
            corrupt(invalid);
            bool rejected = false;
            try { ValidateDraw(before, invalid, id, 10); }
            catch (FixtureAssertion) { rejected = true; }
            Require(rejected, "corrupt draw receipt rejected");
        }
        Console.WriteLine("Shop receipt/skill/weapon/instance self-tests passed; no database or HTTP access.");
    }
}
