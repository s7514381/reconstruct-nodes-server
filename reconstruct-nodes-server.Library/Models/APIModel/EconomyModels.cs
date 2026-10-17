using System;
using System.Collections.Generic;
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
        [JsonIgnore]
        public int InfiniteLevel { get; set; } = 1;
    }

    /// <summary>Kills of one wave session; replays are deduplicated by the kill ledger per (session, index).</summary>
    public class SessionKillsRequest
    {
        [JsonPropertyName("session_id")]
        public Guid SessionId { get; set; }
        [JsonPropertyName("enemy_indices")]
        public List<int> EnemyIndices { get; set; } = [];
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
        [JsonPropertyName("difficulty")]
        public int Difficulty { get; set; }
        [JsonPropertyName("infinite_level")]
        public int InfiniteLevel { get; set; } = 1;
    }

    public sealed class WaveCompleteRequest : CommandRequest
    {
        [JsonPropertyName("session_id")]
        public Guid SessionId { get; set; }
    }

    public sealed class WaveCompleteResult
    {
        [JsonPropertyName("progress")]
        public required GameProgressModel Progress { get; init; }
        [JsonPropertyName("next_wave")]
        public int NextWave { get; init; }
        [JsonPropertyName("next_difficulty")]
        public int NextDifficulty { get; init; }
        [JsonPropertyName("next_infinite_level")]
        public int NextInfiniteLevel { get; init; }
    }
}
