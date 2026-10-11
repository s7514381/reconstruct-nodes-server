using System;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 成就項目：Name 為項目 id。IsReward 與 RewardDate 由伺服器維護，用戶端儲存時送來的值會被忽略。
    /// </summary>
    public class AchievementItemModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("target")]
        public int Target { get; set; }

        [JsonPropertyName("is_reward")]
        public bool IsReward { get; set; }

        [JsonPropertyName("reward_date")]
        public DateTime? RewardDate { get; set; }
    }
}
