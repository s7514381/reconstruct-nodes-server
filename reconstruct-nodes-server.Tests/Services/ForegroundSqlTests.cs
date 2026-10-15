using Microsoft.Extensions.Configuration;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Tests.Services;

[TestClass]
[TestCategory("ForegroundSql")]
[DoNotParallelize]
public class ForegroundSqlTests
{
    private sealed class FailingWeaponSave(BaseServiceArgument a) : WeaponService(a)
    {
        protected override Task SaveSnapshot(WeaponInstanceModel model) =>
            throw new BusinessOperationException("test.injected_after_material_debit");
    }

    [TestMethod]
    public async Task Sql_RefinementFailureAfterDebitRollsBackMaterialAndReceipt()
    {
        using var repository = Repository();
        Guid owner = Guid.NewGuid();
        var a = SnapshotSaveTests.CreateServiceArgument(repository, owner);
        var outcome = await new BaseService(a).TransactionFunc(async () =>
        {
            await Seed(repository, owner);
            await new WeaponService(a).Save(new WeaponInstanceModel { Id = "failure-test", WeaponId = "sword", Rarity = "common" });
            var items = new ItemService(a);
            await items.AdjustAmount("awakening_crystal", 1, "refine");
            var commands = new WeaponCommandService(a, new(a), new FailingWeaponSave(a), items, new(a));
            await commands.Refine(new WeaponRefineRequest
            {
                RequestId = Guid.NewGuid(), WeaponEntryId = "failure-test", CrystalId = "awakening_crystal",
            });
        });
        Assert.IsFalse(outcome.Item1);
        Assert.AreEqual("test.injected_after_material_debit", ((IApiBusinessError)outcome.ex!).ApiMessage.Key);
        foreach (string table in new[] { "Weapon", "WeaponAffix", "WeaponAffixValue", "Item", "PlayerParams", "EconomyRequest" })
        {
            Assert.AreEqual(0, await repository.QueryFirstAsync<int>($"SELECT COUNT(*) FROM {table} WHERE CreateUserId=@owner", new { owner }));
        }
    }

    [TestMethod]
    public async Task Sql_DisjointCharactersMergeWithoutRejectingUnrelatedForegroundCommand()
    {
        using var repository = Repository();
        Guid owner = Guid.NewGuid();
        using var transaction = repository.OpenTransaction();
        var a = await Seed(repository, owner);
        var draw = await new ShopService(a, new GoldService(a), new ItemService(a)).Draw(new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = 1 });
        var core = (await new CharacterService(a).GetList()).Single(c => c.Kind == CharacterService.CoreKind);
        var commands = Commands(a);
        var first = await commands.ConfigureSkills(new SkillLoadoutRequest
        {
            RequestId = Guid.NewGuid(), CharacterId = core.Id!.Value, SkillTrees = [],
        });
        var second = await commands.ConfigureSkills(new SkillLoadoutRequest
        {
            RequestId = Guid.NewGuid(), CharacterId = draw.Results[0].CharacterId, SkillTrees = [],
            ExpectedRevision = 0, // Read before unrelated core command committed.
        });
        Assert.AreEqual(1L, first.Revision);
        Assert.AreEqual(2L, second.Revision);
        await Assert.ThrowsAsync<BusinessOperationException>(() => commands.ConfigureSkills(new SkillLoadoutRequest
        {
            RequestId = Guid.NewGuid(), CharacterId = core.Id.Value, SkillTrees = [], ExpectedRevision = 0,
        }));
        transaction.Rollback();
    }

    [TestMethod]
    public async Task Sql_DeploymentAndBattlefieldRestartCommitWithGoldAndSessionAtomically()
    {
        using var repository = Repository();
        Guid owner = Guid.NewGuid();
        using var transaction = repository.OpenTransaction();
        var a = await Seed(repository, owner);
        var draw = await new ShopService(a, new GoldService(a), new ItemService(a)).Draw(new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = 1 });
        await new GameProgressService(a).Save(new GameProgressModel { Version = 9, Wave = 1 });
        var commands = Commands(a);
        var deployment = new DeploymentCommandRequest
        {
            RequestId = Guid.NewGuid(), DeploymentLog = [new() { CharacterId = draw.Results[0].CharacterId, Cell = [0,0] }],
        };
        var placed = await commands.SetDeployment(deployment);
        Assert.AreEqual(1040L, placed.Snapshot.Gold);
        Assert.AreEqual(1040L, (await commands.SetDeployment(deployment)).Snapshot.Gold);
        var battlefield = new BattlefieldCommandRequest
        {
            RequestId = Guid.NewGuid(), ExpectedRevision = 1,
            Battlefield = new() { Version = 1, Columns = 7, Rows = 10, Castle = [5,8],
                Roads = Enumerable.Range(0,5).Select(x => new GameProgressRoadModel { X = x, Y = 8, Rotation = 0 }).ToList() },
            DeploymentLog = [new() { CharacterId = draw.Results[0].CharacterId, Cell = [1,0] }],
        };
        var edited = await commands.SetBattlefield(battlefield);
        Assert.AreEqual(2L, edited.Revision);
        Assert.AreEqual(980L, edited.Snapshot.Progress.Gold);
        Assert.AreEqual(1, edited.Snapshot.Session.Wave);
        Assert.AreNotEqual(Guid.Empty, edited.Snapshot.Session.SessionId);
        Assert.AreEqual(edited.Snapshot.Session.SessionId, (await commands.SetBattlefield(battlefield)).Snapshot.Session.SessionId);
        Assert.AreEqual(980L, await new GoldService(a).GetBalance());
        transaction.Rollback();
    }

    [TestMethod]
    public async Task Sql_LegacyProgressRequestReceiptReplaysBeforeCasWithoutOverwritingNewerState()
    {
        using var repository = Repository();
        Guid owner = Guid.NewGuid();
        using var transaction = repository.OpenTransaction();
        var a = await Seed(repository, owner);
        var progress = new GameProgressService(a);
        var first = new GameProgressModel { Version = 9, RequestId = Guid.NewGuid(), ExpectedRevision = 0, Wave = 1 };
        string immutable = System.Text.Json.JsonSerializer.Serialize(first);
        await progress.Save(first);
        await progress.Save(new GameProgressModel { Version = 9, RequestId = Guid.NewGuid(), ExpectedRevision = 1, Wave = 2 });
        await progress.Save(System.Text.Json.JsonSerializer.Deserialize<GameProgressModel>(immutable)!);
        var current = (await progress.Get())!;
        Assert.AreEqual(2, current.Wave);
        Assert.AreEqual(2L, current.Revision);
        transaction.Rollback();
    }
    [TestMethod]
    public async Task Sql_AtomicFailureRollsBackSnapshotClaimAndReceipt()
    {
        using var repository = Repository();
        Guid owner = Guid.NewGuid();
        var a = SnapshotSaveTests.CreateServiceArgument(repository, owner);
        var outcome = await new BaseService(a).TransactionFunc(async () =>
        {
            await Seed(repository, owner);
            await Commands(a).ClaimAchievement(new AchievementSnapshotClaimRequest
            {
                RequestId = Guid.NewGuid(), AchievementItemId = "incomplete",
                Snapshot = new AchievementProgressModel { Groups = [new() { Name = "group", Total = 0,
                    Items = [new() { Name = "incomplete", Target = 10 }] }] },
            });
        });
        Assert.IsFalse(outcome.Item1);
        Assert.IsInstanceOfType<BusinessOperationException>(outcome.ex);
        foreach (string table in new[] { "Achievement", "AchievementDetail", "AchievementCollected", "Item", "PlayerParams", "EconomyRequest" })
        {
            Assert.AreEqual(0, await repository.QueryFirstAsync<int>($"SELECT COUNT(*) FROM {table} WHERE CreateUserId=@owner", new { owner }));
        }
        Assert.AreEqual(0, await repository.QueryFirstAsync<int>("SELECT COUNT(*) FROM Player WHERE Id=@owner", new { owner }));
    }

    [TestMethod]
    public async Task Sql_ConcurrentSameRevisionHasOneWinner_AndCleansOnlyItsOwnFixture()
    {
        using var repository = Repository();
        using var other = Repository();
        Guid owner = Guid.NewGuid();
        var a = SnapshotSaveTests.CreateServiceArgument(repository, owner);
        var b = SnapshotSaveTests.CreateServiceArgument(other, owner);
        Guid character;
        using (var transaction = repository.OpenTransaction())
        {
            await Seed(repository, owner);
            character = (await new CharacterService(a).GetList()).Single().Id!.Value;
            transaction.Commit();
        }
        try
        {
            Task<(bool, Exception? ex)> Run(BaseServiceArgument argument) =>
                new BaseService(argument).TransactionFunc(async () =>
                    await Commands(argument).ConfigureSkills(new SkillLoadoutRequest
                    {
                        RequestId = Guid.NewGuid(), CharacterId = character, SkillTrees = [],
                    }));
            var outcomes = await Task.WhenAll(Run(a), Run(b));
            Assert.AreEqual(1, outcomes.Count(o => o.Item1));
            Assert.AreEqual("concurrency.revision_conflict",
                ((IApiBusinessError)outcomes.Single(o => !o.Item1).ex!).ApiMessage.Key);
            Assert.AreEqual(1, await repository.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@owner AND Operation='foreground.skill_loadout'", new { owner }));
        }
        finally
        {
            using var cleanup = repository.OpenTransaction();
            foreach (string table in new[] { "CharacterSkillNode", "CharacterSkillTree", "Character", "EconomyRequest", "PlayerParams" })
            {
                await repository.ExecuteAsync($"DELETE FROM {table} WHERE CreateUserId=@owner", new { owner });
            }
            await repository.ExecuteAsync("DELETE FROM Player WHERE Id=@owner", new { owner });
            cleanup.Commit();
        }
        Assert.AreEqual(0, await repository.QueryFirstAsync<int>("SELECT COUNT(*) FROM Player WHERE Id=@owner", new { owner }));
    }
    private static DapperRepository Repository()
    {
        if (Environment.GetEnvironmentVariable("FOREGROUND_SQL_TESTS") != "1")
        { Assert.Inconclusive("Opt in with FOREGROUND_SQL_TESTS=1; uses dedicated configured test SQL only."); }
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "reconstruct-nodes-server", "appsettings.json"), optional: true)
            .AddEnvironmentVariables().Build();
        string connection = Environment.GetEnvironmentVariable("FOREGROUND_SQL_CONNECTION")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing dedicated test SQL connection.");
        return new DapperRepository(new DapperDBContext(connection),
            new DbConnectionLimitService(2, 2), new DbOperateLimitService(2, 2));
    }

    private static async Task<BaseServiceArgument> Seed(DapperRepository repository, Guid owner)
    {
        await repository.InsertAsync(new Player { Id = owner, Name = "ForegroundIsolatedTest", IsClientLogin = true });
        return SnapshotSaveTests.CreateServiceArgument(repository, owner);
    }

    private static ForegroundService Commands(BaseServiceArgument a) => new(a, new(a), new(a),
        new(a, new ItemService(a)), new(a), new(a, new GoldService(a)));

    [TestMethod]
    public async Task Sql_CharacterReceiptReplayPrecedesVersionCheck_AndPayloadIsImmutable()
    {
        using var repository = Repository();
        Guid owner = Guid.NewGuid();
        using (var transaction = repository.OpenTransaction())
        {
            var a = await Seed(repository, owner);
            var characters = new CharacterService(a);
            CharacterModel core = (await characters.GetList()).Single();
            var commands = Commands(a);
            var request = new SkillLoadoutRequest { RequestId = Guid.NewGuid(), CharacterId = core.Id!.Value, ExpectedRevision = 0, SkillTrees = [] };
            var receipt = await commands.ConfigureSkills(request);
            Assert.AreEqual(1L, receipt.Revision);
            var replay = await commands.ConfigureSkills(request);
            Assert.AreEqual(receipt.Revision, replay.Revision);
            Assert.HasCount(0, replay.Snapshot.SkillTrees);
            await Assert.ThrowsAsync<BusinessOperationException>(() => characters.Save(core));
            request.ExpectedRevision = 1;
            await Assert.ThrowsAsync<BusinessArgumentException>(() => commands.ConfigureSkills(request));
            var next = new SkillLoadoutRequest { RequestId = Guid.NewGuid(), CharacterId = core.Id.Value, ExpectedRevision = 1,
                SkillTrees = [new() { Kind = CharacterService.CoreDefaultSkillKind }] };
            Assert.AreEqual(2L, (await commands.ConfigureSkills(next)).Revision);
            request.ExpectedRevision = 0;
            Assert.AreEqual(1L, (await commands.ConfigureSkills(request)).Revision);
            Assert.HasCount(1, (await characters.Get(core.Id.Value))!.SkillTrees);
            transaction.Rollback();
        }
        Assert.AreEqual(0, await repository.QueryFirstAsync<int>("SELECT COUNT(*) FROM Player WHERE Id=@owner", new { owner }));
    }

    [TestMethod]
    public async Task Sql_TalentAndProgressSnapshotsFenceLateUnversionedAndStaleWrites()
    {
        using var repository = Repository();
        Guid owner = Guid.NewGuid();
        using var transaction = repository.OpenTransaction();
        var a = await Seed(repository, owner);
        var talents = new TalentService(a);
        await talents.Save(new TalentModel { TalentPoints = 10 });
        var commands = new TalentCommandService(new(a), talents);
        var request = new TalentNodeCommandRequest { RequestId = Guid.NewGuid(), TreeId = "weapon_sword", NodeId = "sword_01" };
        var receipt = await commands.Learn(request);
        Assert.AreEqual(9, receipt.Snapshot.TalentPoints);
        Assert.AreEqual(1L, receipt.Revision);
        Assert.AreEqual(1L, (await commands.Learn(request)).Revision);
        await Assert.ThrowsAsync<BusinessOperationException>(() => talents.Save(new TalentModel { TalentPoints = 10 }));
        await Assert.ThrowsAsync<BusinessOperationException>(() => talents.Save(new TalentModel { ExpectedRevision = 0 }));
        var progress = new GameProgressService(a);
        await progress.Save(new GameProgressModel { Version = 9, Wave = 1 });
        GameProgressModel current = (await progress.Get())!;
        current.ExpectedRevision = 0;
        current.Wave = 2;
        await progress.Save(current);
        Assert.AreEqual(1L, (await progress.Get())!.Revision);
        await Assert.ThrowsAsync<BusinessOperationException>(() => progress.Save(new GameProgressModel { Version = 9, Wave = 1 }));
        Assert.AreEqual(2, (await progress.Get())!.Wave);
        transaction.Rollback();
    }

    [TestMethod]
    public async Task Sql_AtomicAchievementSnapshotClaim_ReplayDoesNotAwardTwice()
    {
        using var repository = Repository();
        Guid owner = Guid.NewGuid();
        using var transaction = repository.OpenTransaction();
        var a = await Seed(repository, owner);
        var commands = Commands(a);
        var request = new AchievementSnapshotClaimRequest
        {
            RequestId = Guid.NewGuid(), AchievementItemId = "test.item",
            Snapshot = new AchievementProgressModel { Groups = [new() { Name = "test.group", Total = 1,
                Items = [new() { Name = "test.item", Target = 1 }] }] },
        };
        var result = await commands.ClaimAchievement(request);
        Assert.IsTrue(result.Snapshot.Groups.Single().Items.Single().IsReward);
        Assert.AreEqual(result.Revision, (await commands.ClaimAchievement(request)).Revision);
        Assert.AreEqual(1, (await new ItemService(a).GetInStockList()).Single().Amount);
        await Assert.ThrowsAsync<BusinessOperationException>(() =>
            new AchievementProgressService(a, new ItemService(a)).Save(new AchievementProgressModel()));
        Assert.IsNull(request.Snapshot.ExpectedRevision, "Command must not mutate the immutable input.");
        transaction.Rollback();
    }

    [TestMethod]
    public async Task Sql_WaveTransitionAcceptsOldOwnedKillsAndDeduplicatesRewards()
    {
        using var repository = Repository();
        Guid owner = Guid.NewGuid();
        using var transaction = repository.OpenTransaction();
        var a = await Seed(repository, owner);
        await new GameProgressService(a).Save(new GameProgressModel { Version = 9, Wave = 1,
            ClearedWaves = [System.Text.Json.JsonSerializer.SerializeToElement(2)] });
        var economy = new EconomyService(a, new GoldService(a));
        var oldSession = await economy.StartWave(new WaveStartRequest { RequestId = Guid.NewGuid(), Wave = 1 });
        var transition = new WaveTransitionRequest { RequestId = Guid.NewGuid(), Wave = 2 };
        var receipt = await Commands(a).TransitionWave(transition);
        Assert.AreEqual(2, receipt.Snapshot.Progress.Wave);
        Assert.AreNotEqual(oldSession.SessionId, receipt.Snapshot.Session.SessionId);
        var kill = new EnemyKillRequest { RequestId = Guid.NewGuid(), SessionId = oldSession.SessionId, EnemyIndex = 0 };
        long gold = (await economy.ReportKill(kill)).Gold;
        Assert.AreEqual(gold, (await economy.ReportKill(kill)).Gold);
        kill.RequestId = Guid.NewGuid();
        await Assert.ThrowsAsync<BusinessArgumentException>(() => economy.ReportKill(kill));
        Assert.AreEqual(receipt.Revision, (await Commands(a).TransitionWave(transition)).Revision);
        transaction.Rollback();
    }

    [TestMethod]
    public async Task Sql_RefineIsSingleDebitReplay_AndDisassemblySkipsEquippedAndMissing()
    {
        using var repository = Repository();
        Guid owner = Guid.NewGuid();
        using var transaction = repository.OpenTransaction();
        var a = await Seed(repository, owner);
        var weapons = new WeaponService(a);
        await weapons.Save(new WeaponInstanceModel { Id = "refine-test", WeaponId = "sword", Rarity = "common" });
        var items = new ItemService(a);
        await items.AdjustAmount("awakening_crystal", 2, "refine");
        var commands = new WeaponCommandService(a, new(a), weapons, items, new(a));
        var request = new WeaponRefineRequest { RequestId = Guid.NewGuid(), WeaponEntryId = "refine-test", CrystalId = "awakening_crystal" };
        var receipt = await commands.Refine(request);
        Assert.AreEqual(1, receipt.Snapshot.MaterialCount);
        Assert.AreEqual("fine", receipt.Snapshot.Weapon.Rarity);
        Assert.AreEqual(1, (await commands.Refine(request)).Snapshot.MaterialCount);
        Assert.AreEqual(1, (await items.GetInStockList()).Single().Amount);
        await Assert.ThrowsAsync<BusinessOperationException>(() =>
            weapons.Save(new WeaponInstanceModel { Id = "refine-test", WeaponId = "sword", Rarity = "common" }));
        await Assert.ThrowsAsync<BusinessOperationException>(() =>
            weapons.Save(new WeaponInstanceModel { Id = "REFINE-TEST", WeaponId = "sword", Rarity = "common" }));
        var core = (await new CharacterService(a).GetList()).Single();
        await new CharacterService(a).EquipWeapon(new WeaponEquipRequest { RequestId = Guid.NewGuid(), CharacterId = core.Id!.Value, WeaponEntryId = "refine-test" });
        var disassemble = new WeaponDisassembleRequest { RequestId = Guid.NewGuid(),
            WeaponEntryIds = ["refine-test", "missing", "refine-test"] };
        var result = await commands.Disassemble(disassemble);
        Assert.HasCount(0, result.Snapshot.RemovedIds);
        Assert.HasCount(2, result.Snapshot.SkippedIds);
        Assert.HasCount(1, result.Snapshot.Weapons);
        await new CharacterService(a).EquipWeapon(new WeaponEquipRequest
        {
            RequestId = Guid.NewGuid(), CharacterId = core.Id.Value, WeaponEntryId = null,
        });
        var remove = new WeaponDisassembleRequest
        {
            RequestId = Guid.NewGuid(), WeaponEntryIds = ["refine-test"],
        };
        var removed = await commands.Disassemble(remove);
        Assert.HasCount(1, removed.Snapshot.RemovedIds);
        Assert.HasCount(0, removed.Snapshot.Weapons);
        Assert.AreEqual(removed.Revision, (await commands.Disassemble(remove)).Revision);
        await Assert.ThrowsAsync<BusinessOperationException>(() => weapons.Save(new WeaponInstanceModel
        {
            Id = "refine-test", WeaponId = "sword", Rarity = "fine", ExpectedRevision = receipt.Revision,
        }));
        Assert.AreEqual(1, (await items.GetInStockList()).Single().Amount, "Disassembly grants no extra material.");
        transaction.Rollback();
    }

    [TestMethod]
    public async Task Sql_AcquireReplayAfterDisassembly_DoesNotResurrect_AndDifferentIntentConflicts()
    {
        using var repository = Repository();
        using var transaction = repository.OpenTransaction();
        var a = await Seed(repository, Guid.NewGuid());
        var weapons = new WeaponService(a);
        var commands = new WeaponCommandService(a, new(a), weapons, new(a), new(a));
        var request = new WeaponAcquireRequest
        {
            RequestId = Guid.NewGuid(),
            Weapon = new() { Id = "drop-once", WeaponId = "staff", Rarity = "common" },
        };
        var acquired = await commands.Acquire(request);
        Assert.AreEqual(acquired.Revision, (await commands.Acquire(request)).Revision);
        await commands.Disassemble(new() { RequestId = Guid.NewGuid(), WeaponEntryIds = ["drop-once"] });
        Assert.HasCount(0, (await weapons.GetInstanceList()).ToList());
        Assert.AreEqual(acquired.Revision, (await commands.Acquire(request)).Revision);
        Assert.HasCount(0, (await weapons.GetInstanceList()).ToList(), "Replay never reruns the grant.");
        await Assert.ThrowsAsync<BusinessArgumentException>(() => commands.Acquire(new()
        {
            RequestId = Guid.NewGuid(), Weapon = request.Weapon,
        }));
        request.Weapon.Rarity = "fine";
        await Assert.ThrowsAsync<BusinessArgumentException>(() => commands.Acquire(request));
        transaction.Rollback();
    }

    [TestMethod]
    public async Task Sql_EquipPreservesSkills_LegacySkillSaveCannotChangeEquipment()
    {
        using var repository = Repository();
        using var transaction = repository.OpenTransaction();
        var a = await Seed(repository, Guid.NewGuid());
        var characters = new CharacterService(a);
        var commands = new WeaponCommandService(a, new(a), new(a), new(a), characters);
        await commands.Acquire(new()
        {
            RequestId = Guid.NewGuid(), Weapon = new() { Id = "confirmed", WeaponId = "staff", Rarity = "common" },
        });
        var core = (await characters.GetList()).Single();
        var equipped = await characters.EquipWeapon(new()
        {
            RequestId = Guid.NewGuid(), CharacterId = core.Id!.Value, WeaponEntryId = "confirmed",
        });
        Assert.AreEqual(core.SkillTrees.Single().Kind, equipped.Snapshot.SkillTrees.Single().Kind);
        var skills = equipped.Snapshot;
        skills.WeaponEntryId = "stale-client-reference";
        skills.SkillTrees = [];
        skills.ExpectedRevision = equipped.Revision;
        await characters.Save(skills);
        var current = (await characters.Get(core.Id.Value))!;
        Assert.AreEqual("confirmed", current.WeaponEntryId);
        Assert.HasCount(0, current.SkillTrees);
        var unequipped = await characters.EquipWeapon(new()
        {
            RequestId = Guid.NewGuid(), CharacterId = core.Id.Value, WeaponEntryId = null,
        });
        Assert.IsTrue(string.IsNullOrEmpty(unequipped.Snapshot.WeaponEntryId));
        Assert.HasCount(0, unequipped.Snapshot.SkillTrees);
        transaction.Rollback();
    }
}
