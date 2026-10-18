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
        // 等級 11 = 60 點；其他角色已用 10 點，本角色最多 50。
        var (request, payload, state) = Attributes(11, 50, 0, 0, otherAllocated: 10);
        Assert.AreEqual(8, Receipt<CharacterModel>(CharacterAttributesCommand.Prepare(Player, request, payload, state)).Revision);

        (request, payload, state) = Attributes(11, 51, 0, 0, otherAllocated: 10);
        AssertKey(ApiMessageKeys.CharacterAttributesExceeded, () => CharacterAttributesCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Attributes(11, -1, 5, 5);
        AssertKey(ApiMessageKeys.CharacterAttributesInvalid, () => CharacterAttributesCommand.Prepare(Player, request, payload, state));

        (request, payload, state) = Attributes(1, 1, 0, 0);
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
        new() { Progress = Progress(gold, revision), Characters = [character] };

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
}
