using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 部署只引用角色；裝備與技能由角色目前配置取得。
    /// </summary>
    public class GameProgressDeploymentModel
    {
        [JsonPropertyName("spot_index")]
        public int SpotIndex { get; set; }

        [JsonPropertyName("character_id")]
        public Guid CharacterId { get; set; }

        [JsonPropertyName("wave")]
        public int Wave { get; set; }

        /// <summary>
        /// 格式為 [x, y]，必須恰好兩個元素
        /// </summary>
        [JsonPropertyName("cell")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<int> Cell { get; set; }
    }
}
