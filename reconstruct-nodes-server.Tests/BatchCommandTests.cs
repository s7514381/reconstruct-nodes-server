using Dapper;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>
/// 批次命令的離線測試：只驗證讀取後的 Prepare（重播、衝突、revision、業務錯誤與寫入批次內容），
/// 不連資料庫；回滾與並行行為需要真實 SQL Server，不在此涵蓋。
/// </summary>
[TestClass]
public class BatchCommandTests
{
    private static readonly Guid Player = Guid.NewGuid();

    private static string Result(IReadOnlyList<SqlBatchCommand> commands)
    {
        var parameters = (DynamicParameters)commands[^1].Parameters!;
        return parameters.Get<string>(parameters.ParameterNames.Single(name => name.StartsWith("Result")));
    }

    private static CommandReceipt<T> Receipt<T>(IReadOnlyList<SqlBatchCommand> commands) =>
        System.Text.Json.JsonSerializer.Deserialize<CommandReceipt<T>>(Result(commands))!;

    private static string AllSql(IReadOnlyList<SqlBatchCommand> commands) => string.Join("\n", commands.Select(c => c.Sql));

    private static PlayerParams Revision(string name, long value) =>
        new() { Group = RevisionCommand.RevisionGroup, Name = name, Value = value.ToString() };

    private static void AssertKey(string key, Action action) =>
        Assert.AreEqual(key, Assert.ThrowsExactly<BusinessArgumentException>(action).ApiMessage.Key);

    // ---- Weapon/Disassemble ----

    private static WeaponInstanceModel Weapon(string id) => new() { Id = id, WeaponId = "sword" };

    private static (WeaponDisassembleRequest Request, string Payload, WeaponCommand.State State) Disassemble(params string[] ids)
    {
        var request = new WeaponDisassembleRequest { RequestId = Guid.NewGuid(), WeaponEntryIds = ids.Order(StringComparer.Ordinal).ToList() };
        var state = new WeaponCommand.State
        {
            Revisions = [Revision(nameof(WeaponInstanceModel), 4)],
            Weapons = [Weapon("a"), Weapon("b"), Weapon("c")],
            Equipped = [new Character { WeaponEntryKey = "c" }],
        };
        return (request, RevisionCommand.CanonicalPayload(request), state);
    }

    [TestMethod]
    public void Disassemble_removes_skips_and_advances_domain_and_scope_revisions()
    {
        var (request, payload, state) = Disassemble("a", "c", "missing");
        var commands = WeaponCommand.PrepareDisassemble(Player, request, payload, state);

        var receipt = Receipt<WeaponDisassembleSnapshot>(commands);
        CollectionAssert.AreEqual(new[] { "a" }, receipt.Snapshot.RemovedIds);
        CollectionAssert.AreEqual(new[] { "c", "missing" }, receipt.Snapshot.SkippedIds);
        CollectionAssert.AreEqual(new[] { "b", "c" }, receipt.Snapshot.Weapons.Select(w => w.Id).ToArray());
        Assert.AreEqual(5, receipt.Revision);
        Assert.IsTrue(receipt.Snapshot.Weapons.All(w => w.Revision == 5));
        var sql = AllSql(commands);
        StringAssert.Contains(sql, "UPDATE Weapon SET Status = -1");
        StringAssert.Contains(sql, "INSERT INTO [EconomyRequest]");
        // 領域列已存在（更新），範圍列不存在（新增）。
        StringAssert.Contains(sql, "UPDATE t SET");
        StringAssert.Contains(sql, "INSERT INTO [PlayerParams]");
    }

    [TestMethod]
    public void Disassemble_without_removals_still_advances_revision_but_deletes_nothing()
    {
        var (request, payload, state) = Disassemble("c");
        var commands = WeaponCommand.PrepareDisassemble(Player, request, payload, state);
        Assert.AreEqual(5, Receipt<WeaponDisassembleSnapshot>(commands).Revision);
        Assert.IsFalse(AllSql(commands).Contains("Status = -1"));
    }

    [TestMethod]
    public void Disassemble_replays_same_intent_and_rejects_different_intent()
    {
        var (request, payload, state) = Disassemble("a");
        state.Receipts = [new EconomyRequest { RequestId = request.RequestId, Operation = "weapon.disassemble", Payload = payload, Result = "{\"original\":1}" }];
        var replay = WeaponCommand.PrepareDisassemble(Player, request, payload, state);
        Assert.AreEqual(1, replay.Count);
        Assert.AreEqual("SELECT @Receipt;", replay[0].Sql);

        AssertKey(ApiMessageKeys.EconomyRequestConflict,
            () => WeaponCommand.PrepareDisassemble(Player, request, RevisionCommand.CanonicalPayload(Disassemble("b").Request), state));
    }

    // ---- Character/EquipWeapon ----

    private static (WeaponEquipRequest Request, string Payload, EquipWeaponCommand.State State) Equip(string? weapon, Guid? other = null)
    {
        var character = new Character { Kind = CharacterKind.Warrior };
        var request = new WeaponEquipRequest { RequestId = Guid.NewGuid(), CharacterId = character.Id, WeaponEntryId = weapon };
        var state = new EquipWeaponCommand.State
        {
            Characters = [character],
            Revisions = [Revision(nameof(CharacterModel), 7)],
            Weapons = weapon == null ? [] : [new Weapon { EntryKey = weapon, WeaponKey = "sword", Rarity = "common" }],
        };
        if (other != null) { state.Characters.Add(new Character { Kind = CharacterKind.Archer, WeaponEntryKey = weapon! }); }
        return (request, RevisionCommand.CanonicalPayload(request), state);
    }

    [TestMethod]
    public void Equip_updates_only_equipment_and_returns_snapshot_with_new_revision()
    {
        var (request, payload, state) = Equip("w1");
        var commands = EquipWeaponCommand.Prepare(Player, request, payload, state);
        var receipt = Receipt<CharacterModel>(commands);
        Assert.AreEqual(8, receipt.Revision);
        Assert.AreEqual(8, receipt.Snapshot.Revision);
        Assert.AreEqual("w1", receipt.Snapshot.WeaponEntryId);
        Assert.AreEqual(2, receipt.Snapshot.WeaponQualityVersion);
        var sql = AllSql(commands);
        StringAssert.Contains(sql, "SET Rarity = Rarity");
        StringAssert.Contains(sql, "UPDATE Character SET WeaponEntryKey");
        Assert.IsFalse(sql.Contains("CharacterSkillTree"));
    }

    [TestMethod]
    public void Equip_null_unequips_without_touching_weapon_rows()
    {
        var (request, payload, state) = Equip(null);
        state.Characters[0].WeaponEntryKey = "old";
        var commands = EquipWeaponCommand.Prepare(Player, request, payload, state);
        Assert.IsTrue(string.IsNullOrEmpty(Receipt<CharacterModel>(commands).Snapshot.WeaponEntryId));
        Assert.IsFalse(AllSql(commands).Contains("Rarity = Rarity"));
    }

    [TestMethod]
    public void Equip_rejects_missing_character_unowned_weapon_and_weapon_equipped_elsewhere()
    {
        var (request, payload, state) = Equip("w1");
        state.Characters.Clear();
        AssertKey(ApiMessageKeys.CharacterNotOwned, () => EquipWeaponCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Equip("w1");
        state.Weapons.Clear();
        AssertKey(ApiMessageKeys.WeaponNotOwned, () => EquipWeaponCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Equip("w1", other: Guid.NewGuid());
        AssertKey(ApiMessageKeys.WeaponAlreadyEquipped, () => EquipWeaponCommand.Prepare(Player, request, payload, state));
    }

    [TestMethod]
    public void Equip_replays_and_detects_conflict()
    {
        var (request, payload, state) = Equip("w1");
        state.Receipts = [new EconomyRequest { RequestId = request.RequestId, Operation = "character.equip_weapon", Payload = payload, Result = "{}" }];
        Assert.AreEqual("SELECT @Receipt;", EquipWeaponCommand.Prepare(Player, request, payload, state).Single().Sql);
        AssertKey(ApiMessageKeys.EconomyRequestConflict, () => EquipWeaponCommand.Prepare(Player, request, payload + " ", state));
    }

    // ---- Character/ConfigureAttributes ----

    private static (CharacterAttributesRequest Request, string Payload, CharacterAttributesCommand.State State) Attributes(
        int level, int strength, int intelligence, int dexterity, int otherAllocated = 0)
    {
        var character = new Character { Kind = CharacterKind.Warrior };
        var other = new Character { Kind = CharacterKind.Archer, Strength = otherAllocated };
        var request = new CharacterAttributesRequest
        {
            RequestId = Guid.NewGuid(), CharacterId = character.Id,
            Strength = strength, Intelligence = intelligence, Dexterity = dexterity,
        };
        var state = new CharacterAttributesCommand.State
        {
            Characters = [character, other],
            Revisions = [Revision(nameof(CharacterModel), 7)],
            Progression = [new PlayerParams { Group = "game_progress.progression", Name = "level", Value = level.ToString() }],
        };
        return (request, RevisionCommand.CanonicalPayload(request), state);
    }

    [TestMethod]
    public void Attributes_overwrite_one_character_and_return_snapshot_with_new_revision()
    {
        var (request, payload, state) = Attributes(level: 11, strength: 20, intelligence: 10, dexterity: 30);
        var commands = CharacterAttributesCommand.Prepare(Player, request, payload, state);
        var receipt = Receipt<CharacterModel>(commands);
        Assert.AreEqual(8, receipt.Revision);
        Assert.AreEqual((20, 10, 30), (receipt.Snapshot.Strength, receipt.Snapshot.Intelligence, receipt.Snapshot.Dexterity));
        StringAssert.Contains(AllSql(commands), "UPDATE Character SET Strength");
    }

    [TestMethod]
    public void Attributes_rejects_negative_and_over_pool_including_other_characters()
    {
        // 等級 11 = 66 點；其他角色已用 10 點，本角色最多 56。
        var (request, payload, state) = Attributes(11, 56, 0, 0, otherAllocated: 10);
        Assert.AreEqual(8, Receipt<CharacterModel>(CharacterAttributesCommand.Prepare(Player, request, payload, state)).Revision);

        (request, payload, state) = Attributes(11, 57, 0, 0, otherAllocated: 10);
        AssertKey(ApiMessageKeys.CharacterAttributesExceeded, () => CharacterAttributesCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Attributes(11, -1, 5, 5);
        AssertKey(ApiMessageKeys.CharacterAttributesInvalid, () => CharacterAttributesCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Attributes(1, 7, 0, 0);
        AssertKey(ApiMessageKeys.CharacterAttributesExceeded, () => CharacterAttributesCommand.Prepare(Player, request, payload, state));
    }

    [TestMethod]
    public void Attributes_rejects_unowned_character_and_replays_receipt()
    {
        var (request, payload, state) = Attributes(11, 1, 1, 1);
        state.Characters.Clear();
        AssertKey(ApiMessageKeys.CharacterNotOwned, () => CharacterAttributesCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Attributes(11, 1, 1, 1);
        state.Receipts = [new EconomyRequest { RequestId = request.RequestId, Operation = "character.configure_attributes", Payload = payload, Result = "{}" }];
        Assert.AreEqual("SELECT @Receipt;", CharacterAttributesCommand.Prepare(Player, request, payload, state).Single().Sql);
        AssertKey(ApiMessageKeys.EconomyRequestConflict, () => CharacterAttributesCommand.Prepare(Player, request, payload + " ", state));
    }

    // ---- Character/UpgradeSkillLinks ----

    private static string Named(DynamicParameters parameters, string prefix) =>
        parameters.ParameterNames.Single(name => name.StartsWith(prefix));

    private static (SkillLinkUpgradeRequest Request, string Payload, SkillLinkUpgradeCommand.State State) LinkUpgrade(int linkCount)
    {
        var character = new Character { Kind = 0, SkillLinkCount = linkCount };
        var request = new SkillLinkUpgradeRequest { RequestId = Guid.NewGuid(), CharacterId = character.Id };
        var state = new SkillLinkUpgradeCommand.State
        {
            Characters = [character],
            Revisions = [Revision(nameof(CharacterModel), 7)],
        };
        return (request, RevisionCommand.CanonicalPayload(request), state);
    }

    [TestMethod]
    [DataRow(2, 3, 1)]
    [DataRow(3, 4, 5)]
    [DataRow(4, 5, 25)]
    [DataRow(5, 6, 100)]
    public void SkillLinks_upgrade_one_level_and_spend_the_listed_extractor_cost(int from, int to, int cost)
    {
        var (request, payload, state) = LinkUpgrade(from);
        var commands = SkillLinkUpgradeCommand.Prepare(Player, request, payload, state);
        // 回執由資料庫回填 material_count，離線只驗證傳入 SQL 的回執草稿。
        var parameters = (DynamicParameters)commands[^1].Parameters!;
        var receipt = System.Text.Json.JsonSerializer.Deserialize<CommandReceipt<SkillLinkUpgradeSnapshot>>(
            parameters.Get<string>(Named(parameters, "Receipt")))!;
        Assert.AreEqual(8, receipt.Revision);
        Assert.AreEqual(to, receipt.Snapshot.Character.SkillLinkCount);
        Assert.AreEqual(SkillLinkRules.UpgradeItemKey, parameters.Get<string>(Named(parameters, "ItemKey")));
        Assert.AreEqual(-cost, parameters.Get<int>(Named(parameters, "Delta")));
        StringAssert.Contains(AllSql(commands), "SET SkillLinkCount");
    }

    [TestMethod]
    public void SkillLinks_reject_upgrade_at_max_unowned_character_and_replay_receipt()
    {
        var (request, payload, state) = LinkUpgrade(SkillLinkRules.MaxCount);
        AssertKey(ApiMessageKeys.CharacterSkillLinkMax, () => SkillLinkUpgradeCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = LinkUpgrade(2);
        state.Characters.Clear();
        AssertKey(ApiMessageKeys.CharacterNotOwned, () => SkillLinkUpgradeCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = LinkUpgrade(2);
        state.Receipts = [new EconomyRequest { RequestId = request.RequestId, Operation = "character.upgrade_skill_links", Payload = payload, Result = "{}" }];
        Assert.AreEqual("SELECT @Receipt;", SkillLinkUpgradeCommand.Prepare(Player, request, payload, state).Single().Sql);
        AssertKey(ApiMessageKeys.EconomyRequestConflict, () => SkillLinkUpgradeCommand.Prepare(Player, request, payload + " ", state));
    }

    // ---- Character/Dissolve ----

    private static (CharacterDissolveRequest Request, string Payload, CharacterDissolveCommand.State State) Dissolve(
        int level, CharacterKind kind = CharacterKind.Warrior, string weapon = "sword#1", bool deployed = false)
    {
        var character = new Character { Kind = kind, Level = level, WeaponEntryKey = weapon };
        var request = new CharacterDissolveRequest { RequestId = Guid.NewGuid(), CharacterId = character.Id };
        var state = new CharacterDissolveCommand.State
        {
            Characters = [character],
            Revisions = [Revision(nameof(CharacterModel), 7)],
            Deployments = deployed ? [new Deployment { CharacterId = character.Id }] : [],
        };
        return (request, RevisionCommand.CanonicalPayload(request), state);
    }

    // 回執由資料庫回填 honor_count，離線只驗證傳入 SQL 的回執草稿。
    private static CommandReceipt<CharacterDissolveSnapshot> DissolveDraft(IReadOnlyList<SqlBatchCommand> commands)
    {
        var parameters = (DynamicParameters)commands[^1].Parameters!;
        return System.Text.Json.JsonSerializer.Deserialize<CommandReceipt<CharacterDissolveSnapshot>>(
            parameters.Get<string>(Named(parameters, "Receipt")))!;
    }

    [TestMethod]
    [DataRow(1, 10)]
    [DataRow(2, 20)]
    [DataRow(10, 100)]
    public void Dissolve_grants_honor_by_level_returns_the_weapon_and_removes_the_character(int level, int honor)
    {
        var (request, payload, state) = Dissolve(level);
        var commands = CharacterDissolveCommand.Prepare(Player, request, payload, state);

        var receipt = DissolveDraft(commands);
        Assert.AreEqual(8, receipt.Revision);
        Assert.AreEqual(request.CharacterId, receipt.Snapshot.CharacterId);
        Assert.AreEqual(level, receipt.Snapshot.Level);
        Assert.AreEqual(honor, receipt.Snapshot.HonorGained);
        Assert.AreEqual("sword#1", receipt.Snapshot.ReleasedWeaponEntryId);
        var parameters = (DynamicParameters)commands[^1].Parameters!;
        Assert.AreEqual(CharacterHonorRules.ItemKey, parameters.Get<string>(Named(parameters, "ItemKey")));
        Assert.AreEqual(honor, parameters.Get<int>(Named(parameters, "Delta")));
        string sql = AllSql(commands);
        StringAssert.Contains(sql, "WeaponEntryKey = NULL");
        StringAssert.Contains(sql, "SET Status = -1");
        StringAssert.Contains(sql, "INSERT INTO EconomyRequest");
    }

    [TestMethod]
    public void Dissolve_reports_no_released_weapon_when_the_character_was_unarmed()
    {
        var (request, payload, state) = Dissolve(3, weapon: "");
        Assert.IsNull(DissolveDraft(CharacterDissolveCommand.Prepare(Player, request, payload, state))
            .Snapshot.ReleasedWeaponEntryId);
    }

    [TestMethod]
    public void Dissolve_rejects_core_deployed_and_unowned_characters_and_replays_the_receipt()
    {
        var (request, payload, state) = Dissolve(1, CharacterKind.Core);
        AssertKey(ApiMessageKeys.CharacterDissolveCore, () => CharacterDissolveCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Dissolve(1, deployed: true);
        AssertKey(ApiMessageKeys.CharacterDissolveDeployed, () => CharacterDissolveCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Dissolve(1);
        state.Characters[0].Locked = true;
        AssertKey(ApiMessageKeys.CharacterLocked, () => CharacterDissolveCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Dissolve(1);
        state.Characters.Clear();
        AssertKey(ApiMessageKeys.CharacterNotOwned, () => CharacterDissolveCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Dissolve(1);
        state.Receipts = [new EconomyRequest { RequestId = request.RequestId, Operation = "character.dissolve", Payload = payload, Result = "{}" }];
        Assert.AreEqual("SELECT @Receipt;", CharacterDissolveCommand.Prepare(Player, request, payload, state).Single().Sql);
        AssertKey(ApiMessageKeys.EconomyRequestConflict, () => CharacterDissolveCommand.Prepare(Player, request, payload + " ", state));
    }

    // ---- Character/SetLocked ----

    private static (CharacterLockRequest Request, string Payload, CharacterLockCommand.State State) Lock(
        bool locked, CharacterKind kind = CharacterKind.Warrior)
    {
        var character = new Character { Kind = kind };
        var request = new CharacterLockRequest { RequestId = Guid.NewGuid(), CharacterId = character.Id, Locked = locked };
        var state = new CharacterLockCommand.State
        {
            Characters = [character],
            Revisions = [Revision(nameof(CharacterModel), 7)],
        };
        return (request, RevisionCommand.CanonicalPayload(request), state);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Lock_overwrites_the_flag_and_advances_the_revision(bool locked)
    {
        var (request, payload, state) = Lock(locked);
        var commands = CharacterLockCommand.Prepare(Player, request, payload, state);

        var receipt = Receipt<CharacterLockSnapshot>(commands);
        Assert.AreEqual(8, receipt.Revision);
        Assert.AreEqual(request.CharacterId, receipt.Snapshot.CharacterId);
        Assert.AreEqual(locked, receipt.Snapshot.Locked);
        StringAssert.Contains(AllSql(commands), "SET Locked = @Locked");
    }

    [TestMethod]
    public void Lock_rejects_core_and_unowned_characters_and_replays_the_receipt()
    {
        var (request, payload, state) = Lock(true, CharacterKind.Core);
        AssertKey(ApiMessageKeys.CharacterLockCore, () => CharacterLockCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Lock(true);
        state.Characters.Clear();
        AssertKey(ApiMessageKeys.CharacterNotOwned, () => CharacterLockCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Lock(true);
        state.Receipts = [new EconomyRequest { RequestId = request.RequestId, Operation = "character.set_locked", Payload = payload, Result = "{}" }];
        Assert.AreEqual("SELECT @Receipt;", CharacterLockCommand.Prepare(Player, request, payload, state).Single().Sql);
        AssertKey(ApiMessageKeys.EconomyRequestConflict, () => CharacterLockCommand.Prepare(Player, request, payload + " ", state));
    }

    // ---- Character/Upgrade ----

    private static (CharacterUpgradeRequest Request, string Payload, CharacterUpgradeCommand.State State) Upgrade(
        int level, int materialCount, Action<Character, List<Character>>? tweak = null)
    {
        var target = new Character { Kind = CharacterKind.Warrior, Level = level };
        var materials = Enumerable.Range(0, materialCount)
            .Select(i => new Character { Kind = CharacterKind.Archer, Level = 1 + i % 3, WeaponEntryKey = i == 0 ? "bow#1" : null }).ToList();
        tweak?.Invoke(target, materials);
        var request = new CharacterUpgradeRequest
        {
            RequestId = Guid.NewGuid(), TargetId = target.Id, MaterialIds = materials.Select(m => m.Id).ToList(),
        };
        var state = new CharacterUpgradeCommand.State
        {
            Characters = [target, .. materials],
            Revisions = [Revision(nameof(CharacterModel), 7)],
        };
        return (request, RevisionCommand.CanonicalPayload(request), state);
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(2, 2)]
    [DataRow(3, 4)]
    [DataRow(9, 256)]
    public void Upgrade_costs_two_to_the_level_minus_one_materials_and_returns_their_weapons(int level, int cost)
    {
        Assert.AreEqual(cost, CharacterLevelRules.UpgradeMaterialCount(level));
        var (request, payload, state) = Upgrade(level, cost);
        var commands = CharacterUpgradeCommand.Prepare(Player, request, payload, state);

        var receipt = Receipt<CharacterUpgradeSnapshot>(commands);
        Assert.AreEqual(8, receipt.Revision);
        Assert.AreEqual(level + 1, receipt.Snapshot.Level);
        Assert.AreEqual(request.TargetId, receipt.Snapshot.TargetId);
        CollectionAssert.AreEquivalent(request.MaterialIds, receipt.Snapshot.RemovedIds);
        CollectionAssert.AreEqual(new[] { "bow#1" }, receipt.Snapshot.ReleasedWeaponEntryIds);
        string sql = AllSql(commands);
        StringAssert.Contains(sql, "SET Status = -1");
        StringAssert.Contains(sql, "SET Level = @Level");
        Assert.IsFalse(sql.Contains("honor", StringComparison.OrdinalIgnoreCase), "素材不給榮譽");
    }

    [TestMethod]
    [DataRow(1, 3, 3)]
    [DataRow(2, 3, 3)]
    [DataRow(3, 5, 4)]
    [DataRow(3, 12, 5)]
    [DataRow(3, 11, 4)]
    public void Upgrade_with_extra_materials_chains_levels_and_consumes_all(int level, int count, int expectedLevel)
    {
        Assert.AreEqual(expectedLevel, CharacterLevelRules.LevelAfterMaterials(level, count));
        var (request, payload, state) = Upgrade(level, count);
        var receipt = Receipt<CharacterUpgradeSnapshot>(CharacterUpgradeCommand.Prepare(Player, request, payload, state));
        Assert.AreEqual(expectedLevel, receipt.Snapshot.Level);
        Assert.AreEqual(count, receipt.Snapshot.RemovedIds.Count);
    }

    [TestMethod]
    public void Upgrade_rejects_wrong_counts_duplicates_target_core_deployed_unowned_and_max_level()
    {
        var (request, payload, state) = Upgrade(3, 3);
        AssertKey(ApiMessageKeys.CharacterUpgradeMaterialInvalid, () => CharacterUpgradeCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Upgrade(2, 2);
        request.MaterialIds[1] = request.MaterialIds[0];
        AssertKey(ApiMessageKeys.CharacterUpgradeMaterialInvalid, () => CharacterUpgradeCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Upgrade(2, 2);
        request.MaterialIds[1] = request.TargetId;
        AssertKey(ApiMessageKeys.CharacterUpgradeMaterialInvalid, () => CharacterUpgradeCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Upgrade(2, 2, (_, materials) => materials[0].Kind = CharacterKind.Core);
        AssertKey(ApiMessageKeys.CharacterUpgradeMaterialInvalid, () => CharacterUpgradeCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Upgrade(2, 2, (target, _) => target.Kind = CharacterKind.Core);
        AssertKey(ApiMessageKeys.CharacterUpgradeMaterialInvalid, () => CharacterUpgradeCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Upgrade(2, 2);
        state.Deployments = [new Deployment { CharacterId = request.MaterialIds[0] }];
        AssertKey(ApiMessageKeys.CharacterDissolveDeployed, () => CharacterUpgradeCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Upgrade(2, 2, (_, materials) => materials[1].Locked = true);
        AssertKey(ApiMessageKeys.CharacterLocked, () => CharacterUpgradeCommand.Prepare(Player, request, payload, state));

        // 鎖定的角色仍可當升級目標。
        (request, payload, state) = Upgrade(2, 2, (target, _) => target.Locked = true);
        Assert.AreEqual(3, Receipt<CharacterUpgradeSnapshot>(CharacterUpgradeCommand.Prepare(Player, request, payload, state)).Snapshot.Level);

        (request, payload, state) = Upgrade(2, 2);
        state.Characters.RemoveAt(1);
        AssertKey(ApiMessageKeys.CharacterNotOwned, () => CharacterUpgradeCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Upgrade(CharacterLevelRules.MaxLevel, 0);
        Assert.AreEqual(0, CharacterLevelRules.UpgradeMaterialCount(CharacterLevelRules.MaxLevel));
        AssertKey(ApiMessageKeys.CharacterUpgradeMax, () => CharacterUpgradeCommand.Prepare(Player, request, payload, state));
    }

    [TestMethod]
    public void Upgrade_replays_the_receipt_and_rejects_a_changed_intent()
    {
        var (request, payload, state) = Upgrade(2, 2);
        state.Receipts = [new EconomyRequest { RequestId = request.RequestId, Operation = "character.upgrade", Payload = payload, Result = "{}" }];
        Assert.AreEqual("SELECT @Receipt;", CharacterUpgradeCommand.Prepare(Player, request, payload, state).Single().Sql);
        AssertKey(ApiMessageKeys.EconomyRequestConflict, () => CharacterUpgradeCommand.Prepare(Player, request, payload + " ", state));
    }

    [TestMethod]
    public void Honor_reward_is_ten_per_level_and_rejects_invalid_levels()
    {
        Assert.AreEqual(10, CharacterHonorRules.DissolveReward(1));
        Assert.AreEqual(70, CharacterHonorRules.DissolveReward(7));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CharacterHonorRules.DissolveReward(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CharacterHonorRules.DissolveReward(CharacterLevelRules.MaxLevel + 1));
    }

    [TestMethod]
    public void SkillLoadoutRules_limit_chain_nodes_by_character_link_count()
    {
        static List<GameProgressSkillLoadoutModel> Chain(int nodes) =>
        [
            new() { Kind = 0, AuxiliaryNodes = Enumerable.Range(0, nodes - 1).Select(i => new GameProgressSkillNodeModel
                { Type = GameProgressSkillNodeModel.AuxiliaryType, LootId = $"support_{i}" }).ToList() },
        ];
        SkillLoadoutRules.Validate(Chain(2), 2);
        Assert.AreEqual("skill.loadout.chain_limit",
            Assert.ThrowsExactly<BusinessArgumentException>(() => SkillLoadoutRules.Validate(Chain(3), 2)).ApiMessage.Key);
        SkillLoadoutRules.Validate(Chain(3), 3);
    }

    [TestMethod]
    public void SkillLoadoutRules_keep_cleared_slots_and_orphaned_triggered_skills()
    {
        static GameProgressSkillNodeModel Node(string type, string? lootId = null, int? kind = null) =>
            new() { Type = type, LootId = lootId!, Kind = kind };
        // 空格保留位置、不佔技能點；失去觸發來源的火球不阻擋儲存。
        SkillLoadoutRules.Validate([new() { Kind = 0, AuxiliaryNodes = [
            Node(GameProgressSkillNodeModel.EmptyType),
            Node(GameProgressSkillNodeModel.ActiveType, kind: 1),
        ] }], 3);
        // 空格仍佔連線位置。
        Assert.AreEqual("skill.loadout.chain_limit",
            Assert.ThrowsExactly<BusinessArgumentException>(() => SkillLoadoutRules.Validate([new() { Kind = 0, AuxiliaryNodes = [
                Node(GameProgressSkillNodeModel.EmptyType),
                Node(GameProgressSkillNodeModel.AuxiliaryType, "multi_strike_gem"),
            ] }], 2)).ApiMessage.Key);
    }

    // ---- GameProgress/SetDeployment、SetBattlefield、TransitionWave ----

    private static GameProgressCommand.ProgressRows Progress(long gold = 1000, long revision = 3, int cleared = 5)
    {
        PlayerParams P(string group, string name, string value, int sequence = 0) =>
            new() { Group = group, Name = name, Value = value, Sequence = sequence };
        return new GameProgressCommand.ProgressRows
        {
            Parameters =
            [
                P("game_progress", "version", "8"), P("game_progress", "wave", "2"),
                P("game_progress", "gold", gold.ToString()), P("game_progress", "wave_completed", "True"),
                P("game_progress.cleared_waves", "0", cleared.ToString()), P("game_progress.cleared_waves", "1", "0", 1),
                P("game_progress.cleared_waves", "2", "0", 2), P("game_progress.cleared_waves", "3", "0", 3),
                P("game_progress.unlocked_bullet_kinds", "2", "true"),
                Revision(nameof(GameProgressModel), revision),
            ],
        };
    }

    private static DeploymentCommandRequest Deploy(Guid character, long expected = 3) => new()
    {
        RequestId = Guid.NewGuid(), ExpectedRevision = expected,
        DeploymentLog = [new GameProgressDeploymentModel { CharacterId = character, SpotIndex = 1, Cell = [0, 0] }],
    };

    private static BattlefieldCommand.State BattleState(Guid character, long gold = 1000, long revision = 3) =>
        new() { Progress = Progress(gold, revision), Characters = [new Character { Id = character, Kind = CharacterKind.Warrior }] };

    private static string Payload(object request) => System.Text.Json.JsonSerializer.Serialize(request, request.GetType());

    [TestMethod]
    public void SetDeployment_keeps_gold_and_advances_revision_once()
    {
        Guid character = Guid.NewGuid();
        var request = Deploy(character);
        var commands = BattlefieldCommand.Prepare(Player, request, Payload(request), BattleState(character),
            BattlefieldCommand.DeploymentPlan(request));
        var receipt = Receipt<GameProgressModel>(commands);
        Assert.AreEqual(4, receipt.Revision);
        Assert.AreEqual(4, receipt.Snapshot.Revision);
        Assert.AreEqual(1000, receipt.Snapshot.Gold);
        Assert.AreEqual(1, receipt.Snapshot.DeploymentLog.Count);
        Assert.AreEqual(2, receipt.Snapshot.Wave);
        Assert.IsTrue(receipt.Snapshot.UnlockedBulletKinds.ContainsKey("2"));
        StringAssert.Contains(AllSql(commands), "INSERT INTO PlayerParams");
        Assert.IsFalse(AllSql(commands).Contains("wave_start"));
    }

    [TestMethod]
    public void SetDeployment_checks_revision_and_character_ownership_without_charging_gold()
    {
        Guid character = Guid.NewGuid();
        var request = Deploy(character, expected: 2);
        var conflict = Assert.ThrowsExactly<BusinessOperationException>(() => BattlefieldCommand.Prepare(
            Player, request, Payload(request), BattleState(character), BattlefieldCommand.DeploymentPlan(request)));
        Assert.AreEqual("concurrency.revision_conflict", conflict.ApiMessage.Key);

        request = Deploy(character);
        var free = BattlefieldCommand.Prepare(
            Player, request, Payload(request), BattleState(character, gold: 0), BattlefieldCommand.DeploymentPlan(request));
        Assert.IsFalse(AllSql(free).Contains("EconomyInsufficientGold"));

        AssertKey(ApiMessageKeys.ProgressCharacterNotOwned, () => BattlefieldCommand.Prepare(
            Player, request, Payload(request), BattleState(Guid.NewGuid()), BattlefieldCommand.DeploymentPlan(request)));
    }

    [TestMethod]
    public void BattlefieldSpace_capacity_starts_at_20_grows_one_per_level_and_caps_at_60()
    {
        Assert.AreEqual(20, BattlefieldSpaceRules.Capacity(1));
        Assert.AreEqual(21, BattlefieldSpaceRules.Capacity(2));
        Assert.AreEqual(59, BattlefieldSpaceRules.Capacity(40));
        Assert.AreEqual(60, BattlefieldSpaceRules.Capacity(41));
        Assert.AreEqual(60, BattlefieldSpaceRules.Capacity(100));
        Assert.AreEqual(10, BattlefieldSpaceRules.CostOf(CharacterKind.Warrior));
    }

    [TestMethod]
    public void SetDeployment_rejects_characters_that_exceed_the_battlefield_space_of_the_player_level()
    {
        Character[] characters = Enumerable.Range(0, 3)
            .Select(_ => new Character { Id = Guid.NewGuid(), Kind = CharacterKind.Archer }).ToArray();
        DeploymentCommandRequest Request(int count) => new()
        {
            RequestId = Guid.NewGuid(), ExpectedRevision = 3,
            DeploymentLog = characters.Take(count).Select((row, index) =>
                new GameProgressDeploymentModel { CharacterId = row.Id, SpotIndex = index + 1, Cell = [index, 0] }).ToList(),
        };
        BattlefieldCommand.State StateAt(int level)
        {
            var state = new BattlefieldCommand.State { Progress = Progress(), Characters = [.. characters] };
            state.Progress.Parameters.Add(new PlayerParams { Group = "game_progress.progression", Name = "level", Value = level.ToString() });
            return state;
        }

        var two = Request(2);
        BattlefieldCommand.Prepare(Player, two, Payload(two), StateAt(1), BattlefieldCommand.DeploymentPlan(two));
        var three = Request(3);
        AssertKey("battlefield.space_insufficient", () => BattlefieldCommand.Prepare(
            Player, three, Payload(three), StateAt(1), BattlefieldCommand.DeploymentPlan(three)));
        three = Request(3);
        BattlefieldCommand.Prepare(Player, three, Payload(three), StateAt(11), BattlefieldCommand.DeploymentPlan(three));
    }

    [TestMethod]
    public void SetDeployment_replays_and_conflicts_before_revision_check()
    {
        Guid character = Guid.NewGuid();
        var request = Deploy(character, expected: 99);
        var state = BattleState(character);
        state.Receipts = [new EconomyRequest { RequestId = request.RequestId, Operation = "foreground.deployment", Payload = Payload(request), Result = "{}" }];
        Assert.AreEqual("SELECT @Receipt;", BattlefieldCommand.Prepare(
            Player, request, Payload(request), state, BattlefieldCommand.DeploymentPlan(request)).Single().Sql);
        AssertKey(ApiMessageKeys.EconomyRequestConflict, () => BattlefieldCommand.Prepare(
            Player, request, Payload(request) + " ", state, BattlefieldCommand.DeploymentPlan(request)));
    }

    [TestMethod]
    public void Captain_rules_require_a_deployed_captain_whenever_anyone_is_deployed()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        List<GameProgressDeploymentModel> one = [new() { CharacterId = first, SpotIndex = 1, Cell = [0, 0] }];
        BattlefieldRules.ValidateCaptain(null, []);
        BattlefieldRules.ValidateCaptain(first, one);
        foreach ((Guid? captain, List<GameProgressDeploymentModel> deployments) in new (Guid?, List<GameProgressDeploymentModel>)[]
            { (null, one), (second, one), (first, []) })
        {
            Assert.AreEqual("battlefield.captain_invalid",
                Assert.ThrowsExactly<BusinessArgumentException>(() => BattlefieldRules.ValidateCaptain(captain, deployments)).ApiMessage.Key);
        }
    }

    [TestMethod]
    public void Captain_is_persisted_as_a_progress_scalar_and_read_back()
    {
        Guid captain = Guid.NewGuid();
        var model = new GameProgressModel
        {
            Version = 8, CaptainCharacterId = captain,
            DeploymentLog = [new() { CharacterId = captain, SpotIndex = 1, Cell = [0, 0] }],
        };
        var rows = GameProgressCommand.BuildRows(model);
        var restored = GameProgressCommand.BuildSnapshot(rows.Parameters, rows.Deployments, rows.Roads, 1, 0);
        Assert.AreEqual(captain, restored!.CaptainCharacterId);

        model.CaptainCharacterId = null;
        rows = GameProgressCommand.BuildRows(model);
        Assert.IsNull(GameProgressCommand.BuildSnapshot(rows.Parameters, rows.Deployments, rows.Roads, 1, 0)!.CaptainCharacterId);
    }

    [TestMethod]
    public void Captain_must_belong_to_the_saved_deployment()
    {
        Guid captain = Guid.NewGuid();
        var model = new GameProgressModel
        {
            Version = 8, CaptainCharacterId = captain,
            DeploymentLog = [new() { CharacterId = Guid.NewGuid(), SpotIndex = 1, Cell = [0, 0] }],
        };
        Assert.AreEqual("battlefield.captain_invalid",
            Assert.ThrowsExactly<BusinessArgumentException>(() => GameProgressCommand.ValidateProgress(model)).ApiMessage.Key);
    }

    [TestMethod]
    public void SetDeployment_clears_a_captain_that_is_no_longer_deployed_and_keeps_one_that_is()
    {
        Guid captain = Guid.NewGuid(), other = Guid.NewGuid();
        BattlefieldCommand.State StateWith(Guid captainId)
        {
            var state = BattleState(other);
            state.Progress.Parameters.Add(new PlayerParams { Group = "game_progress", Name = "captain_character_id", Value = captainId.ToString("D") });
            return state;
        }
        var request = Deploy(other);
        var cleared = Receipt<GameProgressModel>(BattlefieldCommand.Prepare(Player, request, Payload(request), StateWith(captain),
            BattlefieldCommand.DeploymentPlan(request)));
        Assert.IsNull(cleared.Snapshot.CaptainCharacterId);

        request = Deploy(other);
        var kept = Receipt<GameProgressModel>(BattlefieldCommand.Prepare(Player, request, Payload(request), StateWith(other),
            BattlefieldCommand.DeploymentPlan(request)));
        Assert.AreEqual(other, kept.Snapshot.CaptainCharacterId);
    }

    [TestMethod]
    public void TransitionWave_starts_session_with_child_receipt_and_active_wave()
    {
        var request = new WaveTransitionRequest { RequestId = Guid.NewGuid(), ExpectedRevision = 3, Wave = 3, Difficulty = 0 };
        var commands = BattlefieldCommand.Prepare(Player, request, Payload(request), BattleState(Guid.NewGuid()),
            BattlefieldCommand.TransitionPlan(request));
        var receipt = Receipt<WaveTransitionSnapshot>(commands);
        Guid session = BattlefieldCommand.ChildRequestId(request.RequestId, "wave");
        Assert.AreEqual(4, receipt.Revision);
        Assert.AreEqual(session, receipt.Snapshot.Session.SessionId);
        Assert.AreEqual(3, receipt.Snapshot.Session.Wave);
        Assert.AreEqual(15, receipt.Snapshot.Session.EnemyCount);
        Assert.AreEqual(1000, receipt.Snapshot.Session.Gold);
        Assert.AreEqual(3, receipt.Snapshot.Progress.Wave);
        Assert.IsFalse(receipt.Snapshot.Progress.WaveCompleted);
        var sql = AllSql(commands);
        StringAssert.Contains(sql, "INSERT INTO [PlayerParams]");
        var rows = commands.SelectMany(c => ((DynamicParameters)c.Parameters!).ParameterNames
            .Select(n => ((DynamicParameters)c.Parameters!).Get<object>(n)?.ToString() ?? "")).ToList();
        Assert.IsTrue(rows.Any(r => r.Contains("wave_start") && r.Contains(session.ToString("D"))));
        Assert.IsTrue(rows.Any(r => r.Contains("active_wave") && r.Contains(session.ToString("D").ToUpperInvariant())));
    }

    [TestMethod]
    public void TransitionWave_rejects_uncleared_wave()
    {
        var request = new WaveTransitionRequest { RequestId = Guid.NewGuid(), ExpectedRevision = 3, Wave = 9, Difficulty = 0 };
        var error = Assert.ThrowsExactly<BusinessOperationException>(() => BattlefieldCommand.Prepare(
            Player, request, Payload(request), BattleState(Guid.NewGuid()), BattlefieldCommand.TransitionPlan(request)));
        Assert.AreEqual("wave.cleared_only", error.ApiMessage.Key);
    }

    // ---- 試煉關卡（難度槽 4） ----

    private static BattlefieldCommand.State TrialState(params (string Group, string Name, string Value)[] overrides)
    {
        var progress = Progress();
        foreach (var (group, name, value) in overrides)
        {
            progress.Parameters.RemoveAll(row => row.Group == group && row.Name == name);
            // 通關紀錄以 Sequence 決定槽位，覆寫列必須維持原槽順序。
            progress.Parameters.Add(new PlayerParams
            { Group = group, Name = name, Value = value, Sequence = group.EndsWith("cleared_waves") ? int.Parse(name) : 0 });
        }
        return new BattlefieldCommand.State { Progress = progress, Characters = [] };
    }

    private static GameProgressModel Transition(BattlefieldCommand.State state, int wave, int difficulty, bool challenge)
    {
        var request = new WaveTransitionRequest
        { RequestId = Guid.NewGuid(), ExpectedRevision = 3, Wave = wave, Difficulty = difficulty, Challenge = challenge };
        return Receipt<WaveTransitionSnapshot>(BattlefieldCommand.Prepare(
            Player, request, Payload(request), state, BattlefieldCommand.TransitionPlan(request))).Snapshot.Progress;
    }

    [TestMethod]
    public void Trial_entry_records_return_point_pads_clear_slots_and_keeps_infinite_level()
    {
        var state = TrialState(("game_progress", "difficulty", "3"), ("game_progress", "wave", "4"),
            ("game_progress", "infinite_level", "7"), ("game_progress.cleared_waves", "3", "4"));
        var request = new WaveTransitionRequest
        { RequestId = Guid.NewGuid(), ExpectedRevision = 3, Wave = 1, Difficulty = 4, Challenge = true };
        var receipt = Receipt<WaveTransitionSnapshot>(BattlefieldCommand.Prepare(
            Player, request, Payload(request), state, BattlefieldCommand.TransitionPlan(request)));
        var progress = receipt.Snapshot.Progress;
        Assert.AreEqual(4, progress.Difficulty);
        Assert.AreEqual(1, progress.Wave);
        Assert.AreEqual(3, progress.TrialReturnDifficulty);
        Assert.AreEqual(4, progress.TrialReturnWave);
        Assert.AreEqual(7, progress.InfiniteLevel);
        Assert.AreEqual(StageRules.SlotCount, progress.ClearedWaves.Count);
        Assert.AreEqual(0, progress.ClearedWaves[4].GetInt32());
        Assert.AreEqual(4, receipt.Snapshot.Session.Difficulty);
        Assert.AreEqual(9, receipt.Snapshot.Session.EnemyCount);
    }

    [TestMethod]
    public void Trial_return_to_infinite_does_not_recompute_infinite_level()
    {
        var state = TrialState(("game_progress", "difficulty", "4"), ("game_progress", "wave", "3"),
            ("game_progress", "infinite_level", "7"), ("game_progress", "trial_return_difficulty", "3"),
            ("game_progress", "trial_return_wave", "4"), ("game_progress.cleared_waves", "3", "4"));
        var progress = Transition(state, 1, 3, challenge: true);
        Assert.AreEqual(3, progress.Difficulty);
        Assert.AreEqual(7, progress.InfiniteLevel);
    }

    [TestMethod]
    public void Trial_return_to_infinite_from_normal_return_point_uses_that_wave()
    {
        var state = TrialState(("game_progress", "difficulty", "4"), ("game_progress", "wave", "3"),
            ("game_progress", "trial_return_difficulty", "1"), ("game_progress", "trial_return_wave", "9"));
        var progress = Transition(state, 1, 3, challenge: true);
        Assert.AreEqual(1 + (9 - 1) / 4, progress.InfiniteLevel);
    }

    [TestMethod]
    public void Trial_rejects_waves_beyond_ten_and_uncleared_jumps()
    {
        AssertKey(ApiMessageKeys.EconomyWaveInvalid, () => Transition(TrialState(), 11, 4, challenge: true));
        AssertKey(ApiMessageKeys.EconomyWaveInvalid, () => Transition(TrialState(), 1, 6, challenge: true));
        var error = Assert.ThrowsExactly<BusinessOperationException>(() => Transition(TrialState(), 3, 4, challenge: false));
        Assert.AreEqual("wave.cleared_only", error.ApiMessage.Key);
        var jump = Transition(TrialState(("game_progress.cleared_waves", "4", "6")), 5, 4, challenge: false);
        Assert.AreEqual(5, jump.Wave);
    }

    [TestMethod]
    public void Trial_rules_use_intro_level_curve_and_do_not_advance_past_the_last_wave()
    {
        Assert.AreEqual(1 + (7 - 1) / 2, StageRules.EnemyLevel(4, 7, 1));
        Assert.AreEqual(9, StageRules.EnemyLevel(3, 80, 9));
        Assert.AreEqual((6, 4, 1), ProgressionRules.NextWave(5, 4, 1));
        Assert.AreEqual((10, 4, 1), ProgressionRules.NextWave(10, 4, 1));
        Assert.AreEqual((1, 1, 50), ProgressionRules.NextWave(100, 0, 1));
        Assert.IsTrue(StageRules.IsStageFinished(4, 10, true));
        Assert.IsFalse(StageRules.IsStageFinished(4, 10, false));
        Assert.IsFalse(StageRules.IsStageFinished(0, 100, true));
        Assert.IsTrue(StageRules.IsValidTarget(4, 10));
        Assert.IsFalse(StageRules.IsValidTarget(4, 11));
        Assert.IsFalse(StageRules.IsValidTarget(6, 1));
    }

    // ---- 挑戰關卡（難度槽 5）：每日 3 次，進入時扣次數 ----

    private static readonly DateTime ChallengeDay = new(2026, 10, 20, 9, 0, 0);

    private static (GameProgressModel Progress, string Sql, string Value) EnterChallenge(
        BattlefieldCommand.State state, DateTime day, bool challenge = true, int wave = 1)
    {
        var request = new WaveTransitionRequest
        { RequestId = Guid.NewGuid(), ExpectedRevision = RevisionCommand.CurrentValue(state.Progress.Parameters, nameof(GameProgressModel)),
          Wave = wave, Difficulty = 5, Challenge = challenge };
        var commands = BattlefieldCommand.Prepare(Player, request, Payload(request), state,
            BattlefieldCommand.TransitionPlan(request, day));
        var row = state.Progress.Parameters.Single(p => p.Name == ChallengeRules.AttemptName(5));
        return (Receipt<WaveTransitionSnapshot>(commands).Snapshot.Progress, AllSql(commands), row.Value);
    }

    [TestMethod]
    public void Challenge_entry_records_return_point_and_creates_todays_attempt_row()
    {
        var state = TrialState(("game_progress", "difficulty", "1"), ("game_progress", "wave", "12"),
            ("game_progress.cleared_waves", "1", "12"));
        var (progress, sql, value) = EnterChallenge(state, ChallengeDay);
        Assert.AreEqual(5, progress.Difficulty);
        Assert.AreEqual(1, progress.TrialReturnDifficulty);
        Assert.AreEqual(12, progress.TrialReturnWave);
        var attempts = ChallengeRules.Attempts.Parse(value)!;
        Assert.AreEqual("2026-10-20", attempts.Date);
        Assert.AreEqual(1, attempts.Used);
        Assert.IsFalse(attempts.Rewarded);
        Assert.AreEqual(2, attempts.Remaining);
        StringAssert.Contains(sql, "INSERT INTO [PlayerParams]");
    }

    [TestMethod]
    public void Challenge_entry_consumes_one_attempt_per_entry_and_rejects_the_fourth()
    {
        var state = TrialState();
        for (int used = 1; used <= ChallengeRules.DailyLimit; used++)
        {
            var (_, _, value) = EnterChallenge(state, ChallengeDay);
            Assert.AreEqual(used, ChallengeRules.Attempts.Parse(value)!.Used);
        }
        var rejected = Assert.ThrowsExactly<BusinessOperationException>(() => EnterChallenge(state, ChallengeDay));
        Assert.AreEqual(ApiMessageKeys.ChallengeNoAttempts, rejected.ApiMessage.Key);
    }

    [TestMethod]
    public void Challenge_attempts_reset_on_a_new_server_day_and_clear_the_reward_flag()
    {
        var state = TrialState(("economy", ChallengeRules.AttemptName(5),
            new ChallengeRules.Attempts { Date = "2026-10-19", Used = 3, Rewarded = true }.Serialize()));
        var (_, _, value) = EnterChallenge(state, ChallengeDay);
        var attempts = ChallengeRules.Attempts.Parse(value)!;
        Assert.AreEqual("2026-10-20", attempts.Date);
        Assert.AreEqual(1, attempts.Used);
        Assert.IsFalse(attempts.Rewarded);
    }

    [TestMethod]
    public void Challenge_must_restart_from_wave_one_even_when_cleared()
    {
        var state = TrialState(("game_progress.cleared_waves", "5", "10"));
        var error = Assert.ThrowsExactly<BusinessOperationException>(() => EnterChallenge(state, ChallengeDay, challenge: false, wave: 5));
        Assert.AreEqual("wave.challenge_first", error.ApiMessage.Key);
        AssertKey(ApiMessageKeys.EconomyWaveInvalid, () => EnterChallenge(state, ChallengeDay, wave: 11));
    }

    [TestMethod]
    public void Challenge_rules_share_the_limited_stage_behaviour_with_trials()
    {
        Assert.IsTrue(StageRules.IsChallenge(5));
        Assert.IsFalse(StageRules.IsTrial(5));
        Assert.IsTrue(StageRules.IsLimitedStage(4));
        Assert.AreEqual(10, StageRules.MaxWave(5));
        Assert.AreEqual((10, 5, 1), ProgressionRules.NextWave(10, 5, 1));
        Assert.IsTrue(StageRules.IsStageFinished(5, 10, true));
        Assert.IsTrue(StageRules.IsValidTarget(5, 10));
        Assert.IsFalse(StageRules.IsValidTarget(5, 11));
    }
}
