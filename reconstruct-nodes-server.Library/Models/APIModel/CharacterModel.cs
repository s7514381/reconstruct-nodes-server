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
        public CharacterKind Kind { get; set; }

        [JsonPropertyName("skill_trees")]
        public List<GameProgressSkillLoadoutModel> SkillTrees { get; set; } = new();

        [JsonPropertyName("strength")]
        public int Strength { get; set; }

        [JsonPropertyName("intelligence")]
        public int Intelligence { get; set; }

        [JsonPropertyName("dexterity")]
        public int Dexterity { get; set; }

        [JsonPropertyName("skill_link_count")]
        public int SkillLinkCount { get; set; } = SkillLinkRules.InitialCount;

        [JsonPropertyName("weapon_quality_version")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? WeaponQualityVersion { get; set; }
    }
}
