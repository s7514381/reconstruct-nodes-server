using System.Text.Json;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>
/// Transaction metadata verified against the client's TalentTreeLayout / TalentCatalog (8 boards x 22 nodes).
/// Stage 1: 1-3 (3 levels). Stage 2: 4-9, stage 3: 10-15 (2 levels; first three are upgrades of the previous
/// stage's first three). Stage 4: 16-20 (1 level; 16-18 upgrade 13-15). Stage 5: keystones 21/22 (1 level, mutually
/// exclusive). Effects are intentionally absent.
/// </summary>
public static class TalentRules
{
    public sealed record Node(string Id, int Stage, int MaxLevel, string? Prerequisite, string? Exclusive = null);

    /// <summary>Client save version that introduced the 8-board layout.</summary>
    public const int CurrentVersion = 4;

    private static readonly int[] StageStarts = [1, 4, 10, 16, 21];
    private static readonly int[] StageSizes = [3, 6, 6, 5, 2];
    private static readonly int[] StageMaxLevels = [3, 2, 2, 1, 1];
    private static readonly int[] StageRequirements = [0, 6, 6, 6, 4];

    private static readonly string[] RemovedTrees =
    [
        "weapon_sword", "weapon_staff", "weapon_bow", "element_fire", "element_ice",
        "element_lightning", "special_critical", "special_cooldown", "special_mana", "element_poison",
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Node>> Trees =
        new Dictionary<string, IReadOnlyDictionary<string, Node>>
        {
            ["t_strength"] = Build("str"),
            ["t_dexterity"] = Build("dex"),
            ["t_intelligence"] = Build("int"),
            ["t_fire"] = Build("fire"),
            ["t_ice"] = Build("ice"),
            ["t_lightning"] = Build("ltn"),
            ["t_critical"] = Build("crit"),
            ["t_swift"] = Build("swift"),
        };

    private static IReadOnlyDictionary<string, Node> Build(string prefix) =>
        Enumerable.Range(1, StageSizes.Sum()).Select(number =>
        {
            int stage = Array.FindLastIndex(StageStarts, start => number >= start) + 1;
            int offset = number - StageStarts[stage - 1];
            string? prerequisite = stage is >= 2 and <= 4 && offset < 3 ? $"{prefix}_{number - 3:D2}" : null;
            string? exclusive = stage == 5 ? $"{prefix}_{(number == 21 ? 22 : 21):D2}" : null;
            return new Node($"{prefix}_{number:D2}", stage, StageMaxLevels[stage - 1], prerequisite, exclusive);
        }).ToDictionary(node => node.Id);

    public static Node Get(string tree, string node) =>
        Trees.TryGetValue(tree, out var nodes) && nodes.TryGetValue(node, out var result)
            ? result : throw new BusinessArgumentException("talent.node_unknown");

    public static int Level(Dictionary<string, JsonElement> progress, string id) =>
        progress.TryGetValue(id, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int level) && level >= 0
            ? level : progress.ContainsKey(id) ? throw new BusinessArgumentException("talent.progress_invalid") : 0;

    private static bool DependenciesMet(string tree, Node node, Dictionary<string, JsonElement> progress)
    {
        var nodes = Trees[tree];
        if (node.Prerequisite != null && Level(progress, node.Prerequisite) < nodes[node.Prerequisite].MaxLevel) { return false; }
        if (node.Exclusive != null && Level(progress, node.Exclusive) > 0) { return false; }
        return node.Stage == 1 || nodes.Values.Where(n => n.Stage == node.Stage - 1).Sum(n => Math.Min(n.MaxLevel, Level(progress, n.Id)))
            >= StageRequirements[node.Stage - 1];
    }

    public static void Learn(TalentModel model, string tree, string id)
    {
        Node node = Get(tree, id);
        if (!model.AcquiredTalents.TryGetValue(tree, out var progress)) { model.AcquiredTalents[tree] = progress = []; }
        int level = Level(progress, id);
        if (level >= node.MaxLevel || !DependenciesMet(tree, node, progress) || model.TalentPoints < 1)
        {
            throw new BusinessOperationException("talent.learn_unavailable");
        }
        progress[id] = JsonSerializer.SerializeToElement(level + 1);
        model.TalentPoints--;
        model.TotalSpentPoints = checked(model.TotalSpentPoints + 1);
    }

    // Client removes the entire node, not just its last level.
    public static void RefundNode(TalentModel model, string tree, string id)
    {
        Node node = Get(tree, id);
        if (!model.AcquiredTalents.TryGetValue(tree, out var progress) || Level(progress, id) == 0)
        {
            throw new BusinessOperationException("talent.node_not_acquired");
        }
        int refund = Math.Min(node.MaxLevel, Level(progress, id));
        var candidate = new Dictionary<string, JsonElement>(progress);
        candidate.Remove(id);
        foreach (string remaining in candidate.Keys)
        {
            if (Level(candidate, remaining) > 0 && !DependenciesMet(tree, Get(tree, remaining), candidate))
            {
                throw new BusinessOperationException("talent.refund_dependency");
            }
        }
        model.AcquiredTalents[tree] = candidate;
        if (candidate.Count == 0) { model.AcquiredTalents.Remove(tree); }
        Refund(model, refund);
    }

    /// <summary>
    /// One-time redesign migration: the retired boards (nine old boards and poison) (and their placeholder nodes) are removed and every
    /// invested point is refunded. The three legacy trees are untouched. Returns the refunded points.
    /// </summary>
    public static int MigrateRemovedTrees(TalentModel model)
    {
        int refund = 0;
        foreach (string tree in RemovedTrees)
        {
            if (model.AcquiredTalents.Remove(tree, out var progress))
            {
                foreach (var entry in progress)
                {
                    refund = checked(refund + (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out int level) && level > 0
                        ? Math.Min(level, 3) : entry.Value.ValueKind == JsonValueKind.True ? LegacyCost(tree, entry.Key) : 0));
                }
            }
            if (model.LegacyCategoryTalents.Remove(tree, out var legacy))
            {
                foreach (var entry in legacy)
                {
                    if (entry.Value.ValueKind == JsonValueKind.True) { refund = checked(refund + LegacyCost(tree, entry.Key)); }
                }
            }
        }
        if (refund > 0) { Refund(model, refund); }
        model.Version = Math.Max(model.Version, CurrentVersion);
        return refund;
    }

    public static void Reset(TalentModel model, string? tree)
    {
        var trees = tree == null
            ? model.AcquiredTalents.Keys.Concat(model.LegacyCategoryTalents.Keys).Distinct().ToArray() : [tree];
        int refund = 0;
        foreach (string current in trees)
        {
            if (model.AcquiredTalents.Remove(current, out var progress))
            {
                foreach (var entry in progress)
                {
                    int amount;
                    if (Trees.TryGetValue(current, out var nodes) && nodes.TryGetValue(entry.Key, out var node))
                    {
                        amount = Math.Min(node.MaxLevel, Level(progress, entry.Key));
                    }
                    else
                    {
                        // Historical category placeholders and legacy trees had one level.
                        amount = LegacyCost(current, entry.Key);
                    }
                    refund = checked(refund + amount);
                }
            }
            if (model.LegacyCategoryTalents.Remove(current, out var legacy))
            {
                foreach (string id in legacy.Keys) { refund = checked(refund + LegacyCost(current, id)); }
            }
        }
        Refund(model, refund);
    }

    private static int LegacyCost(string tree, string id)
    {
        if (Trees.TryGetValue(tree, out var nodes) && nodes.ContainsKey(id)) { return 1; }
        if (tree is not ("offense" or "defense" or "utility"))
        {
            return id switch { "foundation" => 1, "advanced" => 2, "mastery" => 3, _ => 1 };
        }
        return id switch
        {
            "inc_dmg_1" or "inc_dmg_2" or "inc_fire_1" or "inc_ice_1" or "inc_lightning_1" or
            "castle_hp_1" or "dmg_reduce_1" or "gold_1" => 1,
            "gold_3" => 3,
            "inc_dmg_3" or "inc_fire_2" or "inc_ice_2" or "inc_lightning_2" or "inc_atk_speed" or
            "castle_hp_2" or "dmg_reduce_2" or "inc_range" or "gold_2" or "inc_dmg_utility" or "inc_atk_speed_utility" => 2,
            _ => 1,
        };
    }

    private static void Refund(TalentModel model, int amount)
    {
        model.TalentPoints = checked(model.TalentPoints + amount);
        model.TotalSpentPoints = Math.Max(0, model.TotalSpentPoints - amount);
    }
}
