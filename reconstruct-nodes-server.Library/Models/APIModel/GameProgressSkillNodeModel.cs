using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 技能節點。type 為 "auxiliary"（需要 loot_id）、"active"（需要 kind）或 "empty"（保留位置的空格）。
    /// </summary>
    public class GameProgressSkillNodeModel
    {
        public const string AuxiliaryType = "auxiliary";
        public const string ActiveType = "active";
        /// <summary>被清空但保留位置的節點；後方節點維持原位，戰鬥時忽略。</summary>
        public const string EmptyType = "empty";

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
