using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

public static class KillRewardRules
{
    public static long CalculateClaim(string? sessionJson, bool claimed, int enemyIndex,
        IEnumerable<TalentAcquired> goldTalents)
    {
        if (sessionJson == null) { throw new BusinessArgumentException(ApiMessageKeys.EconomySessionNotOwned); }
        var session = JsonSerializer.Deserialize<WaveStartResult>(sessionJson)
            ?? throw new InvalidOperationException("波次紀錄格式錯誤");
        if (session.Wave < 1 || session.EnemyCount < 1) { throw new InvalidOperationException("波次紀錄格式錯誤"); }
        if (enemyIndex < 0 || enemyIndex >= session.EnemyCount) { throw new BusinessArgumentException(ApiMessageKeys.EconomyEnemyInvalid); }
        if (claimed) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRewardClaimed); }
        return Calculate(session, enemyIndex, goldTalents);
    }

    public static long Calculate(WaveStartResult session, int enemyIndex, IEnumerable<TalentAcquired> goldTalents)
    {
        if (enemyIndex < 0 || enemyIndex >= session.EnemyCount)
        {
            throw new BusinessArgumentException(ApiMessageKeys.EconomyEnemyInvalid);
        }
        long reward = checked(8L + session.Wave);
        if (session.Wave % 5 == 0 && enemyIndex == session.EnemyCount - 1) { reward = checked(reward * 8); }
        foreach (var talent in goldTalents)
        {
            var level = JsonSerializer.Deserialize<JsonElement>(talent.Value);
            bool acquired = level.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => level.GetInt32() > 0,
                _ => throw new InvalidOperationException("金幣天賦資料格式錯誤"),
            };
            if (acquired)
            {
                reward = checked(reward + (talent.NodeId switch { "gold_1" => 1, "gold_2" => 2, "gold_3" => 3, _ => 0 }));
            }
        }
        return reward;
    }
}
