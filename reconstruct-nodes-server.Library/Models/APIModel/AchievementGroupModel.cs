using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 成就群組：Name 為群組 id，Total 為目前進度，Collected 為已收集的武器或詞綴 id。
    /// </summary>
    public class AchievementGroupModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("total")]
        public float Total { get; set; }

        [JsonPropertyName("items")]
        public List<AchievementItemModel> Items { get; set; } = new();

        [JsonPropertyName("collected")]
        public List<string> Collected { get; set; } = new();
    }
}
