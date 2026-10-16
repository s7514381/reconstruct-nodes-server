using Dapper;
using System.Data.SqlClient;

namespace ReconstructNodesServer.Utility;

/// <summary>
/// 定期刪除超過保留天數的 EconomyRequest 冪等回執。回執只需涵蓋客戶端可能重試的期間；
/// 進行中波次的 wave_start 回執（擊殺獎勵與 WaveComplete 要用）一律保留。
/// <c>EconomyRequestRetention:Days</c> 預設 0（停用），確認 SQL 後設為例如 30 才會啟用。
/// </summary>
public sealed class EconomyRequestRetentionService(string? connectionString, int retentionDays,
    ILogger<EconomyRequestRetentionService> logger) : BackgroundService
{
    private const int BatchSize = 2000;
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    internal const string DeleteSql = """
        DELETE TOP (@Batch) e FROM EconomyRequest e
        WHERE e.CreateDate < @Cutoff
            AND NOT EXISTS (SELECT 1 FROM PlayerParams p
                WHERE p.CreateUserId = e.CreateUserId AND p.[Group] = 'economy' AND p.Name = 'active_wave'
                    AND p.Value = CONVERT(nvarchar(36), e.RequestId));
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(connectionString) || retentionDays <= 0) { return; }
        try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int total = await PurgeAsync(stoppingToken);
                if (total > 0) { logger.LogInformation("Purged {Count} expired EconomyRequest rows", total); }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "EconomyRequest retention purge failed");
            }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
        int total = 0, deleted;
        do
        {
            // 小批次刪除，避免長交易與大量記錄檔寫入拖慢玩家請求。
            deleted = await connection.ExecuteAsync(new CommandDefinition(DeleteSql,
                new { Batch = BatchSize, Cutoff = cutoff }, commandTimeout: 60, cancellationToken: cancellationToken));
            total += deleted;
            if (deleted == BatchSize) { await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken); }
        }
        while (deleted == BatchSize);
        return total;
    }
}
