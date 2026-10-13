using ReconstructNodesServer.Library.Models;
using System.Globalization;
using System.Text.Json;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 武器快照的前端 JSON 形狀與內部表示互轉；背包武器與裝備武器共用。
    /// </summary>
    internal static class WeaponSnapshotCodec
    {
        public static List<WeaponAffixSnapshot> ReadAffixes(IEnumerable<JsonElement> raw)
        {
            return raw.Select(ReadAffix).ToList();
        }

        public static List<JsonElement> WriteAffixes(IEnumerable<WeaponAffixSnapshot> affixes)
        {
            return affixes.Select(WriteAffix).ToList();
        }

        public static WeaponAffixSnapshot ReadAffix(JsonElement raw)
        {
            if (raw.ValueKind == JsonValueKind.String)
            {
                return new WeaponAffixSnapshot(raw.GetString()!, false, []);
            }

            if (raw.ValueKind == JsonValueKind.Object
                && raw.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String
                && raw.TryGetProperty("values", out JsonElement values))
            {
                return new WeaponAffixSnapshot(id.GetString()!, true, ReadValues(values));
            }

            throw new BusinessFormatException(ApiMessageKeys.WeaponAffixInvalid);
        }

        public static JsonElement WriteAffix(WeaponAffixSnapshot affix)
        {
            if (!affix.Rolled)
            {
                return JsonSerializer.SerializeToElement(affix.AffixKey);
            }
            return JsonSerializer.SerializeToElement(new { id = affix.AffixKey, values = affix.Values });
        }

        /// <summary>
        /// 前端 unique 為空字典代表無特殊屬性
        /// </summary>
        public static WeaponUniqueSnapshot? ReadUnique(IReadOnlyDictionary<string, JsonElement>? raw)
        {
            if (raw == null || raw.Count == 0) { return null; }

            string? uniqueKey = raw.TryGetValue("unique_id", out JsonElement id) ? id.GetString() : null;
            if (string.IsNullOrEmpty(uniqueKey) || !raw.TryGetValue("values", out JsonElement values))
            {
                throw new BusinessFormatException(ApiMessageKeys.WeaponUniqueInvalid);
            }
            return new WeaponUniqueSnapshot(uniqueKey, ReadValues(values));
        }

        public static Dictionary<string, JsonElement> WriteUnique(WeaponUniqueSnapshot? unique)
        {
            if (unique == null) { return new Dictionary<string, JsonElement>(); }

            return new Dictionary<string, JsonElement>
            {
                ["unique_id"] = JsonSerializer.SerializeToElement(unique.UniqueKey),
                ["values"] = JsonSerializer.SerializeToElement(unique.Values),
            };
        }

        /// <summary>
        /// 數值以字串儲存於子表，避免為每個屬性另建欄位
        /// </summary>
        public static string FormatValue(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static int ParseValue(string text) => int.Parse(text, CultureInfo.InvariantCulture);

        private static IReadOnlyList<int> ReadValues(JsonElement array)
        {
            return array.EnumerateArray().Select(v => v.GetInt32()).ToList();
        }
    }
}
