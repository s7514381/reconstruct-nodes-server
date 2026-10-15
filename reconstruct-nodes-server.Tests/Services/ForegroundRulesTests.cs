using System.Text.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests.Services;

[TestClass]
public class ForegroundRulesTests
{
    [TestMethod]
    public void SkillLoadout_EnforcesPointsChainLimitsDuplicatesAndTriggerRules()
    {
        SkillLoadoutRules.Validate([new() { Kind = 0, AuxiliaryNodes = [
            new() { Type = "auxiliary", LootId = "critical_trigger_gem" }, new() { Type = "active", Kind = 1 }] }]);
        Assert.Throws<BusinessArgumentException>(() => SkillLoadoutRules.Validate([new() { Kind = 0 }, new() { Kind = 0 }]));
        Assert.Throws<BusinessOperationException>(() => SkillLoadoutRules.Validate([new() { Kind = 1, AuxiliaryNodes = [
            new() { Type = "auxiliary", LootId = "critical_trigger_gem" }] }]));
        Assert.Throws<BusinessOperationException>(() => SkillLoadoutRules.Validate([new() { Kind = 0, AuxiliaryNodes = [
            new() { Type = "active", Kind = 1 }] }]));
        Assert.Throws<BusinessOperationException>(() => SkillLoadoutRules.Validate([new() { Kind = 0, AuxiliaryNodes = [
            new() { Type = "auxiliary", LootId = "aura_support" }] }]));
        SkillLoadoutRules.Validate([new() { Kind = 6, AuxiliaryNodes = [new() { Type = "auxiliary", LootId = "aura_support" }] }]);
    }

    [TestMethod]
    public void Battlefield_ValidatesEntranceToCastleAndOccupiedCells()
    {
        GameProgressBattlefieldModel battlefield = new()
        {
            Version = 1, Columns = 7, Rows = 10, Castle = [5,8],
            Roads = Enumerable.Range(0,5).Select(x => new GameProgressRoadModel { X = x, Y = 8, Rotation = 0 }).ToList(),
        };
        BattlefieldRules.Validate(battlefield, []);
        Assert.Throws<BusinessArgumentException>(() => BattlefieldRules.Validate(battlefield,
            [new() { CharacterId = Guid.NewGuid(), Cell = [1,8] }]));
        battlefield.Roads.RemoveAt(1);
        Assert.Throws<BusinessArgumentException>(() => BattlefieldRules.Validate(battlefield, []));
        battlefield.Roads.Clear();
        Assert.Throws<BusinessArgumentException>(() => BattlefieldRules.Validate(battlefield, []));
    }

    [TestMethod]
    public void WeaponWire_RequiresIdentityButNotDomainCheckpoint()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WeaponEquipRequest>("{}"));
        string request = JsonSerializer.Serialize(new WeaponEquipRequest
        {
            RequestId = Guid.NewGuid(), CharacterId = Guid.NewGuid(), WeaponEntryId = null,
        });
        using var json = JsonDocument.Parse(request);
        Assert.IsFalse(json.RootElement.TryGetProperty("expected_revision", out _));
        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("weapon_entry_id").ValueKind);
        Assert.IsNotNull(JsonSerializer.Deserialize<WeaponEquipRequest>(request));
    }
    [TestMethod]
    public void Talent_StageUsesPreviousStageNotCumulative_AndRequiresMaxedPrerequisite()
    {
        TalentModel model = new() { TalentPoints = 40 };
        TalentRules.Learn(model, "weapon_sword", "sword_01");
        Assert.Throws<BusinessOperationException>(() => TalentRules.Learn(model, "weapon_sword", "sword_07"));
        TalentRules.Learn(model, "weapon_sword", "sword_01");
        TalentRules.Learn(model, "weapon_sword", "sword_01");
        for (int i = 0; i < 3; i++) { TalentRules.Learn(model, "weapon_sword", "sword_02"); }
        TalentRules.Learn(model, "weapon_sword", "sword_07");
        Assert.Throws<BusinessOperationException>(() => TalentRules.Learn(model, "weapon_sword", "sword_11"));
        Assert.Throws<BusinessOperationException>(() => TalentRules.RefundNode(model, "weapon_sword", "sword_01"));
        TalentRules.RefundNode(model, "weapon_sword", "sword_07");
        TalentRules.RefundNode(model, "weapon_sword", "sword_01");
        Assert.AreEqual(37, model.TalentPoints);
        Assert.AreEqual(3, model.TotalSpentPoints);
    }

    [TestMethod]
    public void Talent_ResetRefundsActualLevelsAndLegacyCost_NotHistoricalSpent()
    {
        TalentModel model = new()
        {
            TalentPoints = 5, TotalSpentPoints = 100,
            AcquiredTalents = new() { ["special_mana"] = new() { ["mana_01"] = JsonSerializer.SerializeToElement(3) } },
            LegacyCategoryTalents = new() { ["weapon_sword"] = new() { ["advanced"] = JsonSerializer.SerializeToElement(true) } },
        };
        TalentRules.Reset(model, null);
        Assert.AreEqual(10, model.TalentPoints);
        Assert.AreEqual(95, model.TotalSpentPoints);
        Assert.HasCount(0, model.AcquiredTalents);
        Assert.HasCount(0, model.LegacyCategoryTalents);
    }

    [TestMethod]
    public void Refinement_AllOperationsPreserveIdentityAndEnforceQuality()
    {
        WeaponInstanceModel weapon = new() { Id = "test", WeaponId = "sword_50", WeaponFamilyId = "sword", Rarity = "common" };
        weapon = RefinementRules.Refine(weapon, "awakening_crystal");
        Assert.AreEqual("fine", weapon.Rarity);
        Assert.HasCount(1, weapon.Affixes);
        weapon = RefinementRules.Refine(weapon, "radiance_crystal");
        Assert.HasCount(2, weapon.Affixes);
        Assert.Throws<BusinessOperationException>(() => RefinementRules.Refine(weapon, "radiance_crystal"));
        weapon = RefinementRules.Refine(weapon, "ascension_crystal");
        Assert.AreEqual("rare", weapon.Rarity);
        Assert.HasCount(3, weapon.Affixes);
        weapon = RefinementRules.Refine(weapon, "empowerment_crystal");
        Assert.HasCount(4, weapon.Affixes);
        weapon = RefinementRules.Refine(weapon, "stripping_crystal");
        Assert.HasCount(3, weapon.Affixes);
        foreach (string crystal in new[] { "chaos_crystal", "tuning_crystal", "perfection_crystal", "polarity_crystal" })
        {
            weapon = RefinementRules.Refine(weapon, crystal);
            Assert.AreEqual("test", weapon.Id);
            Assert.AreEqual("sword_50", weapon.WeaponId);
            Assert.IsTrue(weapon.Affixes.Count is >= 3 and <= 6);
        }
        weapon = RefinementRules.Refine(weapon, "nullification_crystal");
        Assert.AreEqual("common", weapon.Rarity);
        Assert.HasCount(0, weapon.Affixes);
    }

    [TestMethod]
    public void Refinement_RejectsUniqueMixedAndInvalidSavedValues()
    {
        WeaponInstanceModel weapon = new() { Id = "test", WeaponId = "sword", Rarity = "unique" };
        Assert.Throws<BusinessOperationException>(() => RefinementRules.Refine(weapon, "nullification_crystal"));
        weapon.Rarity = "rare";
        weapon.Affixes = [JsonSerializer.SerializeToElement(new { id = "global_damage_t6", values = new[] { 99999 } })];
        Assert.Throws<BusinessArgumentException>(() => RefinementRules.Refine(weapon, "chaos_crystal"));
        weapon.Affixes = [JsonSerializer.SerializeToElement("might"),
            JsonSerializer.SerializeToElement(new { id = "global_damage_t6", values = new[] { 5 } })];
        Assert.Throws<BusinessOperationException>(() => RefinementRules.Refine(weapon, "chaos_crystal"));
    }

    [TestMethod]
    public void Refinement_LegacyRerollConvertsToTiered_AndFineRerollKeepsBounds()
    {
        WeaponInstanceModel weapon = new()
        {
            Id = "test", WeaponId = "staff", Rarity = "fine",
            Affixes = [JsonSerializer.SerializeToElement("might")],
        };
        Assert.Throws<BusinessOperationException>(() => RefinementRules.Refine(weapon, "tuning_crystal"));
        weapon = RefinementRules.Refine(weapon, "reshaping_crystal");
        Assert.IsTrue(weapon.Affixes.Count is >= 1 and <= 2);
        foreach (JsonElement affix in weapon.Affixes)
        {
            Assert.IsTrue(affix.GetProperty("id").GetString()!.EndsWith("_t6", StringComparison.Ordinal));
        }
    }
}
