using System.Text.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>新手選角（Shop/Draw 的 starter_kind）離線契約；不連資料庫。</summary>
[TestClass]
public class StarterPickContractTests
{
    [TestMethod]
    public void StarterKindsAreTheThreeBasicClassesAndAllOfferedByShop()
    {
        CollectionAssert.AreEqual(new[] { CharacterKind.Warrior, CharacterKind.Archer, CharacterKind.Mage }, ShopCommand.StarterKinds.ToArray());
        foreach (var kind in ShopCommand.StarterKinds)
        {
            Assert.IsTrue(ShopCommand.Catalog.Characters.Any(o => o.Kind == kind));
        }
    }

    [TestMethod]
    public void StarterKindIsOmittedForNormalDrawAndReadFromSnakeCaseField()
    {
        var normal = JsonSerializer.Serialize(new ShopDrawRequest { RequestId = Guid.NewGuid(), Count = 1 });
        Assert.IsFalse(normal.Contains("starter_kind"));
        var parsed = JsonSerializer.Deserialize<ShopDrawRequest>($"{{\"request_id\":\"{Guid.NewGuid()}\",\"count\":1,\"starter_kind\":{(int)CharacterKind.Mage}}}")!;
        Assert.AreEqual(CharacterKind.Mage, parsed.StarterKind);
    }
}
