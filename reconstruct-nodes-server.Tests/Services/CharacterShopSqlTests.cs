using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Tests.Services
{
    [TestClass]
    [TestCategory("CharacterShopSql")]
    [DoNotParallelize]
    public class CharacterShopSqlTests
    {
        private class FailingShardService(BaseServiceArgument argument) : ItemService(argument)
        {
            public override Task<int> AdjustAmount(string key, int delta, string? category = null)
                => throw new InvalidOperationException("Injected shard failure");
        }

        private class FixedShop(BaseServiceArgument argument, GoldService gold, ItemService items, int index)
            : ShopService(argument, gold, items)
        {
            protected override int DrawIndex() => index;
        }

        private DapperRepository CreateRepository()
        {
            if (Environment.GetEnvironmentVariable("CHARACTER_SHOP_SQL_TESTS") != "1")
            {
                Assert.Inconclusive("需要明確設定 CHARACTER_SHOP_SQL_TESTS=1 與 CHARACTER_SHOP_SQL_CONNECTION。");
            }
            string connection = Environment.GetEnvironmentVariable("CHARACTER_SHOP_SQL_CONNECTION")
                ?? throw new InvalidOperationException("缺少測試資料庫連線");
            return new DapperRepository(new DapperDBContext(connection),
                new DbConnectionLimitService(2, 2), new DbOperateLimitService(2, 2));
        }

        [TestMethod]
        public async Task RealDatabase_DrawReplay_CharacterSkillsAndDeployment_PreserveGold()
        {
            using var repository = CreateRepository();
            Guid playerId = Guid.NewGuid();
            using (var transaction = repository.OpenTransaction())
            {
                await repository.InsertAsync(new Player { Id = playerId, Name = "CharacterShopSqlTest", IsClientLogin = true });
                var argument = SnapshotSaveTests.CreateServiceArgument(repository, playerId);
                var shop = new ShopService(argument, new GoldService(argument), new ItemService(argument));
                var request = new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = 1 };
                ShopDrawResult draw = await shop.Draw(request);
                Assert.AreEqual(1100, draw.Gold);
                ShopDrawResult replay = await shop.Draw(request);
                Assert.AreEqual(draw.Results[0].CharacterId, replay.Results[0].CharacterId);
                Assert.AreEqual(1, await repository.QueryFirstAsync<int>(
                    "SELECT COUNT(*) FROM Character WHERE CreateUserId=@playerId", new { playerId }));
                Assert.AreEqual(1, await repository.QueryFirstAsync<int>(
                    "SELECT COUNT(*) FROM Weapon WHERE CreateUserId=@playerId", new { playerId }));

                var characters = new CharacterService(argument);
                CharacterModel character = (await characters.GetList()).Single();
                character.SkillTrees = [new GameProgressSkillLoadoutModel
                {
                    Kind = 0, AuxiliaryNodes = [new GameProgressSkillNodeModel { Type = "active", Kind = 0 }],
                }];
                await characters.Save(character);
                var progress = new GameProgressService(argument);
                await progress.Save(new GameProgressModel
                {
                    RequestId = Guid.NewGuid(), Version = 9, Gold = 999999,
                    DeploymentLog = [new GameProgressDeploymentModel { CharacterId = character.Id!.Value }],
                });
                Assert.AreEqual(1040, await new GoldService(argument).GetBalance());
                var restored = await progress.Get();
                Assert.IsNotNull(restored);
                Assert.AreEqual(character.Id, restored.DeploymentLog.Single().CharacterId);
                Assert.AreEqual(0, (await characters.GetList()).Single().SkillTrees.Single().AuxiliaryNodes.Single().Kind);

                await characters.Save(character);
                Assert.AreEqual(character.Kind, (await characters.Get(character.Id.Value))!.Kind);
                await progress.Save(new GameProgressModel { Version = 9 });
                Assert.HasCount(1, await characters.GetList());
                Assert.AreEqual(1040, await new GoldService(argument).GetBalance());
                transaction.Rollback();
            }
            Assert.AreEqual(0, await repository.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM Player WHERE Id=@playerId", new { playerId }));
            foreach (string table in new[] { "Character", "CharacterSkillTree", "CharacterSkillNode", "Weapon", "PlayerParams", "EconomyRequest", "Deployment" })
            {
                Assert.AreEqual(0, await repository.QueryFirstAsync<int>(
                    $"SELECT COUNT(*) FROM {table} WHERE CreateUserId=@playerId", new { playerId }));
            }
        }

        [TestMethod]
        public async Task RealDatabase_DrawFailure_RollsBackDebitAndRewards()
        {
            using var repository = CreateRepository();
            Guid playerId = Guid.NewGuid();
            using (var transaction = repository.OpenTransaction())
            {
                await repository.InsertAsync(new Player { Id = playerId, Name = "CharacterShopRollbackTest", IsClientLogin = true });
                var argument = SnapshotSaveTests.CreateServiceArgument(repository, playerId);
                var shop = new ShopService(argument, new GoldService(argument), new ItemService(argument));
                await shop.Draw(new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = 10 });
                Assert.AreEqual(300, await new GoldService(argument).GetBalance());
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    shop.Draw(new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = 10 }));
                transaction.Rollback();
            }
            Assert.AreEqual(0, await repository.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM EconomyRequest WHERE CreateUserId=@playerId", new { playerId }));
            Assert.AreEqual(0, await repository.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM PlayerParams WHERE CreateUserId=@playerId", new { playerId }));
            Assert.AreEqual(0, await repository.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM Player WHERE Id=@playerId", new { playerId }));
        }

        [TestMethod]
        public async Task RealDatabase_WaveRewardsAndSkillPurchase_AreValidatedAndIdempotent()
        {
            using var repository = CreateRepository();
            Guid playerId = Guid.NewGuid();
            using (var transaction = repository.OpenTransaction())
            {
                await repository.InsertAsync(new Player { Id = playerId, Name = "EconomySqlTest", IsClientLogin = true });
                await repository.InsertAsync(new PlayerParams { Group = "game_progress", Name = "gold", Value = "3000", CreateUserId = playerId });
                var argument = SnapshotSaveTests.CreateServiceArgument(repository, playerId);
                var economy = new EconomyService(argument, new GoldService(argument));
                var start = new WaveStartRequest { RequestId = Guid.NewGuid(), Wave = 5, Difficulty = 0 };
                var wave = await economy.StartWave(start);
                Assert.AreEqual(22, wave.EnemyCount);
                Assert.AreEqual(wave.SessionId, (await economy.StartWave(start)).SessionId);
                var kill = new EnemyKillRequest { RequestId = Guid.NewGuid(), SessionId = wave.SessionId, EnemyIndex = 21 };
                Assert.AreEqual(3104, (await economy.ReportKill(kill)).Gold);
                Assert.AreEqual(3104, (await economy.ReportKill(kill)).Gold);
                await Assert.ThrowsAsync<ArgumentException>(() => economy.ReportKill(new EnemyKillRequest
                {
                    RequestId = Guid.NewGuid(), SessionId = wave.SessionId, EnemyIndex = 21,
                }));
                await Assert.ThrowsAsync<ArgumentException>(() => economy.ReportKill(new EnemyKillRequest
                {
                    RequestId = Guid.NewGuid(), SessionId = wave.SessionId, EnemyIndex = 22,
                }));
                var purchase = new SkillPurchaseRequest { RequestId = Guid.NewGuid(), Type = "active", Kind = 1 };
                Assert.AreEqual(3004, (await economy.PurchaseSkill(purchase)).Gold);
                Assert.AreEqual(3004, (await economy.PurchaseSkill(purchase)).Gold);
                await Assert.ThrowsAsync<ArgumentException>(() => economy.PurchaseSkill(new SkillPurchaseRequest
                {
                    RequestId = Guid.NewGuid(), Type = "active", Kind = 1,
                }));
                var progress = new GameProgressService(argument);
                await progress.Save(new GameProgressModel { Version = 9, Gold = 999999 });
                Assert.AreEqual(3004, await new GoldService(argument).GetBalance());
                Assert.AreEqual("true", await repository.QueryFirstAsync<string>(
                    @"SELECT Value FROM PlayerParams WHERE CreateUserId=@playerId
                      AND [Group]='game_progress.unlocked_bullet_kinds' AND Name='1'", new { playerId }));

                await economy.StartWave(new WaveStartRequest { RequestId = Guid.NewGuid(), Wave = 1, Difficulty = 0 });
                await Assert.ThrowsAsync<ArgumentException>(() => economy.ReportKill(new EnemyKillRequest
                {
                    RequestId = Guid.NewGuid(), SessionId = wave.SessionId, EnemyIndex = 0,
                }));
                transaction.Rollback();
            }
            foreach (string table in new[] { "PlayerParams", "EconomyRequest" })
            {
                Assert.AreEqual(0, await repository.QueryFirstAsync<int>(
                    $"SELECT COUNT(*) FROM {table} WHERE CreateUserId=@playerId", new { playerId }));
            }
        }

        [TestMethod]
        public async Task RealDatabase_ParallelReplayAndMidRewardFailure_PreserveCommittedState()
        {
            using var setup = CreateRepository();
            Guid playerId = Guid.NewGuid();
            try
            {
                using (var transaction = setup.OpenTransaction())
                {
                    await setup.InsertAsync(new Player { Id = playerId, Name = "ShopConcurrencySqlTest", IsClientLogin = true });
                    await setup.InsertAsync(new PlayerParams { Group = "game_progress", Name = "gold", Value = "3000", CreateUserId = playerId });
                    transaction.Commit();
                }
                Guid requestId = Guid.NewGuid();
                async Task<ShopDrawResult> DrawOnce()
                {
                    using var repository = CreateRepository();
                    using var transaction = repository.OpenTransaction();
                    var argument = SnapshotSaveTests.CreateServiceArgument(repository, playerId);
                    var result = await new ShopService(argument, new GoldService(argument), new ItemService(argument))
                        .Draw(new ShopDrawRequest { RequestId = requestId, Count = 1 });
                    transaction.Commit();
                    return result;
                }
                var results = await Task.WhenAll(DrawOnce(), DrawOnce()).WaitAsync(TimeSpan.FromSeconds(30));
                Assert.AreEqual(results[0].Results[0].CharacterId, results[1].Results[0].CharacterId);
                Assert.AreEqual("2900", await setup.QueryFirstAsync<string>(
                    "SELECT Value FROM PlayerParams WHERE CreateUserId=@playerId AND Name='gold'", new { playerId }));
                int existingKind = results[0].Results[0].CharacterKind;
                int missingKind = existingKind == 0 ? 1 : 0;
                using (var transaction = setup.OpenTransaction())
                {
                    var argument = SnapshotSaveTests.CreateServiceArgument(setup, playerId);
                    var failing = new FixedShop(argument, new GoldService(argument), new FailingShardService(argument), missingKind);
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        failing.Draw(new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = 10 }));
                    transaction.Rollback();
                }
                Assert.AreEqual("2900", await setup.QueryFirstAsync<string>(
                    "SELECT Value FROM PlayerParams WHERE CreateUserId=@playerId AND Name='gold'", new { playerId }));
                foreach (string table in new[] { "Character", "Weapon", "EconomyRequest" })
                {
                    Assert.AreEqual(1, await setup.QueryFirstAsync<int>(
                        $"SELECT COUNT(*) FROM {table} WHERE CreateUserId=@playerId", new { playerId }));
                }
            }
            finally
            {
                using var cleanup = setup.OpenTransaction();
                foreach (string table in new[] { "CharacterSkillNode", "CharacterSkillTree", "Deployment", "Character", "WeaponAffixValue", "WeaponAffix", "WeaponUniqueValue", "Weapon", "EconomyRequest", "PlayerParams", "Item" })
                {
                    await setup.ExecuteAsync($"DELETE FROM {table} WHERE CreateUserId=@playerId", new { playerId });
                }
                await setup.ExecuteAsync("DELETE FROM Player WHERE Id=@playerId", new { playerId });
                cleanup.Commit();
            }
            Assert.AreEqual(0, await setup.QueryFirstAsync<int>("SELECT COUNT(*) FROM Player WHERE Id=@playerId", new { playerId }));
        }
    }
}
