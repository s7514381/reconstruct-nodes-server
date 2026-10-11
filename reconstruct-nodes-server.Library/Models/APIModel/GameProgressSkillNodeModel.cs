using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 技能節點。type 為 "auxiliary"（需要 loot_id）或 "active"（需要 kind）。
    /// </summary>
    public class GameProgressSkillNodeModel
    {
        public const string AuxiliaryType = "auxiliary";
        public const string ActiveType = "active";

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("loot_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string LootId { get; set; }

        [JsonPropertyName("kind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Kind { get; set; }
    }
}
