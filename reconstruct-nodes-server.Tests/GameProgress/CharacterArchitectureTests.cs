using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Migrations;

namespace ReconstructNodesServer.Tests.GameProgress
{
    [TestClass]
    public class CharacterArchitectureTests
    {
        [TestMethod]
        public void Character_Model_EnforcesOneKindAndExclusiveEquipment()
        {
            using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;").Options);
            var character = context.Model.FindEntityType(typeof(Character))!;
            var kinds = character.GetIndexes().Single(index => index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "CreateUserId", "Kind" }));
            Assert.IsTrue(kinds.IsUnique);
            Assert.AreEqual("[Status] <> -1", kinds.GetFilter());
            var equipment = character.GetIndexes().Single(index => index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "CreateUserId", "WeaponEntryKey" }));
            Assert.IsTrue(equipment.IsUnique);
            Assert.AreEqual("[Status] <> -1 AND [WeaponEntryKey] IS NOT NULL", equipment.GetFilter());
            Assert.AreEqual(450, character.FindProperty("WeaponEntryKey")!.GetMaxLength());
            var tables = context.Model.GetEntityTypes().Select(entity => entity.GetTableName()).ToList();
            Assert.DoesNotContain("CharacterWeapon", tables);
            Assert.DoesNotContain("DeploymentSkill", tables);
            Assert.DoesNotContain("DeploymentSkillNode", tables);
            var parameters = context.Model.FindEntityType(typeof(PlayerParams))!;
            Assert.IsNotNull(parameters.FindProperty("Value"));
            Assert.DoesNotContain("GoldAccount", tables);
            var requests = context.Model.FindEntityType(typeof(EconomyRequest))!;
            Assert.IsTrue(requests.GetIndexes().Single(index =>
                index.Properties.Select(property => property.Name).SequenceEqual(new[] { "CreateUserId", "RequestId" })).IsUnique);
        }

        [TestMethod]
        public void Character_Json_ContainsIndependentConfiguration()
        {
            var model = new CharacterModel
            {
                Kind = 3,
                SkillTrees = [new GameProgressSkillLoadoutModel
                {
                    Kind = 7,
                    AuxiliaryNodes = [new GameProgressSkillNodeModel { Type = "auxiliary", LootId = "gem" }],
                }],
            };
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(model));
            Assert.IsFalse(json.RootElement.TryGetProperty("name", out _));
            Assert.AreEqual(3, json.RootElement.GetProperty("kind").GetInt32());
            var tree = json.RootElement.GetProperty("skill_trees")[0];
            Assert.AreEqual("gem", tree.GetProperty("auxiliary_gem_ids")[0].GetString());
            Assert.IsFalse(json.RootElement.TryGetProperty("deployment_id", out _));
        }

        [TestMethod]
        [DataRow("Up")]
        [DataRow("Down")]
        public void Migration_ClearsDeploymentsBeforeChangingReference_AndPreservesUnrelatedData(string direction)
        {
            var migration = new SeparateCharactersFromDeployments();
            var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
            typeof(SeparateCharactersFromDeployments).GetMethod(direction, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(migration, [builder]);
            Assert.AreEqual("DELETE FROM [Deployment];", ((SqlOperation)builder.Operations[0]).Sql);
            Assert.IsFalse(builder.Operations.OfType<DropTableOperation>()
                .Any(operation => operation.Name is "Weapon" or "BattlefieldRoad" or "PlayerParams"));
            if (direction == "Up")
            {
                Assert.IsTrue(builder.Operations.OfType<CreateTableOperation>().Any(operation => operation.Name == "Character"));
                var reference = builder.Operations.OfType<AddColumnOperation>().Single(operation => operation.Name == "CharacterId");
                Assert.IsFalse(reference.IsNullable);
                Assert.IsNull(reference.DefaultValue);
            }
        }

        [TestMethod]
        public void EconomyMigration_PreservesGoldInPlayerParams()
        {
            var migration = new AddAuthoritativeGoldAndShopRequests();
            var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
            typeof(AddAuthoritativeGoldAndShopRequests).GetMethod("Up", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(migration, [builder]);
            Assert.IsFalse(builder.Operations.OfType<CreateTableOperation>().Any(operation => operation.Name == "GoldAccount"));
            Assert.IsTrue(builder.Operations.OfType<CreateTableOperation>().Any(operation => operation.Name == "EconomyRequest"));
            Assert.IsTrue(builder.Operations.OfType<SqlOperation>().All(operation =>
                !operation.Sql.Contains("DELETE FROM PlayerParams") && !operation.Sql.Contains("INSERT INTO GoldAccount")));
        }
    }
}
