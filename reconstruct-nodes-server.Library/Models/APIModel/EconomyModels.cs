using System;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    public class WaveStartRequest
    {
        [JsonPropertyName("request_id")]
        public Guid RequestId { get; set; }
        [JsonPropertyName("wave")]
        public int Wave { get; set; }
        [JsonPropertyName("difficulty")]
        public int Difficulty { get; set; }
    }

    public class EnemyKillRequest
    {
        [JsonPropertyName("request_id")]
        public Guid RequestId { get; set; }
        [JsonPropertyName("session_id")]
        public Guid SessionId { get; set; }
        [JsonPropertyName("enemy_index")]
        public int EnemyIndex { get; set; }
    }

    public class SkillPurchaseRequest
    {
        [JsonPropertyName("request_id")]
        public Guid RequestId { get; set; }
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        [JsonPropertyName("kind")]
        public int? Kind { get; set; }
        [JsonPropertyName("loot_id")]
        public string LootId { get; set; } = string.Empty;
    }

    public class GoldOperationResult
    {
        [JsonPropertyName("gold")]
        public long Gold { get; set; }
    }

    public class WaveStartResult : GoldOperationResult
    {
        [JsonPropertyName("session_id")]
        public Guid SessionId { get; set; }
        [JsonPropertyName("wave")]
        public int Wave { get; set; }
        [JsonPropertyName("enemy_count")]
        public int EnemyCount { get; set; }
    }
}
