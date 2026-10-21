#nullable enable
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
        /// <summary>新手選角：指定戰士／弓箭手／法師之一，免費且僅限尚未擁有一般角色時；一般抽取為 null。</summary>
        [JsonPropertyName("starter_kind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CharacterKind? StarterKind { get; set; }
    }

    public class ShopDrawResult
    {
        [JsonPropertyName("request_id")]
        public Guid RequestId { get; set; }
        [JsonPropertyName("gold")]
        public long Gold { get; set; }
        [JsonPropertyName("results")]
        public List<ShopDrawReward> Results { get; set; } = new();
        [JsonPropertyName("state")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ShopDrawState? State { get; set; }
    }

    public class ShopDrawState
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;
        [JsonPropertyName("characters")]
        public List<CharacterModel> Characters { get; set; } = new();
        [JsonPropertyName("weapons")]
        public List<WeaponInstanceModel> Weapons { get; set; } = new();
    }

    public class ShopDrawReward
    {
        [JsonPropertyName("character_id")]
        public Guid CharacterId { get; set; }
        [JsonPropertyName("character_kind")]
        public CharacterKind CharacterKind { get; set; }
        [JsonPropertyName("weapon_entry_id")]
        public string WeaponEntryId { get; set; } = string.Empty;
    }
}
