using System.Net;
using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

internal static partial class Fixture
{
    private sealed record ShopInventory(long Gold, Dictionary<int, CharacterModel> Characters,
        Dictionary<string, WeaponInstanceModel> Weapons, Dictionary<string, (int Amount, string Category)> Items);
    private sealed record DrawValidation(ShopInventory Expected, int NewCharacters, int SameBatchDuplicates);

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
            .Deserialize<List<CharacterModel>>(Json)!.ToDictionary(c => c.Kind);
        var weapons = (await Post(http, "Weapon/GetInstanceList", new { })).Data!
            .Deserialize<List<WeaponInstanceModel>>(Json)!.ToDictionary(w => w.Id);
        var items = (await Post(http, "Item/GetInStockList", new { })).Data!
            .Deserialize<List<Item>>(Json)!.ToDictionary(i => i.ItemKey, i => (i.Amount, i.Category));
        return new(await Gold(http), characters, weapons, items);
    }

    private static object CharacterContent(CharacterModel c) => new
    {
        c.Id, c.Kind, c.WeaponEntryId, c.WeaponId, c.WeaponRarity, c.WeaponAffixes,
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
            "shop authoritative gold, ownership and shard amounts/category readback");
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
        var characters = new Dictionary<int, CharacterModel>(before.Characters);
        var weapons = new Dictionary<string, WeaponInstanceModel>(before.Weapons);
        var items = new Dictionary<string, (int Amount, string Category)>(before.Items);
        var newKinds = new HashSet<int>();
        var touchedShards = new HashSet<string>();
        int sameBatchDuplicates = 0;
        foreach (var reward in receipt.Results)
        {
            var offer = ShopCommand.Catalog.Characters.Single(o => o.Kind == reward.CharacterKind);
            if (characters.TryGetValue(reward.CharacterKind, out var owned))
            {
                Require(reward.IsDuplicate && reward.CharacterId == owned.Id &&
                    reward.ShardId == offer.ShardId && reward.WeaponEntryId == "", "duplicate receipt preserves ownership");
                int amount = items.TryGetValue(offer.ShardId, out var item) ? item.Amount : 0;
                items[offer.ShardId] = (checked(amount + 1), "character_shard");
                touchedShards.Add(offer.ShardId);
                if (newKinds.Contains(reward.CharacterKind)) sameBatchDuplicates++;
                continue;
            }
            Require(!reward.IsDuplicate && reward.ShardId == "" &&
                reward.CharacterId != Guid.Empty && !string.IsNullOrEmpty(reward.WeaponEntryId),
                "first acquisition receipt");
            var character = state.Characters.Single(c => c.Id == reward.CharacterId);
            var weapon = state.Weapons.Single(w => w.Id == reward.WeaponEntryId);
            int skill = reward.CharacterKind switch { 0 => 5, 1 => 0, 2 => 3, _ => -1 };
            Require(character.Kind == reward.CharacterKind && character.WeaponEntryId == weapon.Id &&
                character.WeaponId == offer.WeaponId && character.WeaponRarity == "common" &&
                character.WeaponQualityVersion == 2 && character.WeaponAffixes.Count == 0 &&
                character.WeaponUnique.Count == 0 && character.SkillTrees.Count == 1 &&
                character.SkillTrees[0].Kind == skill && character.SkillTrees[0].AuxiliaryNodes.Count == 0,
                "first acquisition equipment and default skill");
            Require(weapon.WeaponId == offer.WeaponId && weapon.WeaponFamilyId == offer.WeaponId &&
                weapon.Rarity == "common" && weapon.Affixes.Count == 0 && weapon.Unique.Count == 0,
                "first acquisition weapon snapshot");
            characters.Add(character.Kind, character);
            Require(weapons.TryAdd(weapon.Id, weapon), "new weapon unique entry");
            newKinds.Add(character.Kind);
        }
        Require(state.Characters.Count == newKinds.Count && state.Weapons.Count == newKinds.Count &&
            touchedShards.SetEquals(state.Shards.Keys), "draw state contains exactly its mutations");
        foreach (string key in touchedShards)
            Require(state.Shards[key] == items[key].Amount, "same-batch absolute shard amount");
        return new(new(receipt.Gold, characters, weapons, items), newKinds.Count, sameBatchDuplicates);
    }

    private static async Task ShopRegressions(HttpClient http, DapperRepository repository,
        Guid player, List<string> checks)
    {
        var inventory = await ShopReadback(http);
        Require(inventory.Characters.Keys.All(k => k == CharacterService.CoreKind), "fresh shop fixture before first acquisition");
        // Ten rolls over only three kinds guarantees new acquisition + same-batch duplicates,
        // without controlling randomness or retrying/rejecting successful rolls.
        foreach (int count in new[] { 10, 1 })
        {
            Guid id = Guid.NewGuid();
            var request = new ShopDrawRequest { RequestId = id, Count = count };
            var first = await Post(http, "Shop/Draw", request);
            var validation = ValidateDraw(inventory, first.Data!.Deserialize<ShopDrawResult>(Json)!, id, count);
            if (count == 10)
                Require(validation.NewCharacters > 0 && validation.SameBatchDuplicates > 0,
                    "first ten-draw exercises acquisition and same-batch shard increments");
            inventory = validation.Expected;
            AssertShopInventory(inventory, await ShopReadback(http));
            checks.Add($"PASS: count={count} receipt matches readback of character skills/equipment, weapons and final shard amounts.");
            var replay = await Post(http, "Shop/Draw", request);
            AssertSnapshotEqual(first.Data, replay.Data);
            AssertShopInventory(inventory, await ShopReadback(http));
            await AssertReceiptCount(repository, player, id, 1);
            checks.Add($"PASS: count={count} replay returns the original full result with no new debit, characters, weapons or shards.");
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
        Guid id = Guid.NewGuid(), characterId = Guid.NewGuid();
        var offer = ShopCommand.Catalog.Characters.Single(c => c.Kind == 0);
        var before = new ShopInventory(2_000, new(), new(), new());
        var receipt = new ShopDrawResult
        {
            RequestId = id, Gold = before.Gold - DrawCost(10), State = new()
            {
                Characters = [new CharacterModel { Id = characterId, Kind = 0, WeaponEntryId = "test-sword",
                    WeaponId = offer.WeaponId, WeaponRarity = "common", WeaponQualityVersion = 2,
                    SkillTrees = [new GameProgressSkillLoadoutModel { Kind = 5 }] }],
                Weapons = [new WeaponInstanceModel { Id = "test-sword", WeaponId = offer.WeaponId,
                    WeaponFamilyId = offer.WeaponId, Rarity = "common" }],
                Shards = new() { [offer.ShardId] = 9 },
            },
            Results = Enumerable.Range(0, 10).Select(i => new ShopDrawReward
            {
                CharacterId = characterId, CharacterKind = 0, IsDuplicate = i > 0,
                ShardId = i > 0 ? offer.ShardId : "", WeaponEntryId = i == 0 ? "test-sword" : "",
            }).ToList(),
        };
        var valid = ValidateDraw(before, receipt, id, 10);
        Require(valid.NewCharacters == 1 && valid.SameBatchDuplicates == 9 &&
            valid.Expected.Items[offer.ShardId].Amount == 9, "same-batch acquisition and duplicates");
        AssertShopInventory(valid.Expected, valid.Expected);
        foreach (Action<ShopDrawResult> corrupt in new Action<ShopDrawResult>[]
        {
            r => r.Gold++, r => r.RequestId = Guid.NewGuid(),
            r => r.State!.Characters[0].SkillTrees[0].Kind = 0,
            r => r.State!.Weapons[0].WeaponId = "wrong",
            r => r.State!.Shards[offer.ShardId] = 8,
            r => r.Results[1].IsDuplicate = false,
        })
        {
            var invalid = Clone(receipt);
            corrupt(invalid);
            bool rejected = false;
            try { ValidateDraw(before, invalid, id, 10); }
            catch (FixtureAssertion) { rejected = true; }
            Require(rejected, "corrupt draw receipt rejected");
        }
        Console.WriteLine("Shop receipt/skill/weapon/same-batch shard self-tests passed; no database or HTTP access.");
    }
}
