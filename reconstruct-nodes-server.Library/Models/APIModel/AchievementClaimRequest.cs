using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 領取成就獎勵的請求，AchievementItemId 為成就項目 id（例如 enemy_kills_10）。
    /// </summary>
    public class AchievementClaimRequest
    {
        [JsonPropertyName("achievement_item_id")]
        public string AchievementItemId { get; set; } = string.Empty;
    }
}
