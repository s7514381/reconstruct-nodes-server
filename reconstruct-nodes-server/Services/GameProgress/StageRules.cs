namespace ReconstructNodesServer.Services;

/// <summary>
/// 關卡（難度槽）規則的唯一來源：0 入門、1 普通、2 困難、3 無限、4 起為試煉關卡（4 = 試煉1）、5 起為挑戰關卡（5 = 挑戰1）。
/// 每個槽各有通關紀錄（ClearedWaves[index]）；試煉與挑戰沿用入門的怪物等級曲線，但波數上限與通關紀錄獨立。
/// 試煉與挑戰同屬「限定關卡」：進入時記下一般模式返回點、最後一波通關後只能離開，不會自動開下一波。
/// </summary>
public static class StageRules
{
    public const int Infinite = 3;
    public const int FirstTrial = 4;
    public const int FirstChallenge = 5;
    /// <summary>ClearedWaves 的槽數量；新增試煉或挑戰關卡時同步增加。</summary>
    public const int SlotCount = 6;
    public const int FiniteWaves = 100;
    public const int TrialWaves = 10;
    public const int ChallengeWaves = 10;

    private static readonly int[] LevelSteps = [2, 4, 8, 1, 2, 2];

    public static bool IsValid(int difficulty) => difficulty is >= 0 and < SlotCount;

    public static bool IsTrial(int difficulty) => difficulty is >= FirstTrial and < FirstChallenge;

    public static bool IsChallenge(int difficulty) => difficulty >= FirstChallenge;

    /// <summary>試煉或挑戰：有固定波數、離開後回到進入前的一般模式位置。</summary>
    public static bool IsLimitedStage(int difficulty) => difficulty >= FirstTrial;

    /// <summary>難度 0~2 有 100 波上限；試煉與挑戰 10 波；無限沒有上限。</summary>
    public static int MaxWave(int difficulty) =>
        difficulty == Infinite ? int.MaxValue : IsChallenge(difficulty) ? ChallengeWaves : IsTrial(difficulty) ? TrialWaves : FiniteWaves;

    public static bool IsValidTarget(int difficulty, int wave) =>
        IsValid(difficulty) && wave >= 1 && wave <= MaxWave(difficulty);

    public static int EnemyLevel(int difficulty, int wave, int infiniteLevel) =>
        difficulty == Infinite ? infiniteLevel : 1 + (Math.Max(1, wave) - 1) / LevelSteps[difficulty];

    /// <summary>限定關卡最後一波已通關：之後只能由玩家選擇關卡離開，不會自動開下一波。</summary>
    public static bool IsStageFinished(int difficulty, int wave, bool waveCompleted) =>
        IsLimitedStage(difficulty) && waveCompleted && wave >= MaxWave(difficulty);
}
