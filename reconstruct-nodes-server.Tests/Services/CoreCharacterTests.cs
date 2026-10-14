using System.Reflection;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests.Services;

[TestClass]
public class CoreCharacterTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FirstRead_NewAndOldAccounts_GrantsOneUnarmedCoreWithFireball(bool hasOrdinaryCharacter)
    {
        var (service, repository) = CreateReader();
        if (hasOrdinaryCharacter) { repository.Rows.Add(new Character { Kind = 2 }); }
        var first = await ReadTransaction(service, repository);
        var core = first.Single(row => row.Kind == CharacterService.CoreKind);
        Assert.IsNotNull(core.Id);
        Assert.AreNotEqual(Guid.Empty, core.Id);
        Assert.IsTrue(string.IsNullOrEmpty(core.WeaponEntryId));
        Assert.AreEqual(1, core.SkillTrees.Single().Kind);
        Assert.HasCount(0, core.SkillTrees.Single().AuxiliaryNodes);
        var second = await ReadTransaction(service, repository);
        Assert.AreEqual(core.Id, second.Single(row => row.Kind == CharacterService.CoreKind).Id);
        Assert.HasCount(hasOrdinaryCharacter ? 2 : 1, second);
        Assert.HasCount(2, repository.Inserted);
        Assert.IsTrue(repository.Inserted.All(row => row.CreateUserId.HasValue));
        // No Gold/Weapon/PlayerParams writes, hence neither a starter weapon nor a purchase fee.
        Assert.IsInstanceOfType<Character>(repository.Inserted[0]);
        Assert.IsInstanceOfType<CharacterSkillTree>(repository.Inserted[1]);
    }

    [TestMethod]
    public async Task ConcurrentFirstReads_SerializeOnPlayerLock_AndReturnSameGuid()
    {
        var (service, repository) = CreateReader();
        var reads = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => ReadTransaction(service, repository)));
        Assert.HasCount(1, reads.SelectMany(rows => rows).Select(row => row.Id).Distinct().ToList());
        Assert.HasCount(1, repository.Rows.OfType<Character>().ToList());
        Assert.HasCount(2, repository.Inserted);
        Assert.AreEqual(12, repository.LockCount);
    }

    [TestMethod]
    public async Task ExistingCore_EmptySkillsAndWeapon_AreNotReinitialized()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        Character core = new() { Kind = CharacterService.CoreKind, WeaponEntryKey = "owned-entry" };
        repository.QueryRows[typeof(Character)] = new[] { core };
        repository.QueryRows[typeof(Weapon)] = new[]
        {
            new Weapon { EntryKey = "owned-entry", WeaponKey = "staff" },
        };
        CharacterModel result = (await new CharacterService(argument).GetList()).Single();
        Assert.AreEqual(core.Id, result.Id);
        Assert.AreEqual("owned-entry", result.WeaponEntryId);
        Assert.HasCount(0, result.SkillTrees);
        Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
    }

    [TestMethod]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(4, false)]
    public async Task CoreSkillAccess_FireballIsFree_OtherSkillsStillRequireOwnership(int skill, bool allowed)
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        Character core = new() { Kind = CharacterService.CoreKind };
        repository.Existing = core;
        repository.QueryRows[typeof(Character)] = new[] { core };
        CharacterModel model = new()
        {
            Id = core.Id, Kind = CharacterService.CoreKind,
            SkillTrees = [new GameProgressSkillLoadoutModel { Kind = skill }],
        };
        if (allowed)
        {
            await new CharacterService(argument).Save(model);
            Assert.AreEqual(core.Id, model.Id);
            Assert.AreEqual(skill, repository.Calls.Select(call => call.Value).OfType<CharacterSkillTree>().Single().Kind);
        }
        else
        {
            await Assert.ThrowsAsync<ArgumentException>(() => new CharacterService(argument).Save(model));
            Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
        }
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(4)]
    public async Task UnknownKind_WithEmptySkills_IsRejectedBeforeRepository(int kind)
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        await Assert.ThrowsAsync<ArgumentException>(() => new CharacterService(argument).Save(
            new CharacterModel { Id = Guid.NewGuid(), Kind = kind }));
        Assert.HasCount(0, repository.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CoreNestedSkills_UseUsualActiveAndSupportUnlocks(bool unlocked)
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        Character core = new() { Kind = CharacterService.CoreKind };
        repository.Existing = core;
        repository.QueryRows[typeof(Character)] = new[] { core };
        if (unlocked)
        {
            repository.QueryRows[typeof(PlayerParams)] = new[]
            {
                new PlayerParams { Group = "game_progress.unlocked_bullet_kinds", Name = "2", Value = "true" },
                new PlayerParams { Group = "game_progress.unlocked_auxiliary_kinds", Name = "support", Value = "true" },
            };
        }
        CharacterModel model = new()
        {
            Id = core.Id, Kind = CharacterService.CoreKind,
            SkillTrees = [new GameProgressSkillLoadoutModel
            {
                Kind = CharacterService.CoreDefaultSkillKind,
                AuxiliaryNodes =
                [
                    new GameProgressSkillNodeModel { Type = "active", Kind = 2 },
                    new GameProgressSkillNodeModel { Type = "auxiliary", LootId = "support" },
                ],
            }],
        };
        if (unlocked)
        {
            await new CharacterService(argument).Save(model);
            Assert.HasCount(2, repository.Calls.Select(call => call.Value).OfType<CharacterSkillNode>().ToList());
        }
        else
        {
            var service = new CharacterService(argument);
            await Assert.ThrowsAsync<ArgumentException>(() => service.Save(model));
            model.SkillTrees[0].AuxiliaryNodes.RemoveAt(0);
            await Assert.ThrowsAsync<ArgumentException>(() => service.Save(model));
            Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
        }
    }

    [TestMethod]
    public async Task CoreCreationFailure_PropagatesForControllerRollback_WithoutInsertingSkillTree()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        repository.FailInsert = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CharacterService(argument).GetList());
        Assert.HasCount(1, repository.Calls.Where(call => call.Name == "InsertAsync").ToList());
        Assert.IsFalse(repository.Calls.Any(call => call.Value is CharacterSkillTree));
    }

    [TestMethod]
    public async Task OrdinaryDeploymentQuery_ExcludesCoreBeforeChargesOrWrites()
    {
        var (argument, repository) = SnapshotSaveTests.CreateArgument();
        // Repository returns no deployable IDs: a core GUID is not an ordinary deployment.
        await Assert.ThrowsAsync<ArgumentException>(() => new GameProgressService(argument).Save(
            new GameProgressModel { DeploymentLog = [new GameProgressDeploymentModel { CharacterId = Guid.NewGuid() }] }));
        Assert.Contains("Kind IN (0, 1, 2)", repository.Calls[1].Sql!);
        Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
        Assert.IsFalse(ShopService.Catalog.Characters.Any(offer => offer.Kind == CharacterService.CoreKind));
    }

    private static (CharacterService, CoreReadRepository) CreateReader()
    {
        var proxy = DispatchProxy.Create<IDapperRepository, CoreReadRepository>();
        return (new CharacterService(SnapshotSaveTests.CreateServiceArgument(proxy, Guid.NewGuid())),
            (CoreReadRepository)proxy);
    }

    private static async Task<List<CharacterModel>> ReadTransaction(CharacterService service, CoreReadRepository repository)
    {
        try { return await service.GetList(); }
        finally { repository.PlayerLock.Release(); }
    }

    // In-memory transaction/lock double. This checks call ordering and concurrent idempotency;
    // it does not replace an opt-in SQL Server isolation/rollback integration test.
    public class CoreReadRepository : DispatchProxy
    {
        public SemaphoreSlim PlayerLock { get; } = new(1);
        public List<ModelBase> Rows { get; } = [];
        public List<ModelBase> Inserted { get; } = [];
        public int LockCount { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(args);
            if (method.Name == "InsertAsync")
            {
                var row = (ModelBase)args[0]!;
                Rows.Add(row);
                Inserted.Add(row);
                return Task.FromResult(1);
            }
            if (method.Name == "QueryAsync")
            {
                if (((string)args[0]!).Contains("FROM Player WITH (UPDLOCK, HOLDLOCK)"))
                {
                    return Lock();
                }
                Assert.AreEqual(0, PlayerLock.CurrentCount, "Read must follow the transaction's player lock");
                return typeof(CoreReadRepository).GetMethod(nameof(Query), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(method.GetGenericArguments()).Invoke(this, null);
            }
            throw new InvalidOperationException($"Unexpected call: {method.Name}");
        }

        private async Task<IEnumerable<Guid>> Lock()
        {
            await PlayerLock.WaitAsync();
            LockCount++;
            await Task.Yield();
            return [];
        }

        private Task<IEnumerable<T>> Query<T>() => Task.FromResult<IEnumerable<T>>(Rows.OfType<T>().ToArray());
    }
}
