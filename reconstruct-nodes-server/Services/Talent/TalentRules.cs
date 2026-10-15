using System.Text.Json;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>
/// Transaction metadata verified against TalentPointRegistry and ManaTalentPoint.
/// Ordering is by stage/branch, NOT numeric node id. Effects are intentionally absent.
/// </summary>
public static class TalentRules
{
    public sealed record Node(string Id, int Stage, int MaxLevel, string? Prerequisite);
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Node>> Trees =
        new Dictionary<string, IReadOnlyDictionary<string, Node>>
        {
            ["weapon_sword"] = Build("sword", [1,2,3,7,8,4,5,6,9,12,10,16,11,13,14,20,18,19,15,17]),
            ["weapon_staff"] = Build("staff", [1,2,4,3,13,6,5,7,8,12,14,11,9,15,16,10,18,17,19,20]),
            ["weapon_bow"] = Build("bow", [1,2,3,7,10,5,4,6,8,11,9,14,12,13,16,20,17,19,15,18]),
            ["element_fire"] = Build("fire", [1,2,5,3,4,7,6,8,9,12,14,10,11,13,16,19,18,17,15,20]),
            ["element_ice"] = Build("ice", [1,2,4,3,5,7,6,8,9,12,16,10,11,13,14,17,18,19,15,20]),
            ["element_lightning"] = Build("lightning", [1,2,3,4,5,7,6,8,10,13,14,9,11,12,15,17,18,19,16,20]),
            ["special_critical"] = Build("critical", [1,2,3,4,9,5,6,7,8,13,15,10,11,14,16,17,12,19,18,20]),
            ["special_cooldown"] = Build("cooldown", [1,2,3,8,9,11,4,5,7,13,12,6,10,14,16,17,19,18,15,20]),
            ["special_mana"] = Build("mana", Enumerable.Range(1,20).ToArray()),
        };

    private static IReadOnlyDictionary<string, Node> Build(string prefix, int[] order) =>
        order.Select((number, index) =>
        {
            int stage = index < 3 ? 1 : index < 9 ? 2 : index < 15 ? 3 : 4;
            int start = stage switch { 1 => 0, 2 => 3, 3 => 9, _ => 15 };
            string? prerequisite = stage > 1 && index - start < 3 ? $"{prefix}_{order[index - 3]:D2}" : null;
            return new Node($"{prefix}_{number:D2}", stage, stage == 1 ? 3 : stage == 4 ? 1 : 2, prerequisite);
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
        return node.Stage == 1 || nodes.Values.Where(n => n.Stage == node.Stage - 1).Sum(n => Math.Min(n.MaxLevel, Level(progress, n.Id))) >= 6;
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
