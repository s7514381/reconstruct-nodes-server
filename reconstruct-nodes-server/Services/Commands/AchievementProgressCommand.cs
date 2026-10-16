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
    public class AchievementProgressCommand(BaseServiceArgument argument, ItemService itemService,
        AchievementService achievementService, AchievementDetailService detailService,
        AchievementCollectedService collectedService, RevisionCommand Revisions) : BaseService(argument)
    {
        // 與用戶端 AchievementItem.REWARD_ID、REWARD_AMOUNT 保持一致
        private const string RewardItemKey = "diamond";
        private const int RewardAmount = 1;

        private const string DetailTable = nameof(AchievementDetail);

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

        public SnapshotRead<AchievementProgressModel?> GetRead => new($"""
            SELECT * FROM Achievement WHERE CreateUserId = @CreateUserId AND Status != -1 ORDER BY Sequence;
            SELECT c.* FROM AchievementDetail c INNER JOIN Achievement p ON p.Id = c.AchievementId
            WHERE p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId AND p.Status != -1 ORDER BY c.Sequence;
            SELECT c.* FROM AchievementCollected c INNER JOIN Achievement p ON p.Id = c.AchievementId
            WHERE p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId AND p.Status != -1 ORDER BY c.Sequence;
            SELECT Value FROM PlayerParams WHERE CreateUserId = @CreateUserId
                AND [Group] = 'server.revisions' AND Name = 'AchievementProgressModel' AND Status != -1;
            {InputParamsSql}
            {InputWeaponsSql}
            {InputAffixesSql}
            """, async reader =>
        {
            var groups = (await reader.ReadAsync<Achievement>()).ToList();
            var details = (await reader.ReadAsync<AchievementDetail>()).ToLookup(row => row.AchievementId);
            var collected = (await reader.ReadAsync<AchievementCollected>()).ToLookup(row => row.AchievementId);
            string? revision = await reader.ReadSingleOrDefaultAsync<string>();
            var inputs = new AchievementInputs((await reader.ReadAsync<ParamRow>()).ToList(),
                (await reader.ReadAsync<string>()).ToList(), (await reader.ReadAsync<string>()).ToList());
            return Derive(groups, details, collected,
                revision == null ? 0 : long.Parse(revision, System.Globalization.CultureInfo.InvariantCulture), inputs);
        });

        internal sealed class ParamRow
        {
            public string Group { get; set; } = "";
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";
        }

        // 伺服器端推導成就所需的資料；已刪除（Status = -1）的武器仍算收集過。
        private const string InputParamsSql = """
            SELECT [Group], Name, Value FROM PlayerParams WHERE CreateUserId = @CreateUserId AND Status != -1
                AND (([Group] = 'game_progress' AND Name = 'lifetime_enemies_defeated')
                    OR [Group] = 'game_progress.cleared_waves');
            """;
        private const string InputWeaponsSql = """
            SELECT DISTINCT COALESCE(UniqueKey, WeaponKey) FROM Weapon WHERE CreateUserId = @CreateUserId;
            """;
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

        // 成就只由伺服器推導，不接受整份快照寫入。
        public Task Save(AchievementProgressModel model) =>
            throw new NotSupportedException("Achievement snapshots are derived by the server.");

        internal static (List<Achievement> Groups, List<AchievementDetail> Details,
            List<AchievementCollected> Collected) BuildRows(AchievementProgressModel model,
            Dictionary<string, AchievementDetail> claimed)
        {
            List<Achievement> groups = [];
            List<AchievementDetail> details = [];
            List<AchievementCollected> collected = [];
            foreach (AchievementGroupModel group in model.Groups ?? [])
            {
                Achievement row = new() { Name = group.Name, Total = group.Total, Sequence = groups.Count };
                groups.Add(row);
                int sequence = 0;
                foreach (AchievementItemModel item in group.Items ?? [])
                {
                    claimed.TryGetValue(item.Name, out AchievementDetail? previous);
                    details.Add(new AchievementDetail
                    {
                        AchievementId = row.Id, Name = item.Name, Target = item.Target,
                        IsReward = previous != null, RewardDate = previous?.RewardDate, Sequence = sequence++,
                    });
                }
                sequence = 0;
                foreach (string name in group.Collected ?? [])
                {
                    collected.Add(new AchievementCollected
                    {
                        AchievementId = row.Id, Name = name, Sequence = sequence++,
                    });
                }
            }

            return (groups, details, collected);
        }

        private async Task PersistRows(List<Achievement> groups, List<AchievementDetail> details,
            List<AchievementCollected> collected)
        {
            const string sql = """
                DECLARE @Claimed TABLE (Name nvarchar(max), RewardDate datetime2);
                INSERT INTO @Claimed
                SELECT c.Name, c.RewardDate FROM AchievementDetail c INNER JOIN Achievement p ON p.Id = c.AchievementId
                WHERE p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId
                    AND p.Status != -1 AND c.IsReward = 1;
                IF EXISTS (SELECT 1 FROM @Claimed WHERE Name IS NULL)
                    OR EXISTS (SELECT 1 FROM @Claimed GROUP BY Name COLLATE Latin1_General_100_BIN2, DATALENGTH(Name)
                        HAVING COUNT(*) > 1)
                    THROW 51004, 'Invalid claimed achievement identities', 1;
                DELETE FROM AchievementCollected WHERE CreateUserId = @CreateUserId;
                DELETE FROM AchievementDetail WHERE CreateUserId = @CreateUserId;
                DELETE FROM Achievement WHERE CreateUserId = @CreateUserId;

                INSERT INTO Achievement (Id, Sequence, CreateDate, CreateUserId, Status, Name, Category, Total, DayTotal)
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, Name, Category, Total, DayTotal
                FROM OPENJSON(@Groups) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    Name nvarchar(max), Category int, Total real, DayTotal real);

                INSERT INTO AchievementDetail
                    (Id, Sequence, CreateDate, CreateUserId, Status, AchievementId, Name, Target,
                     IsReward, RewardDate, GainCoins, Type, Version)
                SELECT d.Id, d.Sequence, d.CreateDate, @CreateUserId, d.Status, d.AchievementId, d.Name, d.Target,
                       CAST(CASE WHEN c.Name IS NULL THEN 0 ELSE 1 END AS bit), c.RewardDate, d.GainCoins, d.Type, d.Version
                FROM OPENJSON(@Details) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    AchievementId uniqueidentifier, Name nvarchar(max), Target int, IsReward bit,
                    RewardDate datetime2, GainCoins real, Type int, Version nvarchar(max)) d
                LEFT JOIN @Claimed c ON c.Name COLLATE Latin1_General_100_BIN2 = d.Name COLLATE Latin1_General_100_BIN2
                    AND DATALENGTH(c.Name) = DATALENGTH(d.Name);

                INSERT INTO AchievementCollected (Id, Sequence, CreateDate, CreateUserId, Status, AchievementId, Name)
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, AchievementId, Name
                FROM OPENJSON(@Collected) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    AchievementId uniqueidentifier, Name nvarchar(max));
                """;
            var values = new
            {
                CreateUserId = _playerId,
                Groups = JsonSerializer.Serialize(groups),
                Details = JsonSerializer.Serialize(details),
                Collected = JsonSerializer.Serialize(collected),
            };
            await ExecuteAsync(sql, values);
        }

        /// <summary>
        /// 領取已達成項目的獎勵：標記為已領取並發放獎勵物品。需在交易內呼叫，任一步失敗會整筆回滾。
        /// </summary>
        public virtual async Task ClaimReward(string achievementItemId)
        {
            if (string.IsNullOrWhiteSpace(achievementItemId)) { throw new BusinessArgumentException(ApiMessageKeys.AchievementIdRequired); }

            await QueryGroupsAsync(lockRows: true);
            AchievementProgressModel current = (await Get())!;
            AchievementItemModel? target = current.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Name == achievementItemId);
            if (target == null) { throw new BusinessOperationException(ApiMessageKeys.AchievementItemMissing); }
            if (target.IsReward) { throw new BusinessOperationException(ApiMessageKeys.AchievementClaimed); }
            if (current.Groups.Single(g => g.Items.Contains(target)).Total < target.Target)
            { throw new BusinessOperationException(ApiMessageKeys.AchievementIncomplete); }

            // 領取前把伺服器推導的進度落地，後續的領取標記與獎勵才有對應的資料列。
            Dictionary<string, AchievementDetail> previous = (await detailService.QueryChildrenAsync<Achievement>())
                .Where(d => d.IsReward).ToDictionary(d => d.Name);
            var rows = BuildRows(current, previous);
            await PersistRows(rows.Groups, rows.Details, rows.Collected);

            string sql = $@"SELECT TOP 1 *
                              FROM {DetailTable} WITH (UPDLOCK, HOLDLOCK)
                             WHERE Name = @Name
                               AND CreateUserId = @CreateUserId";
            AchievementDetail? detail = await QueryFirstAsync<AchievementDetail?>(sql, new { Name = achievementItemId, CreateUserId = _playerId });
            if (detail == null) { throw new BusinessOperationException(ApiMessageKeys.AchievementItemMissing); }

            detail.IsReward = true;
            detail.RewardDate = DateTime.Now;
            await UpdateAsync(detail);
            await itemService.AdjustAmount(RewardItemKey, RewardAmount);
            await Revisions.Advance(nameof(AchievementProgressModel));
        }

        private Task<List<Achievement>> QueryGroupsAsync(bool lockRows) =>
            achievementService.QueryOwnedRowsAsync(orderBy: row => row.Sequence, lockRows: lockRows);
    }
}
