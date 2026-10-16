using System.Text.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.DBModel;

namespace ReconstructNodesServer.Services;

public static class ProgressionRules
{
    public const int MaxLevel = 100;

    internal static WaveStartResult ReadSession(EconomyRequest receipt, int infiniteLevel)
    {
        var session = JsonSerializer.Deserialize<WaveStartResult>(receipt.Result)
            ?? throw new InvalidOperationException("Invalid wave session.");
        using var intent = JsonDocument.Parse(receipt.Payload);
        session.Difficulty = intent.RootElement.GetProperty("Difficulty").GetInt32();
        if (session.Wave < 1 || session.EnemyCount < 1 || session.Difficulty is < 0 or > 3)
        { throw new InvalidOperationException("Invalid persisted wave session."); }
        using var result = JsonDocument.Parse(receipt.Result);
        if (!result.RootElement.TryGetProperty("infinite_level", out _))
        { session.InfiniteLevel = infiniteLevel; }
        if (session.InfiniteLevel is < 1 or > 50)
        { throw new InvalidOperationException("Invalid persisted infinite level."); }
        return session;
    }

    public static int RequiredExperience(int level)
    {
        int n = Math.Max(1, level) - 1;
        return level >= MaxLevel ? 0 : checked((int)Math.Round(10 + 5 * n + 2 * n * n + .05 * n * n * n,
            MidpointRounding.AwayFromZero));
    }

    public static int ApplyKill(Dictionary<string, JsonElement> progression, WaveStartResult session, int enemyIndex)
    {
        int Read(string key, int initial = 0) => progression.TryGetValue(key, out var value) ? value.GetInt32() : initial;
        int level = Read("level", 1), experience = Read("experience"), fifths = Read("experience_fifths");
        long total = progression.TryGetValue("total_experience", out var saved) ? saved.GetInt64() : 0;
        if (level is < 1 or > MaxLevel || experience < 0 || fifths is < 0 or > 4 || total < 0)
        { throw new InvalidOperationException("Invalid persisted progression."); }
        int enemyLevel = session.Difficulty == 3 ? session.InfiniteLevel :
            1 + (session.Wave - 1) / new[] { 2, 4, 8 }[session.Difficulty];
        long reward = 1L + (session.Wave - 1) / 2;
        if (session.Wave % 5 == 0 && enemyIndex == session.EnemyCount - 1) { reward = checked(reward * 8); }
        long units = checked(fifths + reward * Math.Clamp(10L - Math.Abs((long)level - enemyLevel), 0, 5));
        long earned = units / 5;
        total = checked(total + earned);
        long remaining = checked(experience + earned);
        int previous = level;
        while (level < MaxLevel && remaining >= RequiredExperience(level))
        { remaining -= RequiredExperience(level++); }
        progression["level"] = JsonSerializer.SerializeToElement(level);
        progression["experience"] = JsonSerializer.SerializeToElement(level == MaxLevel ? 0 : remaining);
        progression["experience_fifths"] = JsonSerializer.SerializeToElement(units % 5);
        progression["total_experience"] = JsonSerializer.SerializeToElement(total);
        return level - previous;
    }

    public static (int Wave, int Difficulty, int InfiniteLevel) NextWave(int wave, int difficulty, int infiniteLevel)
    {
        if (difficulty != 3 && wave == 100)
        { return (1, difficulty + 1, 1 + (wave - 1) / new[] { 2, 4, 8 }[difficulty]); }
        return (checked(wave + 1), difficulty, infiniteLevel);
    }
}
