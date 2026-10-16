using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 僅限 Development 的測試重置：清除本人成就領取紀錄與收集歷史，並把擊殺／通關進度歸零，
/// 讓成就可由頭領取。武器與詞綴收集由持有（含已刪除）武器推導，不屬於可清除的歷史，重置後仍會依武器重算。
/// 兩個領域 revision 前進，過期的領取請求會被衝突擋下。
/// </summary>
public class AchievementResetCommand(BaseServiceArgument argument, IWebHostEnvironment environment,
    RevisionCommand revisions) : BaseService(argument)
{
    private const string ResetSql = """
        DELETE d FROM AchievementDetail d INNER JOIN Achievement a ON a.Id = d.AchievementId
            WHERE a.CreateUserId = @CreateUserId;
        DELETE c FROM AchievementCollected c INNER JOIN Achievement a ON a.Id = c.AchievementId
            WHERE a.CreateUserId = @CreateUserId;
        DELETE FROM Achievement WHERE CreateUserId = @CreateUserId;
        UPDATE PlayerParams SET Value = N'0'
            WHERE CreateUserId = @CreateUserId AND Status != -1
            AND (([Group] = 'game_progress' AND Name = 'lifetime_enemies_defeated')
                OR [Group] = 'game_progress.cleared_waves');
        """;

    /// <summary>重置會清除玩家成就，只允許 Development 環境。</summary>
    public static void EnsureAllowed(IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) { throw new BusinessOperationException(ApiMessageKeys.AchievementResetUnavailable); }
    }

    public async Task<string> Execute()
    {
        EnsureAllowed(environment);
        await ExecuteAsync(ResetSql, new { CreateUserId = _playerId });
        await revisions.Advance(nameof(AchievementProgressModel));
        await revisions.Advance(nameof(GameProgressModel));
        return "reset";
    }
}
