using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>Formal editing rules from CharacterSkillLoadout, SkillValidationService and SkillChainRules.</summary>
public static class SkillLoadoutRules
{
    /// <summary>遊戲端 ActiveSkill 的最大穩定 kind（FURNACE_METEOR = 21）；購買與技能配置驗證共用，新增主動技能時同步調整。</summary>
    public const int MaxActiveSkillKind = 21;

    /// <summary>具「攻擊」標籤的主動技能（ActiveSkill 的 BASIC、ICE、LIGHTNING、SWORD_QI、SLASH，以及 kind 7–16 的近戰與攻擊投射物）；只有它們能作為觸發來源。</summary>
    public static bool IsAttackSource(int kind) => kind is 0 or 2 or 3 or 4 or 5 or (>= 7 and <= 16);

    /// <summary>具「法術」標籤且非增益的主動技能（火球與 kind 17–21 的法術）；觸發只能施放它們。</summary>
    public static bool IsTriggerSpell(int kind) => kind is 1 or (>= 17 and <= 21);

    public static void Validate(List<GameProgressSkillLoadoutModel>? trees, int linkCount)
    {
        if (trees == null) { throw new BusinessArgumentException(ApiMessageKeys.SkillTreeRequired); }
        HashSet<int> roots = [];
        HashSet<string> supports = [];
        int points = 0;
        foreach (var tree in trees)
        {
            if (tree == null || tree.AuxiliaryNodes == null) { throw new BusinessArgumentException(ApiMessageKeys.SkillTreeRequired); }
            if (tree.Kind is < 0 or > MaxActiveSkillKind || !roots.Add(tree.Kind))
            { throw new BusinessArgumentException("skill.loadout.duplicate_or_invalid_root"); }
            if (tree.AuxiliaryNodes.Count + 1 > Math.Clamp(linkCount, SkillLinkRules.InitialCount, SkillLinkRules.MaxCount)) { throw new BusinessArgumentException("skill.loadout.chain_limit"); }
            points = checked(points + tree.AuxiliaryNodes.Count(n => n?.Type != GameProgressSkillNodeModel.EmptyType) + 1);
            bool attackSource = IsAttackSource(tree.Kind);
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
                    trigger = node.LootId is "critical_trigger_gem" or "hit_trigger_gem" or "stun_trigger_gem";
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
                    if (!attackSource || !IsTriggerSpell(node.Kind ?? -1) || !triggeredSkills.Add(node.Kind.Value))
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
