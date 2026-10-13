using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.DBModel;

namespace ReconstructNodesServer.Tests.GameProgress
{
    /// <summary>
    /// 只建立 EF model，不開啟連線；UseSqlServer 的連線字串不會被實際使用。
    /// </summary>
    [TestClass]
    public class DeploymentEfModelTests
    {
        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;")
                .Options;
            return new ApplicationDbContext(options);
        }

        [TestMethod]
        public void Entities_MapToRenamedTables()
        {
            using var context = CreateContext();

            Assert.AreEqual("Deployment", context.Model.FindEntityType(typeof(Deployment))!.GetTableName());
            Assert.AreEqual("CharacterSkillTree", context.Model.FindEntityType(typeof(CharacterSkillTree))!.GetTableName());
            Assert.AreEqual("CharacterSkillNode", context.Model.FindEntityType(typeof(CharacterSkillNode))!.GetTableName());
            Assert.AreEqual("BattlefieldRoad", context.Model.FindEntityType(typeof(BattlefieldRoad))!.GetTableName());
        }

        [TestMethod]
        public void Model_HasNoTableWithOldGameProgressNames()
        {
            using var context = CreateContext();

            var tableNames = context.Model.GetEntityTypes().Select(e => e.GetTableName()).ToList();
            CollectionAssert.DoesNotContain(tableNames, "GameProgressDeployment");
            CollectionAssert.DoesNotContain(tableNames, "GameProgressDeploymentSkill");
            CollectionAssert.DoesNotContain(tableNames, "GameProgressDeploymentSkillNode");
            CollectionAssert.DoesNotContain(tableNames, "GameProgressBattlefieldRoad");
        }

        [TestMethod]
        public void PrimaryKeys_UseRenamedConstraintNames()
        {
            using var context = CreateContext();

            Assert.AreEqual("PK_Deployment", context.Model.FindEntityType(typeof(Deployment))!.FindPrimaryKey()!.GetName());
            Assert.AreEqual("PK_CharacterSkillTree", context.Model.FindEntityType(typeof(CharacterSkillTree))!.FindPrimaryKey()!.GetName());
            Assert.AreEqual("PK_CharacterSkillNode", context.Model.FindEntityType(typeof(CharacterSkillNode))!.FindPrimaryKey()!.GetName());
            Assert.AreEqual("PK_BattlefieldRoad", context.Model.FindEntityType(typeof(BattlefieldRoad))!.FindPrimaryKey()!.GetName());
        }

        [TestMethod]
        public void ForeignKeyColumns_UseRenamedProperties()
        {
            using var context = CreateContext();

            var skill = context.Model.FindEntityType(typeof(CharacterSkillTree))!;
            Assert.IsNotNull(skill.FindProperty("CharacterId"));
            Assert.IsNull(skill.FindProperty("DeploymentId"));

            var node = context.Model.FindEntityType(typeof(CharacterSkillNode))!;
            Assert.IsNotNull(node.FindProperty("CharacterSkillTreeId"));
            Assert.IsNull(node.FindProperty("DeploymentSkillId"));
        }

        [TestMethod]
        public void Indexes_UseRenamedNames()
        {
            using var context = CreateContext();

            AssertIndexNames(context, typeof(Deployment), "IX_Deployment_CreateUserId", "IX_Deployment_CharacterId");
            AssertIndexNames(context, typeof(CharacterSkillTree), "IX_CharacterSkillTree_CreateUserId", "IX_CharacterSkillTree_CharacterId");
            AssertIndexNames(context, typeof(CharacterSkillNode), "IX_CharacterSkillNode_CreateUserId", "IX_CharacterSkillNode_CharacterSkillTreeId");
            AssertIndexNames(context, typeof(BattlefieldRoad), "IX_BattlefieldRoad_CreateUserId");
        }

        private static void AssertIndexNames(ApplicationDbContext context, Type entityType, params string[] expected)
        {
            var actual = context.Model.FindEntityType(entityType)!
                .GetIndexes()
                .Select(i => i.GetDatabaseName())
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(
                expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                actual,
                $"index names of {entityType.Name}");
        }
    }
}
