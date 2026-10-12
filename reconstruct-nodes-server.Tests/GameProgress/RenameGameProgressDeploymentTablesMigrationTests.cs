using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ReconstructNodesServer.Migrations;

namespace ReconstructNodesServer.Tests.GameProgress
{
    /// <summary>
    /// 離線驗證 rename migration 產生的操作序列，不連線資料庫。
    /// 目的是證明只有改名操作，沒有 DropTable/CreateTable 等會遺失資料的操作。
    /// </summary>
    [TestClass]
    public class RenameGameProgressDeploymentTablesMigrationTests
    {
        private static readonly string[] ExpectedUpTables =
        {
            "GameProgressDeployment->Deployment",
            "GameProgressDeploymentSkill->DeploymentSkill",
            "GameProgressDeploymentSkillNode->DeploymentSkillNode",
            "GameProgressBattlefieldRoad->BattlefieldRoad",
        };

        private static readonly string[] ExpectedUpColumns =
        {
            "DeploymentSkill.GameProgressDeploymentId->DeploymentId",
            "DeploymentSkillNode.GameProgressDeploymentSkillId->DeploymentSkillId",
        };

        private static readonly string[] ExpectedUpIndexes =
        {
            "Deployment.IX_GameProgressDeployment_CreateUserId->IX_Deployment_CreateUserId",
            "DeploymentSkill.IX_GameProgressDeploymentSkill_CreateUserId->IX_DeploymentSkill_CreateUserId",
            "DeploymentSkill.IX_GameProgressDeploymentSkill_GameProgressDeploymentId->IX_DeploymentSkill_DeploymentId",
            "DeploymentSkillNode.IX_GameProgressDeploymentSkillNode_CreateUserId->IX_DeploymentSkillNode_CreateUserId",
            "DeploymentSkillNode.IX_GameProgressDeploymentSkillNode_GameProgressDeploymentSkillId->IX_DeploymentSkillNode_DeploymentSkillId",
            "BattlefieldRoad.IX_GameProgressBattlefieldRoad_CreateUserId->IX_BattlefieldRoad_CreateUserId",
        };

        private static readonly string[] ExpectedUpPrimaryKeySql =
        {
            "EXEC sp_rename N'PK_GameProgressDeployment', N'PK_Deployment', N'OBJECT';",
            "EXEC sp_rename N'PK_GameProgressDeploymentSkill', N'PK_DeploymentSkill', N'OBJECT';",
            "EXEC sp_rename N'PK_GameProgressDeploymentSkillNode', N'PK_DeploymentSkillNode', N'OBJECT';",
            "EXEC sp_rename N'PK_GameProgressBattlefieldRoad', N'PK_BattlefieldRoad', N'OBJECT';",
        };

        [TestMethod]
        public void Up_ContainsOnlyRenameOperations()
        {
            var operations = BuildOperations("Up");

            AssertOnlyRenameOperations(operations, "Up");
            Assert.HasCount(4, operations.OfType<RenameTableOperation>());
            Assert.HasCount(2, operations.OfType<RenameColumnOperation>());
            Assert.HasCount(6, operations.OfType<RenameIndexOperation>());
            Assert.HasCount(4, operations.OfType<SqlOperation>());
            Assert.HasCount(16, operations);
        }

        [TestMethod]
        public void Up_MapsEachRenameToExpectedName()
        {
            var operations = BuildOperations("Up");

            CollectionAssert.AreEqual(ExpectedUpTables, operations.OfType<RenameTableOperation>()
                .Select(o => $"{o.Name}->{o.NewName}").ToList());
            CollectionAssert.AreEqual(ExpectedUpColumns, operations.OfType<RenameColumnOperation>()
                .Select(o => $"{o.Table}.{o.Name}->{o.NewName}").ToList());
            CollectionAssert.AreEqual(ExpectedUpIndexes, operations.OfType<RenameIndexOperation>()
                .Select(o => $"{o.Table}.{o.Name}->{o.NewName}").ToList());
            CollectionAssert.AreEqual(ExpectedUpPrimaryKeySql, operations.OfType<SqlOperation>()
                .Select(o => o.Sql).ToList());
        }

        [TestMethod]
        public void Down_ContainsOnlyRenameOperations()
        {
            var operations = BuildOperations("Down");

            AssertOnlyRenameOperations(operations, "Down");
            Assert.HasCount(4, operations.OfType<RenameTableOperation>());
            Assert.HasCount(2, operations.OfType<RenameColumnOperation>());
            Assert.HasCount(6, operations.OfType<RenameIndexOperation>());
            Assert.HasCount(4, operations.OfType<SqlOperation>());
        }

        [TestMethod]
        public void Down_IsExactInverseOfUp()
        {
            var down = BuildOperations("Down");

            CollectionAssert.AreEqual(
                Sorted(ExpectedUpTables.Select(SwapArrow)),
                Sorted(down.OfType<RenameTableOperation>().Select(o => $"{o.Name}->{o.NewName}")));
            CollectionAssert.AreEqual(
                Sorted(ExpectedUpColumns.Select(SwapArrow)),
                Sorted(down.OfType<RenameColumnOperation>().Select(o => $"{o.Table}.{o.Name}->{o.NewName}")));
            CollectionAssert.AreEqual(
                Sorted(ExpectedUpIndexes.Select(SwapArrow)),
                Sorted(down.OfType<RenameIndexOperation>().Select(o => $"{o.Table}.{o.Name}->{o.NewName}")));

            var downSql = down.OfType<SqlOperation>().Select(o => o.Sql).ToHashSet();
            foreach (var upSql in ExpectedUpPrimaryKeySql)
            {
                Assert.Contains(SwapSpRename(upSql), downSql, $"Down 缺少對應還原語句: {upSql}");
            }
        }

        private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
        {
            var migration = new RenameGameProgressDeploymentTables();
            var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
            var method = typeof(RenameGameProgressDeploymentTables).GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"{methodName} not found");
            method.Invoke(migration, new object[] { builder });
            return builder.Operations;
        }

        private static void AssertOnlyRenameOperations(IReadOnlyList<MigrationOperation> operations, string direction)
        {
            var allowed = operations.All(o => o is RenameTableOperation || o is RenameColumnOperation
                || o is RenameIndexOperation || o is SqlOperation);
            Assert.IsTrue(allowed, $"{direction} 只允許改名操作與 sp_rename 的 SqlOperation，不得 Drop/Create/Add/Delete");
        }

        // "A->B" 反轉為 "B->A"；"T.A->B" 反轉為 "T.B->A"（資料表前綴保留）
        private static string SwapArrow(string rename)
        {
            var arrow = rename.IndexOf("->", StringComparison.Ordinal);
            var left = rename[..arrow];
            var right = rename[(arrow + 2)..];
            var dot = left.IndexOf('.');
            if (dot < 0) { return $"{right}->{left}"; }
            var table = left[..(dot + 1)];
            return $"{table}{right}->{left[(dot + 1)..]}";
        }

        // "EXEC sp_rename N'A', N'B', N'OBJECT';" -> "EXEC sp_rename N'B', N'A', N'OBJECT';"
        private static string SwapSpRename(string sql)
        {
            var match = Regex.Match(sql, "^EXEC sp_rename N'([^']+)', N'([^']+)', N'OBJECT';$");
            Assert.IsTrue(match.Success, $"無法解析 sp_rename: {sql}");
            return $"EXEC sp_rename N'{match.Groups[2].Value}', N'{match.Groups[1].Value}', N'OBJECT';";
        }

        private static string[] Sorted(IEnumerable<string> values) =>
            values.OrderBy(v => v, StringComparer.Ordinal).ToArray();
    }
}
