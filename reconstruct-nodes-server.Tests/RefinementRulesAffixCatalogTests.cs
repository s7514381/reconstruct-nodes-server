using System.Reflection;
using System.Text.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests;

/// <summary>
/// 伺服器精煉規則必須與客戶端 WeaponAffixCatalog 同步。
/// 基準檔 Fixtures/weapon_affix_catalog.golden.json 由客戶端 test_weapon_affix_server_golden.gd 驗證，
/// 客戶端詞綴目錄變動時重新產生並複製到這裡，本測試即指出伺服器需要同步的家族／值域。
/// </summary>
[TestClass]
public class RefinementRulesAffixCatalogTests
{
    private sealed record Entry(string Id, string Family, int Tier, string Position, string[] Weapons,
        int RequiredLevel, int Min, int Max, int SecondMin, int SecondMax);

    private static readonly string[] AllTypes = ["sword", "staff", "bow"];

    private static List<Entry> LoadGolden()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "weapon_affix_catalog.golden.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray().Select(e => new Entry(
            e.GetProperty("id").GetString()!, e.GetProperty("family").GetString()!, e.GetProperty("tier").GetInt32(),
            e.GetProperty("position").GetString()!, e.GetProperty("weapons").EnumerateArray().Select(w => w.GetString()!).ToArray(),
            e.GetProperty("required_level").GetInt32(), e.GetProperty("min").GetInt32(), e.GetProperty("max").GetInt32(),
            e.GetProperty("second_min").GetInt32(), e.GetProperty("second_max").GetInt32())).ToList();
    }

    private static WeaponInstanceModel Rare(string weaponId, string type, params (string Id, int[] Values)[] affixes) => new()
    {
        Id = "w1", WeaponId = weaponId, WeaponFamilyId = type, Rarity = "rare",
        Affixes = affixes.Select(a => JsonSerializer.SerializeToElement(new { id = a.Id, values = a.Values })).ToList(),
    };

    private static string WeaponIdFor(string type, int level) => level == 0 ? type : $"{type}_{level}";

    private static int[] ValuesFor(Entry entry, bool upper) => entry.SecondMax > 0
        ? [upper ? entry.Max : entry.Min, upper ? entry.SecondMax : entry.SecondMin]
        : [upper ? entry.Max : entry.Min];

    [TestMethod]
    public void ServerFamiliesMatchClientCatalogExactly()
    {
        FieldInfo field = typeof(RefinementRules).GetField("Families", BindingFlags.NonPublic | BindingFlags.Static)!;
        string[] keys = ((System.Collections.IEnumerable)field.GetValue(null)!).Cast<object>()
            .Select(pair => (string)pair.GetType().GetProperty("Key")!.GetValue(pair)!)
            .OrderBy(k => k, StringComparer.Ordinal).ToArray();
        string[] expected = LoadGolden().Select(e => e.Family).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(expected, keys, "Server family list drifted from the client catalog.");
    }

    [TestMethod]
    public void EveryClientAffixTierIsAcceptedAtItsBoundsOnEligibleWeapons()
    {
        foreach (Entry entry in LoadGolden())
        {
            foreach (string type in entry.Weapons)
            {
                string weaponId = WeaponIdFor(type, entry.RequiredLevel);
                foreach (bool upper in new[] { false, true })
                {
                    WeaponInstanceModel original = Rare(weaponId, type, (entry.Id, ValuesFor(entry, upper)));
                    WeaponInstanceModel refined = RefinementRules.Refine(original, "tuning_crystal");
                    Assert.AreEqual(1, refined.Affixes.Count, $"{entry.Id} on {weaponId}");
                    var parsed = refined.Affixes[0];
                    Assert.AreEqual(entry.Id, parsed.GetProperty("id").GetString());
                    int[] rolled = parsed.GetProperty("values").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                    Assert.AreEqual(entry.SecondMax > 0 ? 2 : 1, rolled.Length, entry.Id);
                    Assert.IsTrue(rolled[0] >= entry.Min && rolled[0] <= entry.Max, $"{entry.Id} first value {rolled[0]}");
                    if (rolled.Length == 2) { Assert.IsTrue(rolled[1] >= entry.SecondMin && rolled[1] <= entry.SecondMax, $"{entry.Id} second value"); }
                }
            }
        }
    }

    [TestMethod]
    public void ServerRejectsOutOfBoundsWeaponTypeAndLevelMismatches()
    {
        foreach (Entry entry in LoadGolden())
        {
            string valid = entry.Weapons[0];
            string validId = WeaponIdFor(valid, entry.RequiredLevel);
            int[] tooHigh = ValuesFor(entry, true).Select((v, i) => i == 0 ? v + 1 : v).ToArray();
            Assert.Throws<BusinessArgumentException>(() => RefinementRules.Refine(Rare(validId, valid, (entry.Id, tooHigh)), "tuning_crystal"), $"{entry.Id} above max");
            foreach (string other in AllTypes.Except(entry.Weapons))
            {
                Assert.Throws<BusinessArgumentException>(() => RefinementRules.Refine(Rare(WeaponIdFor(other, 50), other, (entry.Id, ValuesFor(entry, false))), "tuning_crystal"), $"{entry.Id} on {other}");
            }
            if (entry.RequiredLevel > 0)
            {
                string low = WeaponIdFor(valid, entry.RequiredLevel - 5);
                Assert.Throws<BusinessArgumentException>(() => RefinementRules.Refine(Rare(low, valid, (entry.Id, ValuesFor(entry, false))), "tuning_crystal"), $"{entry.Id} below level");
            }
        }
    }

    [TestMethod]
    public void LegacyMultiTierProjectileAffixesConvertToFixedTopTier()
    {
        WeaponInstanceModel high = RefinementRules.Refine(Rare("bow_50", "bow", ("projectiles_t3", [2]), ("projectile_pierce_t6", [1])), "tuning_crystal");
        string[] ids = high.Affixes.Select(a => a.GetProperty("id").GetString()!).ToArray();
        CollectionAssert.AreEqual(new[] { "projectiles_t1", "projectile_pierce_t1" }, ids);
        Assert.AreEqual(2, high.Affixes[1].GetProperty("values")[0].GetInt32());
        WeaponInstanceModel low = RefinementRules.Refine(Rare("bow_5", "bow", ("projectiles_t3", [2]), ("critical_chance_t6", [1])), "tuning_crystal");
        Assert.AreEqual(1, low.Affixes.Count);
        Assert.AreEqual("critical_chance_t6", low.Affixes[0].GetProperty("id").GetString());
    }
}
