using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    public class ShopDrawRequest
    {
        [JsonPropertyName("request_id")]
        public Guid RequestId { get; set; }
        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    public class ShopDrawResult
    {
        [JsonPropertyName("request_id")]
        public Guid RequestId { get; set; }
        [JsonPropertyName("gold")]
        public long Gold { get; set; }
        [JsonPropertyName("results")]
        public List<ShopDrawReward> Results { get; set; } = new();
    }

    public class ShopDrawReward
    {
        [JsonPropertyName("character_id")]
        public Guid CharacterId { get; set; }
        [JsonPropertyName("character_kind")]
        public int CharacterKind { get; set; }
        [JsonPropertyName("is_duplicate")]
        public bool IsDuplicate { get; set; }
        [JsonPropertyName("shard_id")]
        public string ShardId { get; set; } = string.Empty;
        [JsonPropertyName("weapon_entry_id")]
        public string WeaponEntryId { get; set; } = string.Empty;
    }
}
