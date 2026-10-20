using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>十字軍（kind 4）加入商店與角色驗證後的離線契約；不連資料庫。</summary>
[TestClass]
public class CrusaderCatalogTests
{
    private static readonly List<PlayerParams> NoUnlocks = new();

    [TestMethod]
    public void CatalogOffersCrusaderAfterCoreKindWithoutRenumberingExistingKinds()
    {
        var offers = ShopCommand.Catalog.Characters;
        CollectionAssert.AreEqual(new[] { CharacterKind.Warrior, CharacterKind.Archer, CharacterKind.Mage, CharacterKind.Crusader, CharacterKind.ShadowMage, CharacterKind.IceMage, CharacterKind.FireMage, CharacterKind.ShadowAssassin, CharacterKind.FlameSwordsman, CharacterKind.WindHunter, CharacterKind.Ranger, CharacterKind.DesertRanger }, offers.Select(o => o.Kind).ToArray());
        Assert.AreEqual(offers.Count, offers.Select(o => o.Kind).Distinct().Count());
        Assert.AreEqual(offers.Count, offers.Select(o => o.LootId).Distinct().Count());
        Assert.IsFalse(offers.Any(o => o.Kind == CharacterService.CoreKind), "core is never a shop reward");
        var crusader = offers.Single(o => o.Kind == CharacterKind.Crusader);
        Assert.AreEqual("crusader", crusader.LootId);
        Assert.AreEqual("sword", crusader.WeaponId);
    }

    [TestMethod]
    public void CrusaderDefaultActiveSkillIsSlashLikeWarrior()
    {
        var slashTree = new List<GameProgressSkillLoadoutModel> { new() { Kind = 5 } };
        CharacterService.ValidateSkillAccess(CharacterKind.Crusader, slashTree, NoUnlocks);
        CharacterService.ValidateSkillAccess(CharacterKind.Warrior, slashTree, NoUnlocks);
    }

    [TestMethod]
    public void CrusaderCannotUseAnotherClassDefaultSkillWithoutUnlock()
    {
        var lightning = new List<GameProgressSkillLoadoutModel> { new() { Kind = 3 } };
        Assert.ThrowsExactly<BusinessArgumentException>(() => CharacterService.ValidateSkillAccess(CharacterKind.Crusader, lightning, NoUnlocks));
    }

    [TestMethod]
    public void MageClassCharactersOfferStaffAndDefaultToIceSkill()
    {
        foreach (var (kind, lootId) in new[] { (CharacterKind.ShadowMage, "shadow_mage"), (CharacterKind.IceMage, "ice_mage") })
        {
            var offer = ShopCommand.Catalog.Characters.Single(o => o.Kind == kind);
            Assert.AreEqual(lootId, offer.LootId);
            Assert.AreEqual("staff", offer.WeaponId);
            var iceTree = new List<GameProgressSkillLoadoutModel> { new() { Kind = 2 } };
            CharacterService.ValidateSkillAccess(kind, iceTree, NoUnlocks);
            var slash = new List<GameProgressSkillLoadoutModel> { new() { Kind = 5 } };
            Assert.ThrowsExactly<BusinessArgumentException>(() => CharacterService.ValidateSkillAccess(kind, slash, NoUnlocks));
        }
    }

    [TestMethod]
    public void FireMageOffersStaffAndDefaultsToFireballSkill()
    {
        var offer = ShopCommand.Catalog.Characters.Single(o => o.Kind == CharacterKind.FireMage);
        Assert.AreEqual("fire_mage", offer.LootId);
        Assert.AreEqual("staff", offer.WeaponId);
        var fireball = new List<GameProgressSkillLoadoutModel> { new() { Kind = 1 } };
        CharacterService.ValidateSkillAccess(CharacterKind.FireMage, fireball, NoUnlocks);
        var slash = new List<GameProgressSkillLoadoutModel> { new() { Kind = 5 } };
        Assert.ThrowsExactly<BusinessArgumentException>(() => CharacterService.ValidateSkillAccess(CharacterKind.FireMage, slash, NoUnlocks));
    }

    [TestMethod]
    public void ShadowAssassinOffersSwordAndDefaultsToSlashSkill()
    {
        var offer = ShopCommand.Catalog.Characters.Single(o => o.Kind == CharacterKind.ShadowAssassin);
        Assert.AreEqual("shadow_assassin", offer.LootId);
        Assert.AreEqual("sword", offer.WeaponId);
        var slash = new List<GameProgressSkillLoadoutModel> { new() { Kind = 5 } };
        CharacterService.ValidateSkillAccess(CharacterKind.ShadowAssassin, slash, NoUnlocks);
        var fireball = new List<GameProgressSkillLoadoutModel> { new() { Kind = 1 } };
        Assert.ThrowsExactly<BusinessArgumentException>(() => CharacterService.ValidateSkillAccess(CharacterKind.ShadowAssassin, fireball, NoUnlocks));
    }

    [TestMethod]
    public void WindHunterOffersBowAndDefaultsToBasicSkill()
    {
        var offer = ShopCommand.Catalog.Characters.Single(o => o.Kind == CharacterKind.WindHunter);
        Assert.AreEqual("wind_hunter", offer.LootId);
        Assert.AreEqual("bow", offer.WeaponId);
        var basic = new List<GameProgressSkillLoadoutModel> { new() { Kind = 0 } };
        CharacterService.ValidateSkillAccess(CharacterKind.WindHunter, basic, NoUnlocks);
        var slash = new List<GameProgressSkillLoadoutModel> { new() { Kind = 5 } };
        Assert.ThrowsExactly<BusinessArgumentException>(() => CharacterService.ValidateSkillAccess(CharacterKind.WindHunter, slash, NoUnlocks));
    }

    [TestMethod]
    public void RangerOffersBowAndDefaultsToBasicSkill()
    {
        var offer = ShopCommand.Catalog.Characters.Single(o => o.Kind == CharacterKind.Ranger);
        Assert.AreEqual("ranger", offer.LootId);
        Assert.AreEqual("bow", offer.WeaponId);
        var basic = new List<GameProgressSkillLoadoutModel> { new() { Kind = 0 } };
        CharacterService.ValidateSkillAccess(CharacterKind.Ranger, basic, NoUnlocks);
        var slash = new List<GameProgressSkillLoadoutModel> { new() { Kind = 5 } };
        Assert.ThrowsExactly<BusinessArgumentException>(() => CharacterService.ValidateSkillAccess(CharacterKind.Ranger, slash, NoUnlocks));
    }

    [TestMethod]
    public void DesertRangerOffersBowAndDefaultsToBasicSkill()
    {
        var offer = ShopCommand.Catalog.Characters.Single(o => o.Kind == CharacterKind.DesertRanger);
        Assert.AreEqual("desert_ranger", offer.LootId);
        Assert.AreEqual("bow", offer.WeaponId);
        var basic = new List<GameProgressSkillLoadoutModel> { new() { Kind = 0 } };
        CharacterService.ValidateSkillAccess(CharacterKind.DesertRanger, basic, NoUnlocks);
        var slash = new List<GameProgressSkillLoadoutModel> { new() { Kind = 5 } };
        Assert.ThrowsExactly<BusinessArgumentException>(() => CharacterService.ValidateSkillAccess(CharacterKind.DesertRanger, slash, NoUnlocks));
    }

    [TestMethod]
    public void FlameSwordsmanOffersSwordAndDefaultsToSlashSkill()
    {
        var offer = ShopCommand.Catalog.Characters.Single(o => o.Kind == CharacterKind.FlameSwordsman);
        Assert.AreEqual("flame_swordsman", offer.LootId);
        Assert.AreEqual("sword", offer.WeaponId);
        var slash = new List<GameProgressSkillLoadoutModel> { new() { Kind = 5 } };
        CharacterService.ValidateSkillAccess(CharacterKind.FlameSwordsman, slash, NoUnlocks);
        var fireball = new List<GameProgressSkillLoadoutModel> { new() { Kind = 1 } };
        Assert.ThrowsExactly<BusinessArgumentException>(() => CharacterService.ValidateSkillAccess(CharacterKind.FlameSwordsman, fireball, NoUnlocks));
    }

    [TestMethod]
    public void UnknownCharacterKindStillRejected()
    {
        var tree = new List<GameProgressSkillLoadoutModel> { new() { Kind = 0 } };
        Assert.ThrowsExactly<BusinessArgumentException>(() => CharacterService.ValidateSkillAccess((CharacterKind)99, tree, NoUnlocks));
    }
}
