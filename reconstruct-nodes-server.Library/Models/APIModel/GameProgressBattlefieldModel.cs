using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 戰場快照，欄位與前端 battlefield_layout.serialize 相同。
    /// </summary>
    public class GameProgressBattlefieldModel
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("columns")]
        public int Columns { get; set; }

        [JsonPropertyName("rows")]
        public int Rows { get; set; }

        /// <summary>
        /// 格式為 [x, y]，必須恰好兩個元素
        /// </summary>
        [JsonPropertyName("castle")]
        public List<int> Castle { get; set; } = new();

        [JsonPropertyName("roads")]
        public List<GameProgressRoadModel> Roads { get; set; } = new();
    }
}
