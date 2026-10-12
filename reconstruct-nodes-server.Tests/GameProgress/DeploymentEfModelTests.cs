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
            Assert.AreEqual("DeploymentSkill", context.Model.FindEntityType(typeof(DeploymentSkill))!.GetTableName());
            Assert.AreEqual("DeploymentSkillNode", context.Model.FindEntityType(typeof(DeploymentSkillNode))!.GetTableName());
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
            Assert.AreEqual("PK_DeploymentSkill", context.Model.FindEntityType(typeof(DeploymentSkill))!.FindPrimaryKey()!.GetName());
            Assert.AreEqual("PK_DeploymentSkillNode", context.Model.FindEntityType(typeof(DeploymentSkillNode))!.FindPrimaryKey()!.GetName());
            Assert.AreEqual("PK_BattlefieldRoad", context.Model.FindEntityType(typeof(BattlefieldRoad))!.FindPrimaryKey()!.GetName());
        }

        [TestMethod]
        public void ForeignKeyColumns_UseRenamedProperties()
        {
            using var context = CreateContext();

            var skill = context.Model.FindEntityType(typeof(DeploymentSkill))!;
            Assert.IsNotNull(skill.FindProperty("DeploymentId"));
            Assert.IsNull(skill.FindProperty("GameProgressDeploymentId"));

            var node = context.Model.FindEntityType(typeof(DeploymentSkillNode))!;
            Assert.IsNotNull(node.FindProperty("DeploymentSkillId"));
            Assert.IsNull(node.FindProperty("GameProgressDeploymentSkillId"));
        }

        [TestMethod]
        public void Indexes_UseRenamedNames()
        {
            using var context = CreateContext();

            AssertIndexNames(context, typeof(Deployment), "IX_Deployment_CreateUserId");
            AssertIndexNames(context, typeof(DeploymentSkill), "IX_DeploymentSkill_CreateUserId", "IX_DeploymentSkill_DeploymentId");
            AssertIndexNames(context, typeof(DeploymentSkillNode), "IX_DeploymentSkillNode_CreateUserId", "IX_DeploymentSkillNode_DeploymentSkillId");
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
