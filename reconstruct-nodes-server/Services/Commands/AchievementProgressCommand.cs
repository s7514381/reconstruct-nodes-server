using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 成就進度服務：Achievement 每個群組一列（Total 為進度），AchievementDetail 為群組內項目與領取狀態，
    /// AchievementCollected 為已收集的武器與詞綴。
    /// </summary>
    public class AchievementProgressCommand(BaseServiceArgument argument,
        AchievementService achievementService, AchievementDetailService detailService,
        AchievementCollectedService collectedService, RevisionCommand Revisions) : BaseService(argument)
    {
        /// <summary>
        /// 讀取目前玩家的成就進度。進度與收集一律由伺服器資料推導（擊殺、通關波數、武器與詞綴），
        /// 資料庫只保存領取紀錄與既有的收集歷史；用戶端不再上報成就。
        /// </summary>
        public virtual async Task<AchievementProgressModel?> Get()
        {
            List<Achievement> groups = await QueryGroupsAsync(lockRows: false);
            ILookup<Guid?, AchievementDetail> details = (await detailService.QueryChildrenAsync<Achievement>()).ToLookup(d => d.AchievementId);
            ILookup<Guid?, AchievementCollected> collected = (await collectedService.QueryChildrenAsync<Achievement>()).ToLookup(c => c.AchievementId);
            var inputs = new AchievementInputs(
                (await QueryAsync<ParamRow>(InputParamsSql, new { CreateUserId = _playerId })).ToList(),
                (await QueryAsync<string>(InputWeaponsSql, new { CreateUserId = _playerId })).ToList(),
                (await QueryAsync<string>(InputAffixesSql, new { CreateUserId = _playerId })).ToList());

            return Derive(groups, details, collected, await Revisions.Read(nameof(AchievementProgressModel)), inputs);
        }

        public Task<AchievementProgressModel?> GetCommand() => ReadSnapshotCommandAsync(GetRead);

        public SnapshotRead<AchievementProgressModel?> GetRead =>
            new(RowsRead.Sql, async reader => Derive(await RowsRead.Read(reader)));

        /// <summary>一次往返讀出推導成就所需的全部資料列（領取指令也共用這個讀取）。</summary>
        internal static SnapshotRead<AchievementRows> RowsRead => new($"""
            {OwnedSql.Select<Achievement>(orderBy: "Sequence")}
            {OwnedSql.Children<AchievementDetail, Achievement>()}
            {OwnedSql.Children<AchievementCollected, Achievement>()}
            {OwnedSql.Select<PlayerParams>($"[Group] = '{RevisionCommand.RevisionGroup}' AND Name = '{nameof(AchievementProgressModel)}'")}
            {InputParamsSql}
            {InputWeaponsSql}
            {InputAffixesSql}
            """, async reader =>
        {
            var groups = (await reader.ReadAsync<Achievement>()).ToList();
            var details = (await reader.ReadAsync<AchievementDetail>()).ToList();
            var collected = (await reader.ReadAsync<AchievementCollected>()).ToList();
            PlayerParams? revision = await reader.ReadSingleOrDefaultAsync<PlayerParams>();
            var inputs = new AchievementInputs((await reader.ReadAsync<ParamRow>()).ToList(),
                (await reader.ReadAsync<string>()).ToList(), (await reader.ReadAsync<string>()).ToList());
            return new AchievementRows(groups, details, collected, revision, inputs);
        });

        internal sealed record AchievementRows(List<Achievement> Groups, List<AchievementDetail> Details,
            List<AchievementCollected> Collected, PlayerParams? RevisionRow, AchievementInputs Inputs)
        {
            public long Revision => RevisionRow == null ? 0
                : long.Parse(RevisionRow.Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static AchievementProgressModel Derive(AchievementRows rows) =>
            Derive(rows.Groups, rows.Details.ToLookup(d => d.AchievementId),
                rows.Collected.ToLookup(c => c.AchievementId), rows.Revision, rows.Inputs);

        internal sealed class ParamRow
        {
            public string Group { get; set; } = "";
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";
        }

        // 伺服器端推導成就所需的資料；已刪除（Status = -1）的武器仍算收集過。
        private static readonly string InputParamsSql = OwnedSql.Select<PlayerParams>(
            """
            (([Group] = 'game_progress' AND Name = 'lifetime_enemies_defeated')
                OR [Group] = 'game_progress.cleared_waves')
            """, "[Group], Name, Value");
        private static readonly string InputWeaponsSql = OwnedSql.Select<Weapon>(
            columns: "DISTINCT COALESCE(UniqueKey, WeaponKey)", activeOnly: false);
        private const string InputAffixesSql = """
            SELECT DISTINCT a.AffixKey FROM WeaponAffix a INNER JOIN Weapon w ON w.Id = a.WeaponId
            WHERE w.CreateUserId = @CreateUserId AND a.CreateUserId = @CreateUserId;
            """;

        private static readonly (string Name, int[] Targets)[] Catalog =
        [
            ("enemy_kills", [10, 100, 1000, 10000, 100000, 1000000, 10000000]),
            ("waves_intro", [10, 30, 50, 80, 100]),
            ("waves_normal", [10, 30, 50, 80, 100]),
            ("waves_hard", [10, 30, 50, 80, 100]),
            ("weapons_collected", [1, 5, 10, 20, 30, 40]),
            ("affixes_seen", [1, 5, 10, 20, 30]),
        ];

        internal sealed class AchievementInputs
        {
            public long Kills { get; }
            public int[] ClearedWaves { get; } = new int[3];
            public SortedSet<string> Weapons { get; }
            public SortedSet<string> Affixes { get; } = new(StringComparer.Ordinal);

            private AchievementInputs(IEnumerable<string> weapons) => Weapons = new(weapons, StringComparer.Ordinal);

            public AchievementInputs(List<ParamRow> parameters, List<string> weaponKeys, List<string> affixKeys)
                : this(weaponKeys)
            {
                var cultures = System.Globalization.CultureInfo.InvariantCulture;
                foreach (ParamRow row in parameters)
                {
                    if (row.Group == "game_progress" && long.TryParse(row.Value, System.Globalization.NumberStyles.Integer, cultures, out long kills))
                    { Kills = Math.Max(Kills, kills); }
                    else if (int.TryParse(row.Name, System.Globalization.NumberStyles.Integer, cultures, out int difficulty) &&
                        difficulty is >= 0 and < 3 && int.TryParse(row.Value, System.Globalization.NumberStyles.Integer, cultures, out int cleared))
                    { ClearedWaves[difficulty] = Math.Max(ClearedWaves[difficulty], cleared); }
                }
                foreach (string key in affixKeys)
                {
                    int split = key.LastIndexOf("_t", StringComparison.Ordinal);
                    if (split > 0 && int.TryParse(key[(split + 2)..], out int tier) && tier is >= 1 and <= 6)
                    { Affixes.Add(key[..split]); }
                }
            }
        }

        /// <summary>
        /// 以伺服器資料推導完整成就快照；與資料庫既有的進度、收集歷史取最大值／聯集，不會倒退。
        /// </summary>
        internal static AchievementProgressModel Derive(List<Achievement> groups,
            ILookup<Guid?, AchievementDetail> details, ILookup<Guid?, AchievementCollected> collected,
            long revision, AchievementInputs inputs)
        {
            var result = new AchievementProgressModel { Revision = revision };
            foreach ((string name, int[] targets) in Catalog)
            {
                Achievement? stored = groups.FirstOrDefault(g => g.Name == name);
                var claimed = stored == null ? new Dictionary<string, AchievementDetail>()
                    : details[stored.Id].GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.First());
                List<string> names = stored == null ? [] : collected[stored.Id].Select(c => c.Name).ToList();
                double derived = name switch
                {
                    "enemy_kills" => inputs.Kills,
                    "waves_intro" => inputs.ClearedWaves[0],
                    "waves_normal" => inputs.ClearedWaves[1],
                    "waves_hard" => inputs.ClearedWaves[2],
                    _ => 0,
                };
                if (name == "weapons_collected") { names.AddRange(inputs.Weapons.Except(names)); }
                else if (name == "affixes_seen") { names.AddRange(inputs.Affixes.Except(names)); }
                bool collection = name is "weapons_collected" or "affixes_seen";
                result.Groups.Add(new AchievementGroupModel
                {
                    Name = name,
                    Total = (float)Math.Max(stored?.Total ?? 0, collection ? names.Count : derived),
                    Collected = collection ? names : [],
                    Items = targets.Select(target =>
                    {
                        string itemName = $"{name}_{target}";
                        claimed.TryGetValue(itemName, out AchievementDetail? previous);
                        return new AchievementItemModel
                        {
                            Name = itemName, Target = target, IsReward = previous?.IsReward == true,
                            RewardDate = previous?.RewardDate,
                        };
                    }).ToList(),
                });
            }
            return result;
        }

        // 成就只由伺服器推導，不接受整份快照寫入；領取獎勵見 AchievementClaimCommand。
        public Task Save(AchievementProgressModel model) =>
            throw new NotSupportedException("Achievement snapshots are derived by the server.");

        private Task<List<Achievement>> QueryGroupsAsync(bool lockRows) =>
            achievementService.QueryOwnedRowsAsync(orderBy: row => row.Sequence, lockRows: lockRows);
    }
}
