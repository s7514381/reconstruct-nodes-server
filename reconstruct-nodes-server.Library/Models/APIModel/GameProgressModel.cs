using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 遊戲進度快照，欄位沿用前端 save_service 的 payload（snake_case）。
    /// 字典型欄位依 key 拆成子表，值保留為原始 JSON，因此不限制其內部型別。
    /// </summary>
    public class GameProgressModel
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("deployment_log")]
        public List<GameProgressDeploymentModel> DeploymentLog { get; set; } = new();

        [JsonPropertyName("wave")]
        public int Wave { get; set; }

        [JsonPropertyName("gold")]
        public long Gold { get; set; }

        [JsonPropertyName("max_wave_reached")]
        public int MaxWaveReached { get; set; }

        [JsonPropertyName("unlocked_bullet_kinds")]
        public Dictionary<string, JsonElement> UnlockedBulletKinds { get; set; } = new();

        [JsonPropertyName("unlocked_auxiliary_kinds")]
        public Dictionary<string, JsonElement> UnlockedAuxiliaryKinds { get; set; } = new();

        [JsonPropertyName("unlocked_character_kinds")]
        public Dictionary<string, JsonElement> UnlockedCharacterKinds { get; set; } = new();

        [JsonPropertyName("skill_suffixes")]
        public Dictionary<string, JsonElement> SkillSuffixes { get; set; } = new();

        [JsonPropertyName("refine_item_counts")]
        public Dictionary<string, JsonElement> RefineItemCounts { get; set; } = new();

        [JsonPropertyName("progression")]
        public Dictionary<string, JsonElement> Progression { get; set; } = new();

        /// <summary>
        /// 鍵為角色種類（tower kind 的整數字串）
        /// </summary>
        [JsonPropertyName("character_weapons")]
        public Dictionary<string, GameProgressEquipmentModel> CharacterWeapons { get; set; } = new();

        /// <summary>
        /// 前端沒有戰場時整個欄位不輸出，因此為 null 時不寫入 JSON
        /// </summary>
        [JsonPropertyName("battlefield")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GameProgressBattlefieldModel Battlefield { get; set; }

        [JsonPropertyName("difficulty")]
        public int Difficulty { get; set; }

        [JsonPropertyName("lifetime_enemies_defeated")]
        public long LifetimeEnemiesDefeated { get; set; }

        [JsonPropertyName("infinite_level")]
        public int InfiniteLevel { get; set; }

        [JsonPropertyName("cleared_waves")]
        public List<JsonElement> ClearedWaves { get; set; } = new();

        [JsonPropertyName("wave_completed")]
        public bool WaveCompleted { get; set; }
    }
}
