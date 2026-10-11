using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 單一角色或部署所持有的武器快照；欄位與前端 WeaponInstance.serialize_equipment 相同。
    /// 空武器時 weapon_id 與 weapon_entry_id 為空字串，weapon_affixes 為空陣列，weapon_unique 為空字典。
    /// </summary>
    public class GameProgressEquipmentModel
    {
        [JsonPropertyName("weapon_id")]
        public string WeaponId { get; set; } = string.Empty;

        [JsonPropertyName("weapon_entry_id")]
        public string WeaponEntryId { get; set; } = string.Empty;

        [JsonPropertyName("weapon_rarity")]
        public string WeaponRarity { get; set; } = string.Empty;

        [JsonPropertyName("weapon_affixes")]
        public List<JsonElement> WeaponAffixes { get; set; } = new();

        [JsonPropertyName("weapon_unique")]
        public Dictionary<string, JsonElement> WeaponUnique { get; set; } = new();
    }
}
