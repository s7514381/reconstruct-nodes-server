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
        CollectionAssert.AreEqual(new[] { CharacterKind.Warrior, CharacterKind.Archer, CharacterKind.Mage, CharacterKind.Crusader }, offers.Select(o => o.Kind).ToArray());
        Assert.AreEqual(offers.Count, offers.Select(o => o.Kind).Distinct().Count());
        Assert.AreEqual(offers.Count, offers.Select(o => o.LootId).Distinct().Count());
        Assert.AreEqual(offers.Count, offers.Select(o => o.ShardId).Distinct().Count());
        Assert.IsFalse(offers.Any(o => o.Kind == CharacterService.CoreKind), "core is never a shop reward");
        var crusader = offers.Single(o => o.Kind == CharacterKind.Crusader);
        Assert.AreEqual("crusader", crusader.LootId);
        Assert.AreEqual("crusader_shard", crusader.ShardId);
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
    public void UnknownCharacterKindStillRejected()
    {
        var tree = new List<GameProgressSkillLoadoutModel> { new() { Kind = 0 } };
        Assert.ThrowsExactly<BusinessArgumentException>(() => CharacterService.ValidateSkillAccess((CharacterKind)5, tree, NoUnlocks));
    }
}
