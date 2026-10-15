using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 天賦樹存檔快照，欄位沿用前端 talent_tree_manager 的 payload（snake_case）。
    /// 節點值保留為原始 JSON，由服務層檢查型別。
    /// </summary>
    public class TalentModel : RevisionedSnapshot
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("talent_points")]
        public long TalentPoints { get; set; }

        [JsonPropertyName("total_spent_points")]
        public long TotalSpentPoints { get; set; }

        [JsonPropertyName("points_recalculation_version")]
        public int PointsRecalculationVersion { get; set; }

        /// <summary>
        /// 鍵為樹 id，值為節點 id 與級數
        /// </summary>
        [JsonPropertyName("acquired_talents")]
        public Dictionary<string, Dictionary<string, JsonElement>> AcquiredTalents { get; set; } = new();

        /// <summary>
        /// 鍵為舊分支樹 id，值為節點 id 與是否取得
        /// </summary>
        [JsonPropertyName("legacy_category_talents")]
        public Dictionary<string, Dictionary<string, JsonElement>> LegacyCategoryTalents { get; set; } = new();
    }
}
