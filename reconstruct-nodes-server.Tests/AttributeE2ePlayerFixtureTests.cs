using Dapper;
using Microsoft.Data.SqlClient;

namespace ReconstructNodesServer.Tests;

/// <summary>
/// 前端真 HTTP 能力值驗證的資料庫輔助入口（opt-in）：只針對單一專用 ClientId 的玩家，
/// 設定其等級或清理其所有資料；未設定環境變數時為 Inconclusive，不連資料庫。
/// ATTR_FIXTURE_CONNECTION、ATTR_FIXTURE_CLIENT_ID 必填；ATTR_FIXTURE_ACTION=level|verify|cleanup，
/// level 另需 ATTR_FIXTURE_LEVEL。
/// </summary>
[TestClass]
public class AttributeE2ePlayerFixtureTests
{
    [TestMethod]
    public async Task Fixture_Action()
    {
        var connection = Environment.GetEnvironmentVariable("ATTR_FIXTURE_CONNECTION");
        var clientId = Environment.GetEnvironmentVariable("ATTR_FIXTURE_CLIENT_ID");
        var action = Environment.GetEnvironmentVariable("ATTR_FIXTURE_ACTION");
        if (string.IsNullOrEmpty(connection) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(action))
            Assert.Inconclusive("未設定 ATTR_FIXTURE_*，略過");
        Assert.AreEqual(32, clientId.Length, "只接受測試用 32 字元 ClientId");

        await using var db = new SqlConnection(connection);
        await db.OpenAsync();
        var player = await db.QuerySingleOrDefaultAsync<Guid?>("SELECT Id FROM Player WHERE ClientId = @clientId", new { clientId });
        Assert.IsNotNull(player, "找不到該 ClientId 的玩家");

        switch (action)
        {
            case "level":
                var level = Environment.GetEnvironmentVariable("ATTR_FIXTURE_LEVEL")!;
                var rows = await db.ExecuteAsync(
                    "UPDATE PlayerParams SET Value = @level WHERE Name = 'level' AND [Group] = 'game_progress.progression' AND CreateUserId = @player",
                    new { level, player });
                Assert.AreEqual(1, rows, "應只更新該玩家的一筆 level");
                break;
            case "verify":
                var chars = (await db.QueryAsync<(string Kind, int Strength, int Intelligence, int Dexterity)>(
                    "SELECT Kind, Strength, Intelligence, Dexterity FROM [Character] WHERE CreateUserId = @player", new { player })).ToList();
                foreach (var c in chars)
                    Console.WriteLine($"ATTR_DB {c.Kind} {c.Strength}/{c.Intelligence}/{c.Dexterity}");
                Assert.IsTrue(chars.Count > 0);
                break;
            case "cleanup":
                var tables = (await db.QueryAsync<string>(
                    "SELECT t.name FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id WHERE c.name = 'CreateUserId'")).ToList();
                // 外鍵順序未知：反覆刪除直到沒有進展。
                for (var pass = 0; pass < tables.Count + 1 && tables.Count > 0; pass++)
                {
                    foreach (var table in tables.ToList())
                    {
                        try
                        {
                            await db.ExecuteAsync($"DELETE FROM [{table}] WHERE CreateUserId = @player", new { player });
                            tables.Remove(table);
                        }
                        catch (SqlException) { }
                    }
                }
                Assert.AreEqual(0, tables.Count, "仍有資料表因外鍵無法清理：" + string.Join(",", tables));
                await db.ExecuteAsync("DELETE FROM Player WHERE Id = @player AND ClientId = @clientId", new { player, clientId });
                Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Player WHERE ClientId = @clientId", new { clientId }));
                break;
            default:
                Assert.Fail("未知的 ATTR_FIXTURE_ACTION：" + action);
                break;
        }
    }
}
