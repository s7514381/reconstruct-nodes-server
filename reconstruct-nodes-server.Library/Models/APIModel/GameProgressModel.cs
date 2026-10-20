using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 遊戲進度快照，欄位沿用前端 save_service 的 payload（snake_case）。
    /// 字典型欄位依 key 拆成子表，值保留為原始 JSON，因此不限制其內部型別。
    /// </summary>
    public class GameProgressModel : RevisionedSnapshot
    {
        [JsonPropertyName("request_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Guid? RequestId { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("deployment_log")]
        public List<GameProgressDeploymentModel> DeploymentLog { get; set; } = new();

        /// <summary>
        /// 隊長：必須是 DeploymentLog 內的角色；沒有部署角色或舊資料尚未指定時為 null（前端依規則自動選擇）。
        /// </summary>
        [JsonPropertyName("captain_character_id")]
        public Guid? CaptainCharacterId { get; set; }

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

        [JsonPropertyName("progression")]
        public Dictionary<string, JsonElement> Progression { get; set; } = new();

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

        /// <summary>進入試煉前的一般模式難度；試煉結束回到此處。非試煉時僅保留舊值。</summary>
        [JsonPropertyName("trial_return_difficulty")]
        public int TrialReturnDifficulty { get; set; }

        [JsonPropertyName("trial_return_wave")]
        public int TrialReturnWave { get; set; } = 1;
    }
}
