using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Tests.Services
{
    [TestClass]
    public class SnapshotSaveTests
    {
        private static readonly Guid PlayerId = Guid.NewGuid();

        [TestMethod]
        public async Task Weapon_Insert_UsesOwnedNaturalKey_AndMapsFields()
        {
            var (argument, repository) = CreateArgument();
            await new WeaponService(argument).Save(WeaponModel());

            Assert.Contains("Player WITH (UPDLOCK, HOLDLOCK)", repository.Calls[0].Sql!);
            Assert.AreEqual("QueryFirstOrDefaultAsync", repository.Calls[2].Name);
            Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", repository.Calls[2].Sql!);
            Assert.Contains("Status != -1", repository.Calls[2].Sql!);
            Assert.Contains("EntryKey = @NaturalKey", repository.Calls[2].Sql!);
            Assert.AreEqual(PlayerId, Property(repository.Calls[2].Value!, "CreateUserId"));
            Assert.AreEqual("entry", Property(repository.Calls[2].Value!, "NaturalKey"));
            Weapon row = (Weapon)repository.Calls.Single(c => c.Name == "InsertAsync").Value!;
            Assert.AreEqual(PlayerId, row.CreateUserId);
            Assert.AreEqual("entry", row.EntryKey);
            Assert.AreEqual("weapon", row.WeaponKey);
            Assert.AreEqual("family", row.WeaponFamilyKey);
            Assert.AreEqual("rare", row.Rarity);
            Assert.IsFalse(repository.Calls.Any(c => c.Name == "ExecuteAsync"));
        }

        [TestMethod]
        public async Task Weapon_Update_PreservesLimitedUpdate_ThenDeletesChildren()
        {
            var (argument, repository) = CreateArgument();
            Weapon existing = new() { Id = Guid.NewGuid(), EntryKey = "entry", CreateUserId = PlayerId };
            repository.Existing = existing;
            await new WeaponService(argument).Save(WeaponModel());

            Assert.AreEqual("ExecuteAsync", repository.Calls[3].Name);
            string update = repository.Calls[3].Sql!;
            Assert.Contains("WeaponKey = @WeaponKey", update);
            Assert.Contains("AND CreateUserId = @CreateUserId", update);
            Assert.DoesNotContain("Status =", update);
            Assert.AreEqual(existing.Id, Property(repository.Calls[3].Value!, "Id"));
            Assert.IsTrue(repository.Calls.Skip(4).All(c => c.Sql!.StartsWith("DELETE")));
            Assert.IsFalse(repository.Calls.Any(c => c.Name is "UpdateAsync" or "InsertAsync"));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Talent_UpsertsParent_AndReplacesChildrenOnlyOnUpdate(bool exists)
        {
            var (argument, repository) = CreateArgument();
            if (exists) { repository.Existing = new Talent { Id = Guid.NewGuid(), CreateUserId = PlayerId }; }
            await new TalentService(argument).Save(new TalentModel
            {
                Version = 2, TalentPoints = 3, TotalSpentPoints = 4,
                AcquiredTalents = new() { ["tree"] = new() { ["node"] = JsonSerializer.SerializeToElement(2) } },
                LegacyCategoryTalents = new() { ["legacy"] = new() { ["node"] = JsonSerializer.SerializeToElement(true) } },
            });

            Assert.DoesNotContain("@NaturalKey", repository.Calls[2].Sql!);
            Assert.AreEqual(exists ? "UpdateAsync" : "InsertAsync", repository.Calls[3].Name);
            Talent row = (Talent)repository.Calls[3].Value!;
            Assert.AreEqual(PlayerId, row.CreateUserId);
            Assert.AreEqual(2, row.Version);
            Assert.AreEqual(3, row.TalentPoints);
            Assert.AreEqual(4, row.TotalSpentPoints);
            Assert.AreEqual(exists ? 2 : 0, repository.Calls.Count(c => c.Name == "ExecuteAsync"));
            foreach (var call in repository.Calls.Where(c => c.Name == "ExecuteAsync"))
            {
                Assert.AreEqual(row.Id, Property(call.Value!, "ParentId"));
                Assert.AreEqual(PlayerId, Property(call.Value!, "CreateUserId"));
            }
            TalentAcquired acquired = repository.Calls.Select(c => c.Value).OfType<TalentAcquired>().Single();
            TalentLegacyCategory legacy = repository.Calls.Select(c => c.Value).OfType<TalentLegacyCategory>().Single();
            Assert.AreEqual(row.Id, acquired.TalentId);
            Assert.AreEqual(row.Id, legacy.TalentId);
            Assert.AreEqual("2", acquired.Value);
            Assert.AreEqual("true", legacy.Value);
            Assert.AreEqual(PlayerId, acquired.CreateUserId);
            Assert.AreEqual(PlayerId, legacy.CreateUserId);
            Assert.AreEqual("InsertAsync", repository.Calls[^2].Name);
            Assert.AreEqual("InsertAsync", repository.Calls[^1].Name);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Weapon_WritesChildrenAfterParent_WithCorrectOwnershipAndOrder(bool exists)
        {
            var (argument, repository) = CreateArgument();
            if (exists) { repository.Existing = new Weapon { Id = Guid.NewGuid(), EntryKey = "entry", CreateUserId = PlayerId }; }
            WeaponInstanceModel model = WeaponModel();
            model.Affixes = [JsonSerializer.SerializeToElement(new { id = "affix", values = new[] { 3, 4 } })];
            model.Unique = new()
            {
                ["unique_id"] = JsonSerializer.SerializeToElement("unique"),
                ["values"] = JsonSerializer.SerializeToElement(new[] { 5 }),
            };
            await new WeaponService(argument).Save(model);

            int childIndex = repository.Calls.FindIndex(c => c.Value is WeaponAffix);
            Assert.IsGreaterThan(1, childIndex);
            Assert.IsTrue(repository.Calls.Skip(4).Take(childIndex - 4).All(c => c.Sql!.StartsWith("DELETE")));
            Weapon parent = exists ? (Weapon)repository.Existing! : (Weapon)repository.Calls[3].Value!;
            Assert.AreEqual("unique", parent.UniqueKey);
            WeaponAffix affix = (WeaponAffix)repository.Calls[childIndex].Value!;
            Assert.AreEqual(parent.Id, affix.WeaponId);
            Assert.AreEqual("affix", affix.AffixKey);
            Assert.IsTrue(affix.Rolled);
            List<WeaponAffixValue> values = repository.Calls.Select(c => c.Value).OfType<WeaponAffixValue>().ToList();
            Assert.AreEqual("3", values[0].Value);
            Assert.AreEqual("4", values[1].Value);
            Assert.AreEqual(affix.Id, values[0].WeaponAffixId);
            Assert.AreEqual(1, values[1].Sequence);
            WeaponUniqueValue unique = repository.Calls.Select(c => c.Value).OfType<WeaponUniqueValue>().Single();
            Assert.AreEqual(parent.Id, unique.WeaponId);
            Assert.AreEqual("5", unique.Value);
            Assert.IsTrue(repository.Calls.Where(c => c.Name == "InsertAsync")
                .Select(c => c.Value).OfType<ModelBase>().All(row => row.CreateUserId == PlayerId));
        }

        [TestMethod]
        public async Task InvalidSnapshots_DoNotAccessRepository()
        {
            var (argument, repository) = CreateArgument();
            await Assert.ThrowsAsync<ArgumentException>(() => new WeaponService(argument).Save(new WeaponInstanceModel()));
            WeaponInstanceModel invalidAffix = WeaponModel();
            invalidAffix.Affixes.Add(JsonSerializer.SerializeToElement(1));
            await Assert.ThrowsAsync<FormatException>(() => new WeaponService(argument).Save(invalidAffix));
            await Assert.ThrowsAsync<ArgumentException>(() => new TalentService(argument).Save(new TalentModel { TalentPoints = -1 }));
            await Assert.ThrowsAsync<ArgumentException>(() => new GameProgressService(argument).Save(new GameProgressModel
            {
                DeploymentLog = [new GameProgressDeploymentModel()],
            }));
            await Assert.ThrowsAsync<ArgumentException>(() => new AchievementProgressService(argument, new ItemService(argument))
                .Save(new AchievementProgressModel { Groups = [new AchievementGroupModel { Name = "" }] }));
            Assert.HasCount(0, repository.Calls);
        }

        [TestMethod]
        public async Task GameProgress_LocksPlayerBeforeDeletingOwnedRows()
        {
            var (argument, repository) = CreateArgument();
            await new GameProgressService(argument).Save(new GameProgressModel());
            Assert.AreEqual("QueryAsync", repository.Calls[0].Name);
            Assert.Contains("Player WITH (UPDLOCK, HOLDLOCK)", repository.Calls[0].Sql!);
            Assert.AreEqual(PlayerId, Property(repository.Calls[0].Value!, "PlayerId"));
            var deletion = repository.Calls.First(call => call.Name == "ExecuteAsync");
            Assert.Contains("CreateUserId = @CreateUserId", deletion.Sql!);
            foreach (var write in repository.Calls.Where(call => call.Name == "ExecuteAsync"))
            {
                Assert.DoesNotContain("DELETE FROM Character", write.Sql!);
                Assert.DoesNotContain("INSERT INTO Character", write.Sql!);
                Assert.DoesNotContain("UPDATE Character", write.Sql!);
            }
        }

        [TestMethod]
        public async Task Character_NotDeployed_SavesEquipmentAndSkillTree()
        {
            var (argument, repository) = CreateArgument();
            repository.QueryRows[typeof(Guid)] = new[] { Guid.NewGuid() };
            Character existing = new() { Kind = 0, CreateUserId = PlayerId };
            repository.Existing = existing;
            repository.QueryRows[typeof(Character)] = new[] { existing };
            repository.QueryRows[typeof(PlayerParams)] = new[]
            {
                new PlayerParams { Group = "game_progress.unlocked_auxiliary_kinds", Name = "auxiliary_gem", Value = "true" },
            };
            CharacterModel model = new()
            {
                Id = existing.Id, Kind = 0, WeaponEntryId = "entry",
                SkillTrees = [new GameProgressSkillLoadoutModel
                {
                    Kind = 5, AuxiliaryNodes =
                    [
                        new GameProgressSkillNodeModel { Type = "active", Kind = 0 },
                        new GameProgressSkillNodeModel { Type = "auxiliary", LootId = "auxiliary_gem" },
                    ],
                }],
            };
            await new CharacterService(argument).Save(model);
            Character row = repository.Calls.Select(call => call.Value).OfType<Character>().Single();
            Assert.AreEqual(row.Id, model.Id);
            Assert.AreEqual(PlayerId, row.CreateUserId);
            Assert.IsNull(row.WeaponEntryKey, "Generic character saves must not equip a client-supplied weapon.");
            CharacterSkillTree tree = BatchRows<CharacterSkillTree>(repository, "Trees").Single();
            Assert.AreEqual(row.Id, tree.CharacterId);
            var nodes = BatchRows<CharacterSkillNode>(repository, "Nodes");
            Assert.HasCount(2, nodes);
            Assert.IsTrue(nodes.All(node => node.CharacterSkillTreeId == tree.Id));
            Assert.AreEqual(PlayerId, Property(repository.Calls.Single(call => call.Name == "ExecuteAsync").Value!, "CreateUserId"));
            Assert.AreEqual(1, nodes[1].Sequence);
            Assert.IsFalse(repository.Calls.Any(call => call.Sql?.Contains("Deployment") == true));
        }

        [TestMethod]
        public async Task Character_Update_PreservesId_AndReplacesOnlyOwnSkills()
        {
            var (argument, repository) = CreateArgument();
            Character existing = new() { Kind = 3, CreateUserId = PlayerId, WeaponEntryKey = "confirmed" };
            repository.Existing = existing;
            repository.QueryRows[typeof(Character)] = new[] { existing };
            repository.QueryRows[typeof(Guid)] = new[] { Guid.NewGuid() };
            CharacterModel model = new() { Id = existing.Id, Kind = 3, WeaponEntryId = "stale" };
            await new CharacterService(argument).Save(model);
            Assert.AreEqual(existing.Id, model.Id);
            Assert.AreEqual("confirmed", existing.WeaponEntryKey, "A stale skill snapshot cannot replace equipment.");
            Assert.AreEqual(3, existing.Kind);
            var write = repository.Calls.Single(call => call.Name == "ExecuteAsync");
            Assert.Contains("DELETE FROM CharacterSkillNode WHERE CreateUserId = @CreateUserId", write.Sql!);
            Assert.Contains("DELETE FROM CharacterSkillTree WHERE CharacterId = @CharacterId AND CreateUserId = @CreateUserId", write.Sql!);
            Assert.AreEqual(existing.Id, Property(write.Value!, "CharacterId"));
            Assert.IsTrue((bool)Property(write.Value!, "ReplaceChildren")!);
            Assert.DoesNotContain("Deployment", write.Sql!);
        }

        [TestMethod]
        [DataRow("missing-character")]
        [DataRow("changed-kind")]
        [DataRow("missing-weapon")]
        [DataRow("equipped-weapon")]
        public async Task Character_InvalidReferences_RejectBeforeWrites(string reason)
        {
            var (argument, repository) = CreateArgument();
            Character existing = new() { Kind = 3, CreateUserId = PlayerId };
            repository.Existing = existing;
            repository.QueryRows[typeof(Character)] = new[] { existing };
            CharacterModel model = new() { Id = existing.Id, Kind = 3 };
            if (reason is "missing-character" or "changed-kind")
            {
                model.Id = Guid.NewGuid();
                repository.QueryRows[typeof(Character)] = Array.Empty<Character>();
                if (reason == "changed-kind")
                {
                    repository.QueryRows[typeof(Character)] = new[] { new Character { Id = model.Id.Value, Kind = 4 } };
                }
            }
            else
            {
                existing.WeaponEntryKey = "entry";
                model.WeaponEntryId = "entry";
                if (reason == "equipped-weapon")
                {
                    repository.QueryRows[typeof(Guid)] = new[] { Guid.NewGuid() };
                    repository.QueryRows[typeof(int)] = new[] { 4 };
                }
            }
            await Assert.ThrowsAsync<ArgumentException>(() => new CharacterService(argument).Save(model));
            Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
            Assert.IsTrue(repository.Calls.All(call => call.Sql!.Contains("@CreateUserId") || call.Sql.Contains("@PlayerId")));
        }

        [TestMethod]
        [DataRow("unknown", null, null)]
        [DataRow("active", null, null)]
        [DataRow("auxiliary", null, null)]
        public async Task Character_InvalidSkillNode_RejectsBeforeRepository(string type, string? lootId, int? kind)
        {
            var (argument, repository) = CreateArgument();
            CharacterModel model = new()
            {
                Id = Guid.NewGuid(),
                SkillTrees = [new GameProgressSkillLoadoutModel
                {
                    AuxiliaryNodes = [new GameProgressSkillNodeModel { Type = type, LootId = lootId, Kind = kind }],
                }],
            };
            await Assert.ThrowsAsync<ArgumentException>(() => new CharacterService(argument).Save(model));
            Assert.HasCount(0, repository.Calls);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task GameProgress_ValidatesOwnedCharacterBeforeReplacingDeployments(bool exists)
        {
            var (argument, repository) = CreateArgument();
            Guid id = Guid.NewGuid();
            if (exists) { repository.QueryRows[typeof(Guid)] = new[] { id }; }
            var model = new GameProgressModel { RequestId = Guid.NewGuid(), DeploymentLog = [new GameProgressDeploymentModel { CharacterId = id }] };
            if (!exists)
            {
                await Assert.ThrowsAsync<ArgumentException>(() => new GameProgressService(argument).Save(model));
                Assert.IsFalse(repository.Calls.Any(call => call.Name == "ExecuteAsync"));
                return;
            }
            await new GameProgressService(argument).Save(model);
            var row = BatchRows<Deployment>(repository, "Deployments").Single();
            Assert.AreEqual(id, row.CharacterId);
            Assert.AreEqual(PlayerId, Property(repository.Calls.Single(call => call.Name == "ExecuteAsync").Value!, "CreateUserId"));
            Assert.Contains("CreateUserId = @CreateUserId", repository.Calls[3].Sql!);
        }

        [TestMethod]
        public async Task Character_GetList_UsesCurrentWeaponAndIndependentSkills()
        {
            var (argument, repository) = CreateArgument();
            Character row = new() { Kind = 3, WeaponEntryKey = "entry" };
            CharacterSkillTree tree = new() { CharacterId = row.Id, Kind = 7 };
            repository.QueryRows[typeof(Character)] = new[] { row };
            repository.QueryRows[typeof(CharacterSkillTree)] = new[] { tree };
            repository.QueryRows[typeof(CharacterSkillNode)] = new[]
            {
                new CharacterSkillNode { CharacterSkillTreeId = tree.Id, NodeType = "active", Kind = 8 },
            };
            repository.QueryRows[typeof(Weapon)] = new[] { new Weapon { EntryKey = "entry", WeaponKey = "updated", Rarity = "rare" } };
            argument.LazyServiceArgument.WeaponService = new Lazy<WeaponService>(() => new WeaponService(argument));
            CharacterModel model = (await new CharacterService(argument).GetList()).Single();
            Assert.AreEqual("updated", model.WeaponId);
            Assert.AreEqual(8, model.SkillTrees[0].AuxiliaryNodes[0].Kind);
        }

        [TestMethod]
        public async Task Achievement_LocksGroupsBeforeReadingClaims_AndPreservesServerRewardState()
        {
            var (argument, repository) = CreateArgument();
            DateTime rewardDate = new(2026, 1, 1);
            repository.QueryRows[typeof(AchievementDetail)] = new[]
            {
                new AchievementDetail { Name = "claimed", IsReward = true, RewardDate = rewardDate },
            };
            await new AchievementProgressService(argument, new ItemService(argument)).Save(new AchievementProgressModel
            {
                Groups = [new AchievementGroupModel
                {
                    Name = "group", Total = 10,
                    Items =
                    [
                        new AchievementItemModel { Name = "claimed", Target = 1, IsReward = false },
                        new AchievementItemModel { Name = "unclaimed", Target = 1, IsReward = true, RewardDate = rewardDate },
                    ],
                }],
            });

            Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", repository.Calls[0].Sql!);
            Assert.Contains("AchievementDetail", repository.Calls[3].Sql!);
            var write = repository.Calls.Single(c => c.Name == "ExecuteAsync");
            Assert.Contains("DELETE FROM AchievementCollected", write.Sql!);
            Assert.Contains("DELETE FROM AchievementDetail", write.Sql!);
            Assert.Contains("DELETE FROM Achievement WHERE", write.Sql!);
            List<AchievementDetail> details = BatchRows<AchievementDetail>(repository, "Details");
            Assert.IsTrue(details[0].IsReward);
            Assert.AreEqual(rewardDate, details[0].RewardDate);
            Assert.IsFalse(details[1].IsReward);
            Assert.IsNull(details[1].RewardDate);
            Assert.AreEqual(PlayerId, Property(write.Value!, "CreateUserId"));
        }

        [TestMethod]
        public async Task RepositoryFailure_IsPropagated_WithoutWritingChildren()
        {
            var (argument, repository) = CreateArgument();
            repository.FailInsert = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => new WeaponService(argument).Save(WeaponModel()));
            Assert.HasCount(4, repository.Calls);
        }

        private static WeaponInstanceModel WeaponModel() => new()
        {
            Id = "entry", WeaponId = "weapon", WeaponFamilyId = "family", Rarity = "rare",
        };

        private static object? Property(object value, string name) => value is Dapper.DynamicParameters parameters
            ? parameters.Get<object?>(name) : value.GetType().GetProperty(name)!.GetValue(value);

        internal static List<T> BatchRows<T>(RecordingRepository repository, string parameter)
        {
            object arguments = repository.Calls.Single(call =>
                call.Name == "ExecuteAsync" && call.Value?.GetType().GetProperty(parameter) != null).Value!;
            return JsonSerializer.Deserialize<List<T>>((string)Property(arguments, parameter)!)!;
        }

        private static Lazy<T> UnusedLazy<T>() => new(() => throw new InvalidOperationException($"Unexpected dependency: {typeof(T).Name}"));

        internal static (BaseServiceArgument, RecordingRepository) CreateArgument()
        {
            IDapperRepository repository = DispatchProxy.Create<IDapperRepository, RecordingRepository>();
            return (CreateServiceArgument(repository, PlayerId), (RecordingRepository)repository);
        }

        internal static BaseServiceArgument CreateServiceArgument(IDapperRepository repository, Guid playerId)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["SmtpConfig:Password"] = "test-only" }).Build();
            ConstructorInfo constructor = typeof(LazyServiceArgument).GetConstructors().Single();
            MethodInfo unused = typeof(SnapshotSaveTests).GetMethod(nameof(UnusedLazy), BindingFlags.NonPublic | BindingFlags.Static)!;
            object?[] dependencies = constructor.GetParameters().Select(p =>
                unused.MakeGenericMethod(p.ParameterType.GetGenericArguments()[0]).Invoke(null, null)).ToArray();
            var lazy = (LazyServiceArgument)constructor.Invoke(dependencies);
            lazy.MailSender = new Lazy<MailSender>(() => new MailSender(configuration));
            var argument = new BaseServiceArgument(lazy, repository, configuration);
            lazy.PlayerService = new Lazy<PlayerService>(() => new PlayerService(argument) { Player = new Player { Id = playerId } });
            lazy.WeaponService = new Lazy<WeaponService>(() => new WeaponService(argument));
            lazy.GoldService = new Lazy<GoldService>(() => new GoldService(argument));
            return argument;
        }

        public class RecordingRepository : DispatchProxy
        {
            public List<(string Name, string? Sql, object? Value)> Calls { get; } = new();
            public object? Existing { get; set; }
            public Dictionary<Type, object> FirstRows { get; } = new();
            public Dictionary<Type, object> QueryRows { get; } = new();
            public bool FailInsert { get; set; }
            public bool FailExecute { get; set; }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                ArgumentNullException.ThrowIfNull(args);
                string name = targetMethod.Name;
                bool query = name is "QueryAsync" or "QueryFirstOrDefaultAsync" or "ExecuteAsync";
                Calls.Add((name, query ? (string)args[0]! : null, query ? args[1] : args[0]));
                if (name == "InsertAsync" && FailInsert) { throw new InvalidOperationException("Insert failed"); }
                if (name == "ExecuteAsync" && FailExecute) { throw new InvalidOperationException("Execute failed"); }
                object? result = name switch
                {
                    "QueryFirstOrDefaultAsync" => FirstRows.TryGetValue(targetMethod.GetGenericArguments()[0], out object? first)
                        ? first : Existing != null && targetMethod.GetGenericArguments()[0].IsInstanceOfType(Existing) ? Existing : null,
                    "QueryAsync" => QueryRows.TryGetValue(targetMethod.GetGenericArguments()[0], out object? rows)
                        ? rows : Array.CreateInstance(targetMethod.GetGenericArguments()[0], 0),
                    "InsertAsync" or "ExecuteAsync" => 1,
                    "UpdateAsync" => true,
                    _ => throw new InvalidOperationException($"Unexpected repository call: {name}"),
                };
                return typeof(RecordingRepository).GetMethod(nameof(Result), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(targetMethod.ReturnType.GetGenericArguments()[0]).Invoke(null, [result]);
            }

            private static Task<T> Result<T>(object? value) => Task.FromResult(value == null ? default! : (T)value);
        }
    }
}
