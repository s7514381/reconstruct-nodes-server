using System.Text.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>天賦盤重新設計（8 盤 x 22 節點、關鍵天賦二選一、舊盤退點遷移）的離線契約；不連資料庫。</summary>
[TestClass]
public class TalentRulesTests
{
    private static TalentModel Model(int points = 100) => new() { TalentPoints = points, AcquiredTalents = [], LegacyCategoryTalents = [] };

    private static void LearnTimes(TalentModel model, string tree, string node, int times)
    {
        for (int i = 0; i < times; i++) { TalentRules.Learn(model, tree, node); }
    }

    [TestMethod]
    public void EveryBoardHasTwentyTwoNodesWithStageLevels()
    {
        string[] trees = ["t_strength", "t_dexterity", "t_intelligence", "t_fire", "t_ice", "t_lightning", "t_critical", "t_swift"];
        string[] prefixes = ["str", "dex", "int", "fire", "ice", "ltn", "crit", "swift"];
        for (int t = 0; t < trees.Length; t++)
        {
            for (int number = 1; number <= 22; number++)
            {
                var node = TalentRules.Get(trees[t], $"{prefixes[t]}_{number:D2}");
                int expected = number <= 3 ? 3 : number <= 15 ? 2 : 1;
                Assert.AreEqual(expected, node.MaxLevel, node.Id);
            }
            Assert.Throws<BusinessArgumentException>(() => TalentRules.Get(trees[t], $"{prefixes[t]}_23"));
        }
        Assert.Throws<BusinessArgumentException>(() => TalentRules.Get("weapon_sword", "sword_01"));
    }

    [TestMethod]
    public void UpgradeRequiresMaxedSourceAndStageNeedsPreviousStageSpend()
    {
        var model = Model();
        Assert.Throws<BusinessOperationException>(() => TalentRules.Learn(model, "t_strength", "str_04"));
        LearnTimes(model, "t_strength", "str_01", 3);
        LearnTimes(model, "t_strength", "str_02", 3);
        TalentRules.Learn(model, "t_strength", "str_04");
        Assert.Throws<BusinessOperationException>(() => TalentRules.Learn(model, "t_strength", "str_10"));
    }

    [TestMethod]
    public void KeystonesAreMutuallyExclusiveAndNeedStageFourSpend()
    {
        var model = Model(200);
        Assert.Throws<BusinessOperationException>(() => TalentRules.Learn(model, "t_fire", "fire_21"));
        foreach (int number in new[] { 1, 2, 3 }) { LearnTimes(model, "t_fire", $"fire_{number:D2}", 3); }
        foreach (int number in new[] { 7, 8, 9 }) { LearnTimes(model, "t_fire", $"fire_{number:D2}", 2); }
        foreach (int number in new[] { 13, 14, 15 }) { LearnTimes(model, "t_fire", $"fire_{number:D2}", 2); }
        foreach (int number in new[] { 19, 20 }) { LearnTimes(model, "t_fire", $"fire_{number:D2}", 1); }
        Assert.Throws<BusinessOperationException>(() => TalentRules.Learn(model, "t_fire", "fire_21"));
        foreach (int number in new[] { 16, 17 }) { LearnTimes(model, "t_fire", $"fire_{number:D2}", 1); }
        TalentRules.Learn(model, "t_fire", "fire_21");
        Assert.Throws<BusinessOperationException>(() => TalentRules.Learn(model, "t_fire", "fire_22"));
        TalentRules.RefundNode(model, "t_fire", "fire_21");
        TalentRules.Learn(model, "t_fire", "fire_22");
    }

    [TestMethod]
    public void RetiredBoardsAndPoisonAreRefundedOnce()
    {
        var model = Model(4);
        model.TotalSpentPoints = 6;
        model.AcquiredTalents["weapon_sword"] = new() { ["sword_01"] = JsonSerializer.SerializeToElement(2) };
        model.AcquiredTalents["element_poison"] = new() { ["foundation"] = JsonSerializer.SerializeToElement(1) };
        model.LegacyCategoryTalents["weapon_sword"] = new() { ["foundation"] = JsonSerializer.SerializeToElement(true) };
        int refund = TalentRules.MigrateRemovedTrees(model);
        Assert.AreEqual(4, refund);
        Assert.AreEqual(8, model.TalentPoints);
        Assert.AreEqual(2, model.TotalSpentPoints);
        Assert.IsFalse(model.AcquiredTalents.ContainsKey("weapon_sword"));
        Assert.IsFalse(model.LegacyCategoryTalents.ContainsKey("weapon_sword"));
        Assert.IsFalse(model.AcquiredTalents.ContainsKey("element_poison"));
        Assert.AreEqual(TalentRules.CurrentVersion, model.Version);
        Assert.AreEqual(0, TalentRules.MigrateRemovedTrees(model));
        Assert.AreEqual(8, model.TalentPoints);
    }
}
