using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 成就進度快照，欄位沿用前端成就群組資料（snake_case）。
    /// </summary>
    public class AchievementProgressModel
    {
        [JsonPropertyName("groups")]
        public List<AchievementGroupModel> Groups { get; set; } = new();
    }
}
