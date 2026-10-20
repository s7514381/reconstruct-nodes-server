using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>
/// 角色性格／命格／等級契約：數值與客戶端 scripts/character_traits.gd、character_level_rules.gd 同步，
/// 線上格式維持整數；任一邊重排或漏項會在這裡失敗，而不是讓存檔角色顯示成錯的標籤。
/// </summary>
[TestClass]
public class CharacterTraitContractTests
{
    [TestMethod]
    public void PersonalityValuesAreStableAndMatchTheClient()
    {
        string[] expected =
        [
            "Hardy", "Lonely", "Adamant", "Naughty", "Brave", "Bold", "Docile", "Impish", "Lax", "Relaxed",
            "Modest", "Mild", "Bashful", "Rash", "Quiet", "Calm", "Gentle", "Careful", "Quirky", "Sassy",
            "Timid", "Hasty", "Jolly", "Naive", "Serious",
        ];
        CollectionAssert.AreEqual(expected, CharacterTraits.Personalities.Select(value => value.ToString()).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(0, 25).ToArray(), CharacterTraits.Personalities.Select(value => (int)value).ToArray());
    }

    [TestMethod]
    public void FateValuesAreStableAndMatchTheClient()
    {
        string[] expected =
            ["Ziwei", "Qisha", "Pojun", "Tianji", "Taiyang", "Taiyin", "Tianliang", "Tanlang", "Lianzhen", "Tianfu"];
        CollectionAssert.AreEqual(expected, CharacterTraits.Fates.Select(value => value.ToString()).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(0, 10).ToArray(), CharacterTraits.Fates.Select(value => (int)value).ToArray());
    }

    [TestMethod]
    public void WireFormatIsNumbersAndNewCharactersStartAtLevelOne()
    {
        var model = new CharacterModel { Kind = CharacterKind.Mage, Personality = CharacterPersonality.Jolly, Fate = CharacterFate.Tianfu };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(model));
        Assert.AreEqual(22, json.RootElement.GetProperty("personality").GetInt32());
        Assert.AreEqual(9, json.RootElement.GetProperty("fate").GetInt32());
        Assert.AreEqual(1, json.RootElement.GetProperty("level").GetInt32());
        var parsed = JsonSerializer.Deserialize<CharacterModel>("{\"personality\":3,\"fate\":4,\"level\":7}")!;
        Assert.AreEqual(CharacterPersonality.Naughty, parsed.Personality);
        Assert.AreEqual(CharacterFate.Taiyang, parsed.Fate);
        Assert.AreEqual(7, parsed.Level);
        Assert.AreEqual(CharacterLevelRules.InitialLevel, new Character().Level);
    }

    [TestMethod]
    public void UndefinedNumbersDeserializeButAreRejectedByTheValidators()
    {
        var parsed = JsonSerializer.Deserialize<CharacterModel>("{\"personality\":99,\"fate\":-1}")!;
        Assert.IsFalse(CharacterTraits.IsDefined(parsed.Personality));
        Assert.IsFalse(CharacterTraits.IsDefined(parsed.Fate));
        Assert.IsTrue(CharacterTraits.Personalities.All(CharacterTraits.IsDefined));
        Assert.IsTrue(CharacterTraits.Fates.All(CharacterTraits.IsDefined));
        Assert.IsFalse(CharacterLevelRules.IsValid(0));
        Assert.IsFalse(CharacterLevelRules.IsValid(CharacterLevelRules.MaxLevel + 1));
        Assert.IsTrue(CharacterLevelRules.IsValid(CharacterLevelRules.MaxLevel));
    }

    [TestMethod]
    public void ShopRollsReachEveryTrait()
    {
        var personalities = Enumerable.Range(0, 2000).Select(_ => ShopCommand.RollPersonality()).Distinct().Count();
        var fates = Enumerable.Range(0, 2000).Select(_ => ShopCommand.RollFate()).Distinct().Count();
        Assert.AreEqual(CharacterTraits.Personalities.Count, personalities, "all 25 personalities are reachable");
        Assert.AreEqual(CharacterTraits.Fates.Count, fates, "all 10 fates are reachable");
    }
}
