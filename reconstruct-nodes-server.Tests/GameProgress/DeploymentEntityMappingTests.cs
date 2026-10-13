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
            Assert.AreEqual("CharacterSkillTree", CommonUtility.GetTableAttribute<CharacterSkillTree>());
            Assert.AreEqual("CharacterSkillNode", CommonUtility.GetTableAttribute<CharacterSkillNode>());
            Assert.AreEqual("BattlefieldRoad", CommonUtility.GetTableAttribute<BattlefieldRoad>());
        }

        [TestMethod]
        public void RelationAttribute_ResolvesToRenamedForeignKeyProperties()
        {
            Assert.AreEqual("CharacterId", CommonUtility.GetForeignKeyField<CharacterSkillTree, Character>());
            Assert.AreEqual("CharacterSkillTreeId", CommonUtility.GetForeignKeyField<CharacterSkillNode, CharacterSkillTree>());
            Assert.AreEqual("CharacterId", CommonUtility.GetForeignKeyField<Deployment, Character>());
        }

        [TestMethod]
        public void ForeignKeyProperties_UseRenamedColumnNames()
        {
            // Dapper.Contrib 以屬性名稱對應欄位，因此屬性名即資料表欄位名。
            Assert.IsNotNull(typeof(Deployment).GetProperty("CharacterId"));
            Assert.IsNull(typeof(Deployment).GetProperty("WeaponEntryKey"));
            Assert.IsNull(typeof(Deployment).GetProperty("CharacterName"));
            Assert.IsNotNull(typeof(CharacterSkillNode).GetProperty("CharacterSkillTreeId"));
        }
    }
}
