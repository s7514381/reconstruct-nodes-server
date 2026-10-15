using System.Linq.Expressions;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests.Services;

[TestClass]
public class SnapshotPersistenceTests
{
    [TestMethod]
    public async Task DescendantDeletes_UseMetadata_AndRestrictBothOwners_WithoutStatusFilter()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        var service = new PersistenceProbe(argument);
        Guid id = Guid.NewGuid();
        await service.DeleteWeaponValues(id);
        await service.DeleteCharacterNodes(id);

        string[] tables = ["WeaponAffixValue", "CharacterSkillNode"];
        string[] parents = ["WeaponAffix", "CharacterSkillTree"];
        string[] ancestorKeys = ["WeaponId", "CharacterId"];
        for (int i = 0; i < 2; i++)
        {
            var call = repository.Calls[i];
            Assert.Contains($"DELETE FROM {tables[i]} WHERE CreateUserId = @CreateUserId", call.Sql!);
            Assert.Contains($"IN (SELECT Id FROM {parents[i]} WHERE {ancestorKeys[i]} = @ParentId AND CreateUserId = @CreateUserId)", call.Sql!);
            Assert.DoesNotContain("Status", call.Sql!);
            Assert.AreEqual(id, call.Value!.GetType().GetProperty("ParentId")!.GetValue(call.Value));
        }
    }

    [TestMethod]
    public async Task LimitedUpdate_RejectsProtectedColumns_AndNonPropertyExpressionsBeforeExecuting()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        var service = new PersistenceProbe(argument);
        Expression<Func<Weapon, object?>>[] forbidden =
        [
            row => row.Id, row => row.CreateUserId, row => row.CreateDate,
            row => row.Status, row => row.EntryKey, row => row.WeaponKey.Length,
            row => row.WeaponKey.ToUpperInvariant(),
        ];
        foreach (var selector in forbidden)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.Update(selector));
        }
        await Assert.ThrowsAsync<ArgumentException>(() => service.Update());
        Assert.HasCount(0, repository.Calls);
    }

    [TestMethod]
    public async Task LimitedUpdate_DeduplicatesFields_AndUsesCurrentPlayerRatherThanRowOwner()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        await new PersistenceProbe(argument).Update(row => row.WeaponKey, row => row.WeaponKey);
        var call = repository.Calls.Single();
        Assert.AreEqual("UPDATE Weapon SET WeaponKey = @WeaponKey WHERE Id = @Id AND CreateUserId = @CreateUserId", call.Sql);
        var parameters = (Dapper.DynamicParameters)call.Value!;
        Assert.HasCount(3, parameters.ParameterNames);
        Assert.AreEqual(new WeaponService(argument)._playerId, parameters.Get<Guid?>("CreateUserId"));
    }

    [TestMethod]
    public async Task Weapon_Read_PreservesInactiveDetails_AndBatchOrderAndOwnerFilters()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        Weapon weapon = new() { EntryKey = "entry", UniqueKey = "unique" };
        WeaponAffix affix = new() { WeaponId = weapon.Id, AffixKey = "affix", Status = (EStatus)(-1) };
        repository.QueryRows[typeof(Weapon)] = new[] { weapon };
        repository.QueryRows[typeof(WeaponAffix)] = new[] { affix };
        repository.QueryRows[typeof(WeaponAffixValue)] = new[]
        {
            new WeaponAffixValue { WeaponAffixId = affix.Id, Value = "4", Status = (EStatus)(-1) },
        };
        await new WeaponService(argument).GetInstanceList();
        Assert.HasCount(6, repository.Calls);
        Assert.Contains("Player WITH (UPDLOCK, HOLDLOCK)", repository.Calls[0].Sql!);
        Assert.Contains("Status != -1 ORDER BY CreateDate", repository.Calls[1].Sql!);
        foreach (var call in repository.Calls.Skip(2).Take(3))
        {
            Assert.Contains("p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId", call.Sql!);
            Assert.Contains("IN @ParentIds", call.Sql!);
            Assert.Contains("ORDER BY c.Sequence", call.Sql!);
            Assert.DoesNotContain("c.Status", call.Sql!);
        }
        Assert.DoesNotContain("p.Status", repository.Calls[3].Sql!);
    }

    [TestMethod]
    public async Task Character_Read_FiltersActiveTreesAndNodes_WithoutNPlusOne()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        Character character = new() { Kind = CharacterService.CoreKind };
        repository.QueryRows[typeof(Character)] = new[] { character };
        repository.QueryRows[typeof(CharacterSkillTree)] = new[] { new CharacterSkillTree { CharacterId = character.Id } };
        await new CharacterService(argument).GetList();
        Assert.Contains("ORDER BY Kind", repository.Calls[1].Sql!);
        foreach (var call in repository.Calls.Skip(2).Take(2))
        {
            Assert.Contains("c.Status != -1", call.Sql!);
            Assert.Contains("p.Status != -1", call.Sql!);
            Assert.Contains("IN @ParentIds", call.Sql!);
            Assert.Contains("ORDER BY c.Sequence", call.Sql!);
        }
        // 玩家鎖、角色、兩層技能、武器（無武器時提早返回）。
        Assert.HasCount(7, repository.Calls);
    }

    [TestMethod]
    public async Task EmptyReads_PreserveNullAndEmptySemantics()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        Assert.IsNull(await new TalentService(argument).GetSnapshot());
        Assert.IsNull(await new AchievementProgressService(argument, new ItemService(argument)).Get());
        Assert.IsNull(await new GameProgressService(argument).Get());
        Assert.HasCount(0, (await new WeaponService(argument).GetInstanceList()).ToList());
        Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "ExecuteAsync" or "UpdateAsync"));
    }

    [TestMethod]
    public async Task Talent_Read_UsesOwnedActiveParent_AndUnfilteredOrderedDetails()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        Talent parent = new();
        repository.FirstRows[typeof(Talent)] = parent;
        repository.QueryRows[typeof(TalentAcquired)] = new[]
        {
            new TalentAcquired { TalentId = parent.Id, TreeId = "tree", NodeId = "node", Value = "2", Status = (EStatus)(-1) },
        };
        TalentModel? model = await new TalentService(argument).GetSnapshot();
        Assert.AreEqual(2, model!.AcquiredTalents["tree"]["node"].GetInt32());
        Assert.Contains("Player WITH (UPDLOCK, HOLDLOCK)", repository.Calls[0].Sql!);
        Assert.Contains("SELECT TOP 1", repository.Calls[1].Sql!);
        foreach (var call in repository.Calls.Skip(2).Take(2))
        {
            Assert.Contains("p.Status != -1", call.Sql!);
            Assert.Contains("p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId", call.Sql!);
            Assert.DoesNotContain("c.Status", call.Sql!);
            Assert.Contains("ORDER BY c.Sequence", call.Sql!);
        }
    }

    [TestMethod]
    public async Task OrderedWrites_ResetEachCollection_AndStopOnInsertFailure()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        var service = new PersistenceProbe(argument);
        await service.InsertValues([7, 8]);
        await service.InsertValues([9]);
        var rows = repository.Calls.Select(call => (WeaponUniqueValue)call.Value!).ToList();
        CollectionAssert.AreEqual(new int?[] { 0, 1, 0 }, rows.Select(row => row.Sequence).ToArray());
        Assert.IsTrue(rows.All(row => row.CreateUserId == rows[0].CreateUserId && row.CreateUserId.HasValue));
        repository.FailInsert = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InsertValues([10, 11]));
        Assert.HasCount(4, repository.Calls);
    }

    [TestMethod]
    public async Task Achievement_ReplacesGroups_WithIndependentSequencesAndServerClaims()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        await new AchievementProgressService(argument, new ItemService(argument)).Save(new AchievementProgressModel
        {
            Groups =
            [
                new() { Name = "first", Items = [new() { Name = "a", Target = 1 }, new() { Name = "b", Target = 1 }], Collected = ["x", "y"] },
                new() { Name = "second", Items = [new() { Name = "c", Target = 1 }], Collected = ["z"] },
            ],
        });
        var details = SnapshotSaveTests.BatchRows<AchievementDetail>(repository, "Details");
        CollectionAssert.AreEqual(new int?[] { 0, 1, 0 }, details.Select(row => row.Sequence).ToArray());
        var collected = SnapshotSaveTests.BatchRows<AchievementCollected>(repository, "Collected");
        CollectionAssert.AreEqual(new int?[] { 0, 1, 0 }, collected.Select(row => row.Sequence).ToArray());
        Assert.AreEqual(details[2].AchievementId, collected[2].AchievementId);
        Assert.AreNotEqual(details[0].AchievementId, details[2].AchievementId);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", repository.Calls[0].Sql!);
        Assert.Contains("p.Status != -1", repository.Calls[3].Sql!);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Achievement_Claim_LocksGroupsBeforeDetail_AndRejectsClaimedOrIncomplete(bool claimed)
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        Achievement group = new() { Name = "group", Total = claimed ? 10 : 0 };
        repository.QueryRows[typeof(Achievement)] = new[] { group };
        repository.FirstRows[typeof(AchievementDetail)] = new AchievementDetail
        {
            AchievementId = group.Id, Name = "item", Target = 1, IsReward = claimed,
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AchievementProgressService(argument, new ItemService(argument)).ClaimReward("item"));
        Assert.HasCount(3, repository.Calls);
        Assert.Contains("FROM Achievement WITH (UPDLOCK, HOLDLOCK)", repository.Calls[1].Sql!);
        Assert.Contains("FROM AchievementDetail WITH (UPDLOCK, HOLDLOCK)", repository.Calls[2].Sql!);
        Assert.IsTrue(repository.Calls.Skip(1).All(call => call.Sql!.Contains("CreateUserId = @CreateUserId")));
    }

    [TestMethod]
    public async Task Progress_ProtectsGoldAndServerUnlocks_AndKeepsScalarSequenceNull()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        await new GameProgressService(argument).Save(new GameProgressModel
        {
            Gold = 999,
            UnlockedBulletKinds = new() { ["1"] = JsonSerializer.SerializeToElement(true) },
            UnlockedAuxiliaryKinds = new() { ["2"] = JsonSerializer.SerializeToElement(true) },
            SkillSuffixes = new() { ["a"] = JsonSerializer.SerializeToElement(1), ["b"] = JsonSerializer.SerializeToElement(2) },
            Progression = new() { ["c"] = JsonSerializer.SerializeToElement(3) },
        });
        string delete = repository.Calls.Single(call => call.Sql?.Contains("DELETE FROM PlayerParams") == true).Sql!;
        Assert.Contains("NOT ([Group] = 'game_progress' AND Name = 'gold')", delete);
        Assert.Contains("'game_progress.unlocked_bullet_kinds', 'game_progress.unlocked_auxiliary_kinds'", delete);
        var rows = SnapshotSaveTests.BatchRows<PlayerParams>(repository, "Parameters");
        Assert.IsFalse(rows.Any(row => row.Name == "gold" || row.Group is "game_progress.unlocked_bullet_kinds" or "game_progress.unlocked_auxiliary_kinds"));
        Assert.IsTrue(rows.Where(row => row.Group == "game_progress").All(row => row.Sequence == null));
        CollectionAssert.AreEqual(new int?[] { 0, 1 }, rows.Where(row => row.Group.EndsWith("skill_suffixes")).Select(row => row.Sequence).ToArray());
        Assert.AreEqual(0, rows.Single(row => row.Group.EndsWith("progression")).Sequence);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(200)]
    public async Task SnapshotReplacement_UsesOneParameterizedWriteRegardlessOfRowCount(int count)
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        string quotedName = "測試'; DELETE FROM Player;--";
        await new AchievementProgressService(argument, new ItemService(argument)).Save(new AchievementProgressModel
        {
            Groups = Enumerable.Range(0, count).Select(index => new AchievementGroupModel
            {
                Name = $"group-{index}", Total = index,
                Items = [new() { Name = $"{quotedName}-{index}", Target = 1 }],
                Collected = ["first", "second"],
            }).ToList(),
        });
        var achievementWrite = repository.Calls.Single(call => call.Name == "ExecuteAsync");
        Assert.DoesNotContain(quotedName, achievementWrite.Sql!);
        Assert.HasCount(count, SnapshotSaveTests.BatchRows<Achievement>(repository, "Groups"));
        Assert.HasCount(count, SnapshotSaveTests.BatchRows<AchievementDetail>(repository, "Details"));
        Assert.HasCount(count * 2, SnapshotSaveTests.BatchRows<AchievementCollected>(repository, "Collected"));
        Assert.IsFalse(repository.Calls.Any(call => call.Name == "InsertAsync"));

        repository.Calls.Clear();
        await new GameProgressService(argument).Save(new GameProgressModel
        {
            SkillSuffixes = Enumerable.Range(0, count).ToDictionary(
                index => $"{quotedName}-{index}", index => JsonSerializer.SerializeToElement(index)),
            Battlefield = new()
            {
                Castle = [1, 2],
                Roads = Enumerable.Range(0, count).Select(index => new GameProgressRoadModel
                {
                    X = index, Y = index + 1, Corner = index % 2 == 0, Rotation = index % 4,
                }).ToList(),
            },
        });
        var progressWrite = repository.Calls.Single(call => call.Name == "ExecuteAsync");
        Assert.DoesNotContain(quotedName, progressWrite.Sql!);
        Assert.HasCount(12 + count, SnapshotSaveTests.BatchRows<PlayerParams>(repository, "Parameters"));
        Assert.HasCount(count, SnapshotSaveTests.BatchRows<BattlefieldRoad>(repository, "Roads"));
        Assert.HasCount(0, SnapshotSaveTests.BatchRows<Deployment>(repository, "Deployments"));
        Assert.IsFalse(repository.Calls.Any(call => call.Name == "InsertAsync"));
    }

    [TestMethod]
    public async Task SnapshotReplacement_PropagatesBatchFailures()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        repository.FailExecute = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AchievementProgressService(argument, new ItemService(argument)).Save(new()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GameProgressService(argument).Save(new()));
        Assert.AreEqual(2, repository.Calls.Count(call => call.Name == "ExecuteAsync"));
        Assert.IsFalse(repository.Calls.Any(call => call.Name == "InsertAsync"));
    }

    [TestMethod]
    public async Task CharacterReplacement_PreservesTreeLinksAndIndependentNodeSequences()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        Character core = new() { Kind = CharacterService.CoreKind };
        repository.Existing = core;
        repository.QueryRows[typeof(Character)] = new[] { core };
        await new CharacterService(argument).Save(new CharacterModel
        {
            Id = core.Id, Kind = core.Kind,
            SkillTrees =
            [
                new() { Kind = 1, AuxiliaryNodes = [new() { Type = "active", Kind = 0 }, new() { Type = "active", Kind = 1 }] },
                new() { Kind = 0, AuxiliaryNodes = [new() { Type = "active", Kind = 0 }] },
            ],
        });
        var trees = SnapshotSaveTests.BatchRows<CharacterSkillTree>(repository, "Trees");
        var nodes = SnapshotSaveTests.BatchRows<CharacterSkillNode>(repository, "Nodes");
        CollectionAssert.AreEqual(new int?[] { 0, 1 }, trees.Select(tree => tree.Sequence).ToArray());
        CollectionAssert.AreEqual(new int?[] { 0, 1, 0 }, nodes.Select(node => node.Sequence).ToArray());
        Assert.AreEqual(trees[0].Id, nodes[0].CharacterSkillTreeId);
        Assert.AreEqual(trees[0].Id, nodes[1].CharacterSkillTreeId);
        Assert.AreEqual(trees[1].Id, nodes[2].CharacterSkillTreeId);
        Assert.IsTrue(trees.All(tree => tree.CharacterId == core.Id));
        Assert.AreEqual(1, repository.Calls.Count(call => call.Name == "ExecuteAsync"));
        Assert.IsFalse(repository.Calls.Any(call => call.Name == "InsertAsync"));
    }

    private sealed class PersistenceProbe(BaseServiceArgument argument) : _SnapshotService<Weapon, WeaponInstanceModel>(argument)
    {
        protected override string? NaturalKeyField => nameof(Weapon.EntryKey);
        protected override void ValidateSnapshot(WeaponInstanceModel model) { }
        protected override Task SaveSnapshot(WeaponInstanceModel model) => Task.CompletedTask;
        public Task Update(params Expression<Func<Weapon, object?>>[] fields) => UpdateSnapshotFieldsAsync(new Weapon(), fields);
        public Task DeleteWeaponValues(Guid id) => DeleteSnapshotDescendantsAsync<WeaponAffixValue, WeaponAffix, Weapon>(id);
        public Task DeleteCharacterNodes(Guid id) => DeleteSnapshotDescendantsAsync<CharacterSkillNode, CharacterSkillTree, Character>(id);
        public Task InsertValues(int[] values) => InsertSnapshotRowsAsync(values, (value, _) => new WeaponUniqueValue { Value = value.ToString() });
    }
}
