using System.Security.Cryptography;
using System.Text.Json;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>
/// Server-side rolls matching RefinementService and WeaponAffixCatalog.
/// The client supplies an intent, never a rolled candidate or material balance.
/// </summary>
public static class RefinementRules
{
    private enum Operation { Awaken, Ascend, AddFine, AddRare, Remove, RerollFine, RerollRare, Reset, Tune, Maximize, Polarize }
    private sealed record Family(string Id, bool Prefix, string[] Types, int Min, int Max, int Step,
        int SecondMin = 0, int SecondMax = 0, int SecondStep = 0);
    private sealed record Affix(Family Family, int Tier, int[] Values)
    {
        public string Id => $"{Family.Id}_t{Tier}";
    }
    private static readonly string[] All = ["sword", "staff", "bow"];
    private static readonly string[] Ranged = ["staff", "bow"];
    private static readonly IReadOnlyDictionary<string, Operation> Crystals = new Dictionary<string, Operation>
    {
        ["awakening_crystal"] = Operation.Awaken, ["ascension_crystal"] = Operation.Ascend,
        ["radiance_crystal"] = Operation.AddFine, ["empowerment_crystal"] = Operation.AddRare,
        ["stripping_crystal"] = Operation.Remove, ["reshaping_crystal"] = Operation.RerollFine,
        ["chaos_crystal"] = Operation.RerollRare, ["nullification_crystal"] = Operation.Reset,
        ["tuning_crystal"] = Operation.Tune, ["perfection_crystal"] = Operation.Maximize,
        ["polarity_crystal"] = Operation.Polarize,
    };
    private static readonly IReadOnlyDictionary<string, Family> Families = BuildFamilies();
    private static IReadOnlyDictionary<string, Family> BuildFamilies()
    {
        List<Family> families =
        [
            new("global_damage",true,All,5,10,5), new("range",false,All,4,8,3),
            new("recovery",false,All,4,8,3), new("critical_chance",false,All,1,2,1),
            new("critical_damage",false,All,8,12,5), new("physical_damage",true,All,5,10,5),
            new("elemental_damage",true,All,4,8,4), new("attack_damage",true,All,5,10,5),
            new("spell_damage",true,["staff"],5,10,5), new("projectile_damage",true,Ranged,5,10,5),
            new("area_damage",true,["sword","staff"],5,10,5), new("added_physical",true,All,1,2,2,3,4,3),
            new("elemental_penetration",false,All,1,2,1), new("projectile_speed",false,Ranged,5,10,5),
            new("projectiles",false,["bow"],1,1,0), new("projectile_pierce",false,Ranged,1,1,0),
            new("area",false,["sword","staff"],4,8,3), new("burn_damage",true,All,8,15,7),
            new("burn_duration",false,All,5,10,5), new("cold_duration",false,All,5,10,5),
            new("mana_cost",false,["staff"],2,4,2), new("mana",true,["staff"],5,10,5),
            new("mana_regen",false,All,5,10,5), new("mana_kill",false,All,1,2,1),
            new("freeze_duration",false,All,5,10,5),
        ];
        foreach (string element in new[] { "fire", "ice", "lightning" })
        {
            families.Add(new($"{element}_damage",true,All,6,12,6));
            families.Add(new($"added_{element}",true,All,1,2,2,3,5,3));
            families.Add(new($"spell_added_{element}",true,["staff"],1,2,2,3,5,3));
            families.Add(new($"{element}_penetration",false,All,1,2,1));
        }
        return families.ToDictionary(f => f.Id);
    }

    public static WeaponInstanceModel Refine(WeaponInstanceModel original, string crystalId)
    {
        if (!Crystals.TryGetValue(crystalId, out Operation operation)) { throw new BusinessArgumentException("weapon.error.crystal_invalid"); }
        if (original.Unique.Count != 0 || original.Rarity == "unique") { throw new BusinessOperationException("weapon.error.unique"); }
        (string type, int level) = WeaponBase(original.WeaponId);
        if (original.Rarity is not ("common" or "fine" or "rare")) { throw new BusinessArgumentException("weapon.error.quality"); }
        string? required = operation switch
        {
            Operation.Awaken => "common",
            Operation.Ascend or Operation.AddFine or Operation.RerollFine => "fine",
            Operation.AddRare or Operation.Remove or Operation.RerollRare or Operation.Maximize or Operation.Polarize => "rare",
            _ => null,
        };
        if (required != null && original.Rarity != required ||
            required == null && original.Rarity == "common") { throw new BusinessOperationException("weapon.error.quality"); }
        List<Affix> affixes = [];
        bool legacy = false;
        foreach (JsonElement raw in original.Affixes)
        {
            if (raw.ValueKind == JsonValueKind.String)
            {
                if (raw.GetString() is not ("might" or "slayer" or "longshot" or "eagle_eye" or "haste" or "windwalk" or "critical_chance" or "critical_damage"))
                { throw new BusinessArgumentException("weapon.error.invalid_affix"); }
                legacy = true;
                continue;
            }
            WeaponAffixSnapshot parsed = WeaponSnapshotCodec.ReadAffix(raw);
            int split = parsed.AffixKey.LastIndexOf("_t", StringComparison.Ordinal);
            if (split < 1 || !Families.TryGetValue(parsed.AffixKey[..split], out Family? family) ||
                !int.TryParse(parsed.AffixKey[(split + 2)..], out int tier) || tier is < 1 or > 6 ||
                !family.Types.Contains(type) || (6 - tier) * 10 > level)
            { throw new BusinessArgumentException("weapon.error.affix_eligibility"); }
            int[] values = parsed.Values.ToArray();
            var bounds = Bounds(family, tier);
            if (values.Length != bounds.Length || values.Where((v, i) => v < bounds[i].Min || v > bounds[i].Max).Any())
            { throw new BusinessArgumentException("weapon.error.invalid_affix"); }
            affixes.Add(new(family, tier, values));
        }
        if (legacy && affixes.Count > 0) { throw new BusinessOperationException("weapon.error.mixed"); }
        ValidateQuota(original.Rarity, affixes);
        bool replacing = operation is Operation.Awaken or Operation.Ascend or Operation.RerollFine or Operation.RerollRare or Operation.Reset;
        if (legacy && !replacing) { throw new BusinessOperationException("weapon.error.legacy"); }
        string quality = operation switch { Operation.Awaken => "fine", Operation.Ascend => "rare", Operation.Reset => "common", _ => original.Rarity };
        switch (operation)
        {
            case Operation.Reset:
                affixes.Clear();
                break;
            case Operation.RerollFine:
            case Operation.RerollRare:
                affixes.Clear();
                Fill(affixes, quality, type, level);
                break;
            case Operation.Awaken:
            case Operation.Ascend:
                if (legacy) { affixes.Clear(); Fill(affixes, quality, type, level); }
                else { Add(affixes, quality, type, level); }
                break;
            case Operation.AddFine:
            case Operation.AddRare:
                Add(affixes, quality, type, level);
                break;
            case Operation.Remove:
                RequireAffixes(affixes);
                affixes.RemoveAt(Roll(0, affixes.Count - 1));
                break;
            case Operation.Tune:
                RequireAffixes(affixes);
                for (int i = 0; i < affixes.Count; i++) { affixes[i] = Rolled(affixes[i].Family, affixes[i].Tier); }
                break;
            case Operation.Maximize:
            case Operation.Polarize:
                RequireAffixes(affixes);
                int index = Roll(0, affixes.Count - 1);
                Affix selected = affixes[index];
                bool upper = operation == Operation.Maximize || Roll(0, 1) == 0;
                affixes[index] = selected with { Values = Bounds(selected.Family, selected.Tier).Select(b => upper ? b.Max : b.Min).ToArray() };
                break;
        }
        ValidateQuota(quality, affixes);
        return new WeaponInstanceModel
        {
            Id = original.Id, WeaponId = original.WeaponId, WeaponFamilyId = original.WeaponFamilyId,
            Rarity = quality, Unique = original.Unique,
            Affixes = WeaponSnapshotCodec.WriteAffixes(affixes.Select(a => new WeaponAffixSnapshot(a.Id, true, a.Values))),
        };
    }

    private static (string Type, int Level) WeaponBase(string id)
    {
        foreach (string type in All)
        {
            if (id == type) { return (type, 0); }
            if (id.StartsWith(type + "_", StringComparison.Ordinal) &&
                int.TryParse(id[(type.Length + 1)..], out int level) && level is >= 5 and <= 50 && level % 5 == 0)
            { return (type, level); }
        }
        throw new BusinessArgumentException("weapon.error.not_found");
    }
    private static (int Min, int Max)[] Bounds(Family family, int tier)
    {
        int rank = 6 - tier;
        int min = family.Min + rank * family.Step, max = family.Max + rank * family.Step;
        if (family.Id is "projectiles" or "projectile_pierce") { min += rank / 2; max += (rank + 1) / 2; }
        return family.SecondMax > 0
            ? [(min, max), (family.SecondMin + rank * family.SecondStep, family.SecondMax + rank * family.SecondStep)]
            : [(min, max)];
    }
    private static Affix Rolled(Family family, int tier) =>
        new(family, tier, Bounds(family, tier).Select(b => Roll(b.Min, b.Max)).ToArray());
    private static int Roll(int min, int max) => RandomNumberGenerator.GetInt32(min, checked(max + 1));
    private static void RequireAffixes(List<Affix> affixes)
    {
        if (affixes.Count == 0) { throw new BusinessOperationException("weapon.error.no_affixes"); }
    }
    private static void ValidateQuota(string quality, List<Affix> affixes)
    {
        int limit = quality == "fine" ? 1 : 3;
        int max = quality switch { "common" => 0, "fine" => 2, _ => 6 };
        if (affixes.Count > max || affixes.Count(a => a.Family.Prefix) > limit ||
            affixes.Count(a => !a.Family.Prefix) > limit || affixes.Select(a => a.Family.Id).Distinct().Count() != affixes.Count)
        { throw new BusinessOperationException("weapon.error.affix_limit"); }
    }
    private static void Fill(List<Affix> affixes, string quality, string type, int level)
    {
        int count = quality == "fine" ? Roll(1, 2) : Roll(3, 6);
        for (int i = 0; i < count; i++) { Add(affixes, quality, type, level); }
    }
    private static void Add(List<Affix> affixes, string quality, string type, int level)
    {
        int limit = quality == "fine" ? 1 : 3;
        var eligible = Families.Values.Where(f => f.Types.Contains(type) &&
            !affixes.Any(a => a.Family.Id == f.Id) &&
            affixes.Count(a => a.Family.Prefix == f.Prefix) < limit).ToArray();
        if (eligible.Length == 0) { throw new BusinessOperationException("weapon.error.quota"); }
        Family family = eligible[Roll(0, eligible.Length - 1)];
        int[] tiers = Enumerable.Range(1, 6).Where(tier => (6 - tier) * 10 <= level).ToArray();
        affixes.Add(Rolled(family, tiers[Roll(0, tiers.Length - 1)]));
    }
}
