using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Linq;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 角色的一棵技能樹。auxiliary_nodes 同時包含輔助與主動節點，
    /// auxiliary_gem_ids 由輔助節點推導，只用於輸出，不會被儲存。
    /// </summary>
    public class GameProgressSkillLoadoutModel
    {
        [JsonPropertyName("kind")]
        public int Kind { get; set; }

        [JsonPropertyName("auxiliary_nodes")]
        public List<GameProgressSkillNodeModel> AuxiliaryNodes { get; set; } = new();

        [JsonPropertyName("auxiliary_gem_ids")]
        public List<string> AuxiliaryGemIds => (AuxiliaryNodes ?? new List<GameProgressSkillNodeModel>())
            .Where(node => node != null && node.Type == GameProgressSkillNodeModel.AuxiliaryType && !string.IsNullOrEmpty(node.LootId))
            .Select(node => node.LootId)
            .ToList();
    }
}
