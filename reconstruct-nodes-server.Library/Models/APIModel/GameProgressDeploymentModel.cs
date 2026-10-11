using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 部署紀錄，繼承武器欄位；cell 與 weapon_quality_version 僅在前端有值時輸出。
    /// </summary>
    public class GameProgressDeploymentModel : GameProgressEquipmentModel
    {
        [JsonPropertyName("spot_index")]
        public int SpotIndex { get; set; }

        [JsonPropertyName("character_name")]
        public string CharacterName { get; set; } = string.Empty;

        [JsonPropertyName("wave")]
        public int Wave { get; set; }

        [JsonPropertyName("kind")]
        public int Kind { get; set; }

        [JsonPropertyName("skill_loadout")]
        public List<GameProgressSkillLoadoutModel> SkillLoadout { get; set; } = new();

        [JsonPropertyName("weapon_quality_version")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? WeaponQualityVersion { get; set; }

        /// <summary>
        /// 格式為 [x, y]，必須恰好兩個元素
        /// </summary>
        [JsonPropertyName("cell")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<int> Cell { get; set; }
    }
}
