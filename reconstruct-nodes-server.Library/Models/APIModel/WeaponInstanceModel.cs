using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 單把背包武器快照，欄位沿用前端存檔 JSON（snake_case），前端可直接送出既有字典。
    /// affixes 元素為純字串（未擲骰）或 {id, values}；unique 為空字典或 {unique_id, values}。
    /// </summary>
    public class WeaponInstanceModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("weapon_id")]
        public string WeaponId { get; set; } = string.Empty;

        [JsonPropertyName("weapon_family_id")]
        public string WeaponFamilyId { get; set; } = string.Empty;

        [JsonPropertyName("rarity")]
        public string Rarity { get; set; } = string.Empty;

        [JsonPropertyName("affixes")]
        public List<JsonElement> Affixes { get; set; } = new();

        [JsonPropertyName("unique")]
        public Dictionary<string, JsonElement> Unique { get; set; } = new();
    }
}
