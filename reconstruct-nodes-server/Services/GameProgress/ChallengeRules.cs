using System.Text.Json;
using System.Text.Json.Serialization;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 挑戰關卡（StageRules 難度槽 5 起）的每日次數與通關獎勵規則唯一來源。
/// 次數以 PlayerParams（economy / challenge_attempts_{難度}）保存 {date, used, rewarded}：
/// 進入挑戰時（TransitionWave）原子扣 1 次，日期（伺服器本地日）變更即重置；
/// 通關獎勵每次進入最多領一次（rewarded），由 <see cref="ChallengeCommand"/> 的領取交易寫入。
/// </summary>
public static class ChallengeRules
{
    public const int DailyLimit = 3;
    public const string RewardItemKey = "energy_extractor";
    public const int RewardAmount = 3;
    public const string Group = "economy";
    private const string NamePrefix = "challenge_attempts_";

    public static IEnumerable<int> Difficulties =>
        Enumerable.Range(StageRules.FirstChallenge, StageRules.SlotCount - StageRules.FirstChallenge);

    public static string AttemptName(int difficulty) => NamePrefix + difficulty;

    public static IEnumerable<string> AttemptNames => Difficulties.Select(AttemptName);

    /// <summary>LIKE 條件：底線以 [_] 表示字面底線。</summary>
    internal const string AttemptRowFilter = "[Group] = 'economy' AND Name LIKE 'challenge[_]attempts[_]%'";

    public sealed class Attempts
    {
        [JsonPropertyName("date")]
        public string Date { get; set; } = "";
        [JsonPropertyName("used")]
        public int Used { get; set; }
        [JsonPropertyName("rewarded")]
        public bool Rewarded { get; set; }

        public int Remaining => Math.Max(0, DailyLimit - Used);

        public string Serialize() => JsonSerializer.Serialize(this);

        /// <summary>讀取已保存的次數（不做換日）；沒有紀錄回傳 null，內容損壞視為資料錯誤。</summary>
        public static Attempts? Parse(string? value) => string.IsNullOrEmpty(value)
            ? null
            : JsonSerializer.Deserialize<Attempts>(value) ?? throw new InvalidOperationException("Invalid challenge attempts.");

        /// <summary>今天的次數：沒有紀錄或已換日都從 0 開始。</summary>
        public static Attempts ForToday(Attempts? stored, string todayKey) =>
            stored != null && stored.Date == todayKey ? stored : new Attempts { Date = todayKey };
    }

    public static PlayerParams? FindAttemptRow(IEnumerable<PlayerParams> rows, int difficulty) =>
        rows.SingleOrDefault(row => row.Group == Group && row.Name == AttemptName(difficulty));

    /// <summary>
    /// 進入挑戰：扣 1 次並重置獎勵旗標。更新（或新增）記憶體內的列並回傳，由呼叫端寫入；沒有次數時拒絕。
    /// </summary>
    internal static (PlayerParams Row, bool Created) Consume(List<PlayerParams> rows, int difficulty, string todayKey)
    {
        PlayerParams? row = FindAttemptRow(rows, difficulty);
        Attempts today = Attempts.ForToday(Attempts.Parse(row?.Value), todayKey);
        if (today.Remaining < 1) { throw new BusinessOperationException(ApiMessageKeys.ChallengeNoAttempts); }
        string value = new Attempts { Date = todayKey, Used = today.Used + 1, Rewarded = false }.Serialize();
        if (row != null)
        {
            row.Value = value;
            return (row, false);
        }
        row = new PlayerParams { Group = Group, Name = AttemptName(difficulty), Value = value };
        rows.Add(row);
        return (row, true);
    }

    public static ChallengeStatus BuildStatus(IReadOnlyCollection<PlayerParams> rows, string todayKey) => new()
    {
        Today = todayKey,
        Stages = Difficulties.Select(difficulty => new ChallengeStageStatusModel
        {
            Difficulty = difficulty,
            DailyLimit = DailyLimit,
            Remaining = Attempts.ForToday(Attempts.Parse(FindAttemptRow(rows, difficulty)?.Value), todayKey).Remaining,
            RewardItemId = RewardItemKey,
            RewardAmount = RewardAmount,
        }).ToList(),
    };
}
