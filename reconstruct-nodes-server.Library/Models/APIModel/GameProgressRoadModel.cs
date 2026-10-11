using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 戰場道路格。
    /// </summary>
    public class GameProgressRoadModel
    {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("corner")]
        public bool Corner { get; set; }

        [JsonPropertyName("rotation")]
        public int Rotation { get; set; }
    }
}
