using Dapper;
using System.Data.SqlClient;

namespace ReconstructNodesServer.Utility;

/// <summary>啟動時先建立資料庫連線池並預熱 Dapper，避免第一個玩家請求承擔 TLS／登入／JIT 成本。</summary>
public sealed class DatabaseWarmupService(string? connectionString, ILogger<DatabaseWarmupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(connectionString)) { return; }
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(stoppingToken);
            await connection.QueryFirstAsync<int>("SELECT 1");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Database warmup failed");
        }
    }
}
