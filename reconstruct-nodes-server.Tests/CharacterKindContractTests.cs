using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>
/// 角色種類強型別契約：數值與客戶端 scripts/character_kind.gd 同步（客戶端有對應測試），
/// 新增角色時，這裡任何一項沒跟上都會失敗，而不是在部署／商店才出錯。
/// </summary>
[TestClass]
public class CharacterKindContractTests
{
    [TestMethod]
    public void NumericValuesAreStableAndMatchTheClient()
    {
        var expected = new Dictionary<string, int>
        {
            ["Warrior"] = 0, ["Archer"] = 1, ["Mage"] = 2, ["Core"] = 3, ["Crusader"] = 4,
        };
        var actual = CharacterKinds.All.ToDictionary(kind => kind.ToString(), kind => (int)kind);
        CollectionAssert.AreEquivalent(expected, actual);
    }

    [TestMethod]
    public void WireFormatStaysANumberNotAName()
    {
        var model = new CharacterModel { Kind = CharacterKind.Crusader };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(model));
        Assert.AreEqual(JsonValueKind.Number, json.RootElement.GetProperty("kind").ValueKind);
        Assert.AreEqual(4, json.RootElement.GetProperty("kind").GetInt32());
        Assert.AreEqual(CharacterKind.Mage, JsonSerializer.Deserialize<CharacterModel>("{\"kind\":2}")!.Kind);
        Assert.AreEqual("4", CharacterKinds.Wire(CharacterKind.Crusader));
    }

    [TestMethod]
    public void UndefinedNumbersDeserializeButAreRejectedByTheValidator()
    {
        var model = JsonSerializer.Deserialize<CharacterModel>("{\"kind\":99}")!;
        Assert.IsFalse(CharacterKinds.IsDefined(model.Kind));
        Assert.IsFalse(CharacterKinds.IsDefined((CharacterKind)(-1)));
        Assert.IsTrue(CharacterKinds.All.All(CharacterKinds.IsDefined));
    }

    [TestMethod]
    public void DeployableKindsExcludeOnlyCoreAndDriveTheSqlFilters()
    {
        CollectionAssert.AreEqual(
            new[] { CharacterKind.Warrior, CharacterKind.Archer, CharacterKind.Mage, CharacterKind.Crusader },
            CharacterKinds.Deployable.ToArray());
        Assert.AreEqual("0, 1, 2, 4", CharacterKinds.DeployableSqlList);
        StringAssert.Contains(GameProgressCommand.DeployableCharactersRead.Sql, "Kind IN (0, 1, 2, 4)");
    }

    [TestMethod]
    public void EveryDeployableKindIsInTheShopAndHasADefaultSkill()
    {
        CollectionAssert.AreEquivalent(
            CharacterKinds.Deployable.ToArray(),
            ShopCommand.Catalog.Characters.Select(offer => offer.Kind).ToArray());
        var anySkill = new List<GameProgressSkillLoadoutModel> { new() { Kind = 0 } };
        foreach (var kind in CharacterKinds.All)
        {
            CharacterService.ValidateSkillAccess(kind, anySkill, new List<PlayerParams>());
        }
    }
}
