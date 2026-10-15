using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests.Services
{
    [TestClass]
    public class ShopDrawTests
    {
        private class ShardService(BaseServiceArgument argument) : ItemService(argument)
        {
            public int Shards { get; private set; }
            public override Task<int> AdjustAmount(string key, int delta, string? category = null)
            {
                Assert.AreEqual("warrior_shard", key);
                Assert.AreEqual("character_shard", category);
                Shards += delta;
                return Task.FromResult(Shards);
            }
        }

        private class FixedShop(BaseServiceArgument argument, GoldService gold, ItemService items) : ShopService(argument, gold, items)
        {
            protected override int DrawIndex() => 0;
        }

        [TestMethod]
        [DataRow(1, 1100L, 0)]
        [DataRow(10, 300L, 9)]
        public async Task Draw_ChargesOnce_AndRecognizesDuplicatesWithinBatch(int count, long balance, int duplicates)
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            var shards = new ShardService(argument);
            var shop = new FixedShop(argument, new GoldService(argument), shards);
            var result = await shop.Draw(new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = count });
            Assert.AreEqual(balance, result.Gold);
            Assert.HasCount(count, result.Results);
            Assert.AreEqual(duplicates, shards.Shards);
            Assert.IsFalse(result.Results[0].IsDuplicate);
            Assert.AreEqual(duplicates, result.Results.Count(reward => reward.IsDuplicate));
            Assert.HasCount(1, repository.Calls.Select(call => call.Value).OfType<Character>().ToList());
            Assert.HasCount(1, repository.Calls.Select(call => call.Value).OfType<Weapon>().ToList());
            Assert.IsNotNull(result.State);
            Assert.HasCount(1, result.State.Characters);
            Assert.HasCount(1, result.State.Weapons);
            Assert.AreEqual(5, result.State.Characters.Single().SkillTrees.Single().Kind);
            Assert.AreEqual(result.Results[0].CharacterId, result.State.Characters.Single().Id);
            Assert.AreEqual(result.Results[0].WeaponEntryId, result.State.Weapons.Single().Id);
            Assert.AreEqual(duplicates, result.State.Shards.GetValueOrDefault("warrior_shard"));
            Assert.AreEqual(5, repository.Calls.Select(call => call.Value).OfType<CharacterSkillTree>().Single().Kind);
            Assert.IsTrue(result.Results.All(reward => reward.CharacterId == result.Results[0].CharacterId));
            Assert.AreEqual(balance.ToString(), repository.Calls.Select(call => call.Value).OfType<PlayerParams>().Single().Value);
            Assert.HasCount(1, repository.Calls.Select(call => call.Value).OfType<EconomyRequest>().ToList());
            using var stored = JsonDocument.Parse(repository.Calls.Select(call => call.Value).OfType<EconomyRequest>().Single().Result);
            Assert.IsFalse(stored.RootElement.GetProperty("results")[0].TryGetProperty("character_name", out _));
            Assert.IsNull(typeof(Character).GetProperty("Name"));
            Assert.IsNull(typeof(Item).GetProperty("Name"));
            Assert.IsNull(typeof(CharacterModel).GetProperty("Name"));
            Assert.IsNull(typeof(ItemDeltaRequest).GetProperty("Name"));
            Assert.DoesNotContain("\"Name\"", JsonSerializer.Serialize(ShopService.Catalog));
        }

        [TestMethod]
        public async Task Draw_LegacyReplay_PreservesRewardsWithoutReturningName()
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            Guid requestId = Guid.NewGuid();
            Guid characterId = Guid.NewGuid();
            repository.FirstRows[typeof(EconomyRequest)] = new EconomyRequest
            {
                RequestId = requestId, Operation = "shop_draw", Payload = JsonSerializer.Serialize(new { Count = 1 }),
                Result = "{\"request_id\":\"" + requestId + "\",\"gold\":77,\"results\":[{\"character_id\":\"" +
                    characterId + "\",\"character_kind\":0,\"character_name\":\"\\u6230\\u58eb\",\"is_duplicate\":false,\"shard_id\":\"\",\"weapon_entry_id\":\"weapon\"}]}",
            };
            var result = await new FixedShop(argument, new GoldService(argument), new ShardService(argument))
                .Draw(new ShopDrawRequest { RequestId = requestId, Count = 1 });
            Assert.AreEqual(77L, result.Gold);
            Assert.AreEqual(characterId, result.Results.Single().CharacterId);
            Assert.AreEqual("weapon", result.Results.Single().WeaponEntryId);
            Assert.IsNull(result.State, "Old durable receipts remain replayable without inventing state");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.IsFalse(json.RootElement.GetProperty("results")[0].TryGetProperty("character_name", out _));
            Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
        }

        [TestMethod]
        public async Task Draw_InsufficientGold_DoesNotCreateRewardsOrRequest()
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            repository.QueryRows[typeof(PlayerParams)] = new[] { new PlayerParams { Group = "game_progress", Name = "gold", Value = "99" } };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new FixedShop(argument, new GoldService(argument), new ShardService(argument))
                    .Draw(new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = 1 }));
            Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
        }

        [TestMethod]
        public async Task Draw_UpdatesExistingGoldParameter_WithoutChangingItsIdentity()
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            var row = new PlayerParams { Group = "game_progress", Name = "gold", Value = "3000" };
            Guid id = row.Id;
            repository.QueryRows[typeof(PlayerParams)] = new[] { row };
            var result = await new FixedShop(argument, new GoldService(argument), new ShardService(argument))
                .Draw(new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = 10 });
            Assert.AreEqual(2100, result.Gold);
            Assert.AreEqual(id, row.Id);
            Assert.AreEqual("2100", row.Value);
            Assert.AreEqual("game_progress", row.Group);
            Assert.AreEqual("gold", row.Name);
            Assert.IsTrue(repository.Calls.Any(call => call.Name == "UpdateAsync" && ReferenceEquals(call.Value, row)));
            Assert.IsFalse(repository.Calls.Where(call => call.Name == "InsertAsync").Any(call => call.Value is PlayerParams));
        }

        [TestMethod]
        [DataRow("-1")]
        [DataRow("broken")]
        [DataRow("")]
        [DataRow("9223372036854775808")]
        public async Task Gold_InvalidStoredValue_ReportsFailure(string value)
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            repository.QueryRows[typeof(PlayerParams)] = new[]
            {
                new PlayerParams { Group = "game_progress", Name = "gold", Value = value },
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => new GoldService(argument).GetBalance());
            Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
        }

        [TestMethod]
        public async Task Gold_DuplicateParameters_ReportsFailure()
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            repository.QueryRows[typeof(PlayerParams)] = new[]
            {
                new PlayerParams { Group = "game_progress", Name = "gold", Value = "100" },
                new PlayerParams { Group = "game_progress", Name = "gold", Value = "200" },
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => new GoldService(argument).GetBalance());
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Draw_ReplayReturnsOriginalResult_AndRejectsDifferentPayload(bool mismatch)
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            Guid id = Guid.NewGuid();
            var expected = new ShopDrawResult { RequestId = id, Gold = 77 };
            repository.FirstRows[typeof(EconomyRequest)] = new EconomyRequest
            {
                RequestId = id, Operation = "shop_draw", Payload = JsonSerializer.Serialize(new { Count = 1 }),
                Result = JsonSerializer.Serialize(expected),
            };
            var shop = new FixedShop(argument, new GoldService(argument), new ShardService(argument));
            var request = new ShopDrawRequest { RequestId = id, Count = mismatch ? 10 : 1 };
            if (mismatch) { await Assert.ThrowsAsync<ArgumentException>(() => shop.Draw(request)); }
            else { Assert.AreEqual(77, (await shop.Draw(request)).Gold); }
            Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(2)]
        [DataRow(-1)]
        public async Task Draw_UnsupportedCount_RejectsBeforeRepository(int count)
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            await Assert.ThrowsAsync<ArgumentException>(() =>
                new FixedShop(argument, new GoldService(argument), new ShardService(argument))
                    .Draw(new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = count }));
            Assert.HasCount(0, repository.Calls);
        }

        [TestMethod]
        public async Task Character_SaveCannotCreateOwnership()
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            await Assert.ThrowsAsync<ArgumentException>(() => new CharacterService(argument)
                .Save(new CharacterModel { Kind = 0 }));
            Assert.HasCount(0, repository.Calls);
        }

        [TestMethod]
        public async Task GameProgress_DoesNotWriteSubmittedGold()
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            await new GameProgressService(argument).Save(new GameProgressModel { Gold = long.MaxValue });
            Assert.IsFalse(repository.Calls.Select(call => call.Value).OfType<PlayerParams>().Any(row => row.Name == "gold"));
            var deletion = repository.Calls.Single(call => call.Sql?.StartsWith("DELETE FROM PlayerParams") == true);
            Assert.Contains("AND NOT ([Group] = 'game_progress' AND Name = 'gold')", deletion.Sql!);
        }

        [TestMethod]
        public async Task PlayerParams_CannotWriteGoldThroughSnapshotOrRawCrud()
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            var service = new PlayerParamsService(argument);
            await Assert.ThrowsAsync<ArgumentException>(() => service.Save(new ReconstructNodesServer.Library.GameModel.PlayerParamsModel
            {
                Group = "game_progress", Name = "gold", Value = "999999",
            }));
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveData(new PlayerParams
            {
                Group = "game_progress", Name = "gold", Value = "999999",
            }));
            Assert.HasCount(0, repository.Calls);
        }

        [TestMethod]
        public async Task PlayerParams_CannotRenameOrDeleteExistingGoldRow()
        {
            var (argument, repository) = SnapshotSaveTests.CreateArgument();
            Guid id = Guid.NewGuid();
            repository.QueryRows[typeof(Guid)] = new[] { id };
            var service = new PlayerParamsService(argument);
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveData(new PlayerParams
            {
                Id = id, Group = "unrelated", Name = "renamed", Value = "999999",
            }));
            await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteModel(new ReconstructNodesServer.Library.GameModel.PlayerParamsModel
            {
                Id = id,
            }));
            Assert.IsFalse(repository.Calls.Any(call => call.Name is "InsertAsync" or "UpdateAsync" or "ExecuteAsync"));
        }
    }
}
