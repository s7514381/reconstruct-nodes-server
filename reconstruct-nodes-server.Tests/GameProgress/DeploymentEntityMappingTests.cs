using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Utility;

namespace ReconstructNodesServer.Tests.GameProgress
{
    [TestClass]
    public class DeploymentEntityMappingTests
    {
        [TestMethod]
        public void TableAttribute_UsesRenamedTableNames()
        {
            Assert.AreEqual("Deployment", CommonUtility.GetTableAttribute<Deployment>());
            Assert.AreEqual("DeploymentSkill", CommonUtility.GetTableAttribute<DeploymentSkill>());
            Assert.AreEqual("DeploymentSkillNode", CommonUtility.GetTableAttribute<DeploymentSkillNode>());
            Assert.AreEqual("BattlefieldRoad", CommonUtility.GetTableAttribute<BattlefieldRoad>());
        }

        [TestMethod]
        public void RelationAttribute_ResolvesToRenamedForeignKeyProperties()
        {
            Assert.AreEqual("DeploymentId", CommonUtility.GetForeignKeyField<DeploymentSkill, Deployment>());
            Assert.AreEqual("DeploymentSkillId", CommonUtility.GetForeignKeyField<DeploymentSkillNode, DeploymentSkill>());
        }

        [TestMethod]
        public void ForeignKeyProperties_UseRenamedColumnNames()
        {
            // Dapper.Contrib 以屬性名稱對應欄位，因此屬性名即資料表欄位名。
            Assert.IsNotNull(typeof(DeploymentSkill).GetProperty("DeploymentId"));
            Assert.IsNull(typeof(DeploymentSkill).GetProperty("GameProgressDeploymentId"));
            Assert.IsNotNull(typeof(DeploymentSkillNode).GetProperty("DeploymentSkillId"));
            Assert.IsNull(typeof(DeploymentSkillNode).GetProperty("GameProgressDeploymentSkillId"));
        }
    }
}
