using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    public class CharacterModel : GameProgressEquipmentModel
    {
        [JsonPropertyName("id")]
        public Guid? Id { get; set; }

        [JsonPropertyName("kind")]
        public int Kind { get; set; }

        [JsonPropertyName("skill_trees")]
        public List<GameProgressSkillLoadoutModel> SkillTrees { get; set; } = new();

        [JsonPropertyName("weapon_quality_version")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? WeaponQualityVersion { get; set; }
    }
}
