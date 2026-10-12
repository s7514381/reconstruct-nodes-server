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

            Assert.AreEqual("QueryFirstOrDefaultAsync", repository.Calls[0].Name);
            Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", repository.Calls[0].Sql!);
            Assert.Contains("Status != -1", repository.Calls[0].Sql!);
            Assert.Contains("EntryKey = @NaturalKey", repository.Calls[0].Sql!);
            Assert.AreEqual(PlayerId, Property(repository.Calls[0].Value!, "CreateUserId"));
            Assert.AreEqual("entry", Property(repository.Calls[0].Value!, "NaturalKey"));
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

            Assert.AreEqual("ExecuteAsync", repository.Calls[1].Name);
            string update = repository.Calls[1].Sql!;
            Assert.Contains("WeaponKey = @WeaponKey", update);
            Assert.Contains("AND CreateUserId = @CreateUserId", update);
            Assert.DoesNotContain("Status =", update);
            Assert.AreEqual(existing.Id, Property(repository.Calls[1].Value!, "Id"));
            Assert.IsTrue(repository.Calls.Skip(2).All(c => c.Sql!.StartsWith("DELETE")));
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

            Assert.DoesNotContain("@NaturalKey", repository.Calls[0].Sql!);
            Assert.AreEqual(exists ? "UpdateAsync" : "InsertAsync", repository.Calls[1].Name);
            Talent row = (Talent)repository.Calls[1].Value!;
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
            Assert.IsTrue(repository.Calls.Skip(2).Take(childIndex - 2).All(c => c.Sql!.StartsWith("DELETE")));
            Weapon parent = exists ? (Weapon)repository.Existing! : (Weapon)repository.Calls[1].Value!;
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
                CharacterWeapons = new() { ["not-an-integer"] = new GameProgressEquipmentModel() },
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
            Assert.AreEqual("ExecuteAsync", repository.Calls[1].Name);
            Assert.Contains("CreateUserId = @CreateUserId", repository.Calls[1].Sql!);
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
            Assert.Contains("AchievementDetail", repository.Calls[1].Sql!);
            Assert.IsTrue(repository.Calls.Skip(2).Take(3).All(c => c.Sql!.StartsWith("DELETE")));
            List<AchievementDetail> details = repository.Calls.Where(c => c.Name == "InsertAsync")
                .Select(c => c.Value).OfType<AchievementDetail>().ToList();
            Assert.IsTrue(details[0].IsReward);
            Assert.AreEqual(rewardDate, details[0].RewardDate);
            Assert.IsFalse(details[1].IsReward);
            Assert.IsNull(details[1].RewardDate);
            Assert.IsTrue(details.All(d => d.CreateUserId == PlayerId));
        }

        [TestMethod]
        public async Task RepositoryFailure_IsPropagated_WithoutWritingChildren()
        {
            var (argument, repository) = CreateArgument();
            repository.FailInsert = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => new WeaponService(argument).Save(WeaponModel()));
            Assert.HasCount(2, repository.Calls);
        }

        private static WeaponInstanceModel WeaponModel() => new()
        {
            Id = "entry", WeaponId = "weapon", WeaponFamilyId = "family", Rarity = "rare",
        };

        private static object? Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);

        private static Lazy<T> UnusedLazy<T>() => new(() => throw new InvalidOperationException($"Unexpected dependency: {typeof(T).Name}"));

        private static (BaseServiceArgument, RecordingRepository) CreateArgument()
        {
            IDapperRepository repository = DispatchProxy.Create<IDapperRepository, RecordingRepository>();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["SmtpConfig:Password"] = "test-only" }).Build();
            ConstructorInfo constructor = typeof(LazyServiceArgument).GetConstructors().Single();
            MethodInfo unused = typeof(SnapshotSaveTests).GetMethod(nameof(UnusedLazy), BindingFlags.NonPublic | BindingFlags.Static)!;
            object?[] dependencies = constructor.GetParameters().Select(p =>
                unused.MakeGenericMethod(p.ParameterType.GetGenericArguments()[0]).Invoke(null, null)).ToArray();
            var lazy = (LazyServiceArgument)constructor.Invoke(dependencies);
            lazy.MailSender = new Lazy<MailSender>(() => new MailSender(configuration));
            var argument = new BaseServiceArgument(lazy, repository, configuration);
            lazy.PlayerService = new Lazy<PlayerService>(() => new PlayerService(argument) { Player = new Player { Id = PlayerId } });
            return (argument, (RecordingRepository)repository);
        }

        public class RecordingRepository : DispatchProxy
        {
            public List<(string Name, string? Sql, object? Value)> Calls { get; } = new();
            public object? Existing { get; set; }
            public Dictionary<Type, object> QueryRows { get; } = new();
            public bool FailInsert { get; set; }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                ArgumentNullException.ThrowIfNull(args);
                string name = targetMethod.Name;
                bool query = name is "QueryAsync" or "QueryFirstOrDefaultAsync" or "ExecuteAsync";
                Calls.Add((name, query ? (string)args[0]! : null, query ? args[1] : args[0]));
                if (name == "InsertAsync" && FailInsert) { throw new InvalidOperationException("Insert failed"); }
                object? result = name switch
                {
                    "QueryFirstOrDefaultAsync" => Existing,
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
