using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>Formal editing rules from CharacterSkillLoadout, SkillValidationService and SkillChainRules.</summary>
public static class SkillLoadoutRules
{
    public static void Validate(List<GameProgressSkillLoadoutModel>? trees, int linkCount)
    {
        if (trees == null) { throw new BusinessArgumentException(ApiMessageKeys.SkillTreeRequired); }
        HashSet<int> roots = [];
        HashSet<string> supports = [];
        int points = 0;
        foreach (var tree in trees)
        {
            if (tree == null || tree.AuxiliaryNodes == null) { throw new BusinessArgumentException(ApiMessageKeys.SkillTreeRequired); }
            if (tree.Kind is < 0 or > 6 || !roots.Add(tree.Kind))
            { throw new BusinessArgumentException("skill.loadout.duplicate_or_invalid_root"); }
            if (tree.AuxiliaryNodes.Count + 1 > Math.Clamp(linkCount, SkillLinkRules.InitialCount, SkillLinkRules.MaxCount)) { throw new BusinessArgumentException("skill.loadout.chain_limit"); }
            points = checked(points + tree.AuxiliaryNodes.Count(n => n?.Type != GameProgressSkillNodeModel.EmptyType) + 1);
            bool attackSource = tree.Kind is 0 or 2 or 3 or 4 or 5;
            bool trigger = false, aura = false;
            HashSet<int> triggeredSkills = [];
            foreach (var node in tree.AuxiliaryNodes)
            {
                if (node == null) { throw new BusinessArgumentException(ApiMessageKeys.SkillNodeRequired); }
                if (node.Type == GameProgressSkillNodeModel.EmptyType) { continue; }
                if (node.Type == GameProgressSkillNodeModel.AuxiliaryType)
                {
                    if (string.IsNullOrWhiteSpace(node.LootId) || !supports.Add(node.LootId))
                    { throw new BusinessArgumentException("skill.loadout.duplicate_support"); }
                    trigger = node.LootId is "critical_trigger_gem" or "hit_trigger_gem";
                    if (trigger && !attackSource) { throw new BusinessOperationException("skill.chain.trigger_source_not_repeatable"); }
                    if (node.LootId == "aura_support")
                    {
                        if (tree.Kind != 6 || aura) { throw new BusinessOperationException("skill.chain.aura_requires_buff"); }
                        aura = true;
                    }
                }
                else if (node.Type == GameProgressSkillNodeModel.ActiveType)
                {
                    // 失去觸發來源的被觸發技能（例如來源被清空）保留在原位，戰鬥時忽略，不阻擋儲存。
                    if (!trigger) { continue; }
                    if (!attackSource || node.Kind != 1 || !triggeredSkills.Add(node.Kind.Value))
                    { throw new BusinessOperationException("skill.chain.trigger_target_spell"); }
                    trigger = false;
                }
                else { throw new BusinessArgumentException(ApiMessageKeys.SkillNodeTypeInvalid); }
            }
        }
        // TowerData: all current characters have 100 points and every node costs 10.
        if (points > 10) { throw new BusinessOperationException("skill.loadout.points_insufficient"); }
    }
}
