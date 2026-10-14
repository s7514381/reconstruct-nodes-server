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
        Assert.HasCount(4, repository.Calls);
        Assert.Contains("Status != -1 ORDER BY CreateDate", repository.Calls[0].Sql!);
        foreach (var call in repository.Calls.Skip(1))
        {
            Assert.Contains("p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId", call.Sql!);
            Assert.Contains("IN @ParentIds", call.Sql!);
            Assert.Contains("ORDER BY c.Sequence", call.Sql!);
            Assert.DoesNotContain("c.Status", call.Sql!);
        }
        Assert.DoesNotContain("p.Status", repository.Calls[2].Sql!);
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
        Assert.HasCount(5, repository.Calls);
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
        Assert.Contains("SELECT TOP 1", repository.Calls[0].Sql!);
        foreach (var call in repository.Calls.Skip(1))
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
        var details = repository.Calls.Select(call => call.Value).OfType<AchievementDetail>().ToList();
        CollectionAssert.AreEqual(new int?[] { 0, 1, 0 }, details.Select(row => row.Sequence).ToArray());
        var collected = repository.Calls.Select(call => call.Value).OfType<AchievementCollected>().ToList();
        CollectionAssert.AreEqual(new int?[] { 0, 1, 0 }, collected.Select(row => row.Sequence).ToArray());
        Assert.AreEqual(details[2].AchievementId, collected[2].AchievementId);
        Assert.AreNotEqual(details[0].AchievementId, details[2].AchievementId);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", repository.Calls[0].Sql!);
        Assert.Contains("p.Status != -1", repository.Calls[1].Sql!);
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
        Assert.HasCount(2, repository.Calls);
        Assert.Contains("FROM Achievement WITH (UPDLOCK, HOLDLOCK)", repository.Calls[0].Sql!);
        Assert.Contains("FROM AchievementDetail WITH (UPDLOCK, HOLDLOCK)", repository.Calls[1].Sql!);
        Assert.IsTrue(repository.Calls.All(call => call.Sql!.Contains("CreateUserId = @CreateUserId")));
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
        string delete = repository.Calls.Single(call => call.Sql?.StartsWith("DELETE FROM PlayerParams") == true).Sql!;
        Assert.Contains("NOT ([Group] = 'game_progress' AND Name = 'gold')", delete);
        Assert.Contains("'game_progress.unlocked_bullet_kinds', 'game_progress.unlocked_auxiliary_kinds'", delete);
        var rows = repository.Calls.Select(call => call.Value).OfType<PlayerParams>().ToList();
        Assert.IsFalse(rows.Any(row => row.Name == "gold" || row.Group is "game_progress.unlocked_bullet_kinds" or "game_progress.unlocked_auxiliary_kinds"));
        Assert.IsTrue(rows.Where(row => row.Group == "game_progress").All(row => row.Sequence == null));
        CollectionAssert.AreEqual(new int?[] { 0, 1 }, rows.Where(row => row.Group.EndsWith("skill_suffixes")).Select(row => row.Sequence).ToArray());
        Assert.AreEqual(0, rows.Single(row => row.Group.EndsWith("progression")).Sequence);
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
