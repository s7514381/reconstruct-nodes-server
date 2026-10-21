using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 遊戲進度服務：無父表。純量與設定存於 PlayerParams（Group 以 game_progress 開頭）；
    /// 部署只引用獨立保存的角色，戰場道路依 CreateUserId 拆表。
    /// </summary>
    public class GameProgressCommand(BaseServiceArgument argument, DeploymentService deploymentService,
        BattlefieldRoadService roadService, PlayerParamsService paramsService, RevisionCommand Revisions) : BaseService(argument)
    {
        private const string ProgressGroup = GameProgressParamGroups.Progress;
        private const string SettingGroupPrefix = GameProgressParamGroups.SettingPrefix;

        // LIKE 中的 [_] 代表字面底線，避免底線被當成萬用字元
        private const string ProgressRowFilter = "([Group] = 'game_progress' OR [Group] LIKE 'game[_]progress.%')";

        private const string UnlockedBulletKindsGroup = SettingGroupPrefix + "unlocked_bullet_kinds";
        private const string UnlockedAuxiliaryKindsGroup = SettingGroupPrefix + "unlocked_auxiliary_kinds";
        private const string UnlockedCharacterKindsGroup = SettingGroupPrefix + "unlocked_character_kinds";
        private const string ProgressionGroup = SettingGroupPrefix + "progression";
        private const string ClearedWavesGroup = SettingGroupPrefix + "cleared_waves";

        /// <summary>
        /// game_progress 群組內的純量鍵名
        /// </summary>
        private static class ProgressKey
        {
            public const string Version = "version";
            public const string Wave = "wave";
            public const string Gold = "gold";
            public const string MaxWaveReached = "max_wave_reached";
            public const string Difficulty = "difficulty";
            public const string LifetimeEnemiesDefeated = "lifetime_enemies_defeated";
            public const string InfiniteLevel = "infinite_level";
            public const string WaveCompleted = "wave_completed";
            public const string TrialReturnDifficulty = "trial_return_difficulty";
            public const string TrialReturnWave = "trial_return_wave";
            public const string CaptainCharacterId = "captain_character_id";
            public const string BattlefieldVersion = "battlefield_version";
            public const string BattlefieldColumns = "battlefield_columns";
            public const string BattlefieldRows = "battlefield_rows";
            public const string CastleX = "castle_x";
            public const string CastleY = "castle_y";
        }

        /// <summary>
        /// 讀取目前玩家的遊戲進度，沒有資料（缺少 version 純量）時回傳 null
        /// </summary>
        public virtual async Task<GameProgressModel?> Get()
        {
            string rowSql = $@"SELECT *
                                 FROM {paramsService._tableName}
                                WHERE Status != -1
                                  AND CreateUserId = @CreateUserId
                                  AND {ProgressRowFilter}
                             ORDER BY [Group], Sequence";
            List<PlayerParams> progressRows = (await QueryAsync<PlayerParams>(rowSql, new { CreateUserId = _playerId })).ToList();
            if (!progressRows.Any(row => row.Group == ProgressGroup && row.Name == ProgressKey.Version)) { return null; }
            return BuildSnapshot(progressRows,
                await deploymentService.QueryOwnedRowsAsync(orderBy: row => row.Sequence),
                await roadService.QueryOwnedRowsAsync(orderBy: row => row.Sequence),
                await Revisions.Read(nameof(GameProgressModel)), await _lazy.GoldCommand.Value.GetBalance());
        }

        public Task<GameProgressModel?> GetCommand() => ReadSnapshotCommandAsync(GetRead);

        public SnapshotRead<GameProgressModel?> GetRead => new($"""
            {OwnedSql.Select<PlayerParams>(ProgressRowFilter, orderBy: "[Group], Sequence")}
            {OwnedSql.Select<Deployment>(orderBy: "Sequence")}
            {OwnedSql.Select<BattlefieldRoad>(orderBy: "Sequence")}
            {RevisionCommand.SelectValueSql(nameof(GameProgressModel))}
            """, async reader =>
        {
            var rows = (await reader.ReadAsync<PlayerParams>()).ToList();
            var deployments = (await reader.ReadAsync<Deployment>()).ToList();
            var roads = (await reader.ReadAsync<BattlefieldRoad>()).ToList();
            string? revision = await reader.ReadSingleOrDefaultAsync<string>();
            List<PlayerParams> gold = rows.Where(row => row.Group == ProgressGroup && row.Name == ProgressKey.Gold).ToList();
            if (gold.Count > 1) { throw new InvalidOperationException("玩家金幣參數重複"); }
            return BuildSnapshot(rows, deployments, roads,
                revision == null ? 0 : long.Parse(revision, CultureInfo.InvariantCulture),
                gold.Count == 0 ? GoldCommand.StartingGold : GoldCommand.ReadBalance(gold[0]));
        });

        internal static GameProgressModel? BuildSnapshot(List<PlayerParams> progressRows,
            List<Deployment> deploymentRows, List<BattlefieldRoad> roadRows, long revision, long gold)
        {
            Dictionary<string, string> progressValues = progressRows
                .Where(param => param.Group == ProgressGroup)
                .ToDictionary(param => param.Name, param => param.Value);
            if (!progressValues.ContainsKey(ProgressKey.Version)) { return null; }

            ILookup<string, PlayerParams> settingRows = progressRows
                .Where(param => param.Group != ProgressGroup)
                .ToLookup(param => param.Group);

            List<GameProgressDeploymentModel> deploymentLog = deploymentRows.Select(row =>
            {
                GameProgressDeploymentModel deployment = new()
                {
                    SpotIndex = row.SpotIndex,
                    CharacterId = row.CharacterId,
                    Wave = row.Wave,
                    Cell = row.CellX.HasValue && row.CellY.HasValue ? new List<int> { row.CellX.Value, row.CellY.Value } : null,
                };
                return deployment;
            }).ToList();

            return new GameProgressModel
            {
                Revision = revision,
                Version = ReadInt(progressValues, ProgressKey.Version),
                DeploymentLog = deploymentLog,
                CaptainCharacterId = progressValues.TryGetValue(ProgressKey.CaptainCharacterId, out string? captain)
                    && Guid.TryParse(captain, out Guid captainId) ? captainId : null,
                Wave = ReadInt(progressValues, ProgressKey.Wave),
                Gold = gold,
                MaxWaveReached = ReadInt(progressValues, ProgressKey.MaxWaveReached),
                UnlockedBulletKinds = ReadSettingGroup(settingRows, UnlockedBulletKindsGroup),
                UnlockedAuxiliaryKinds = ReadSettingGroup(settingRows, UnlockedAuxiliaryKindsGroup),
                UnlockedCharacterKinds = ReadSettingGroup(settingRows, UnlockedCharacterKindsGroup),
                Progression = ReadSettingGroup(settingRows, ProgressionGroup),
                Battlefield = BuildBattlefield(progressValues, roadRows),
                Difficulty = ReadInt(progressValues, ProgressKey.Difficulty),
                LifetimeEnemiesDefeated = ReadLong(progressValues, ProgressKey.LifetimeEnemiesDefeated),
                InfiniteLevel = ReadInt(progressValues, ProgressKey.InfiniteLevel),
                ClearedWaves = PadClearedWaves(settingRows[ClearedWavesGroup]
                    .Select(param => JsonSerializer.Deserialize<JsonElement>(param.Value)).ToList()),
                WaveCompleted = ReadBool(progressValues, ProgressKey.WaveCompleted),
                TrialReturnDifficulty = ReadInt(progressValues, ProgressKey.TrialReturnDifficulty),
                TrialReturnWave = Math.Max(1, ReadInt(progressValues, ProgressKey.TrialReturnWave)),
            };
        }

        /// <summary>舊帳號只有 4 個槽；補齊到目前的關卡槽數，讓試煉通關紀錄從 0 開始。</summary>
        private static List<JsonElement> PadClearedWaves(List<JsonElement> cleared)
        {
            while (cleared.Count < StageRules.SlotCount) { cleared.Add(JsonSerializer.SerializeToElement(0)); }
            return cleared;
        }

        /// <summary>
        /// 以整份快照覆寫遊戲進度：先刪除此玩家既有的進度列再重新寫入。需在交易內呼叫。
        /// </summary>
        public async Task Save(GameProgressModel model)
        {
            ValidateProgress(model);
            await SaveSnapshot(model);
        }

        private async Task SaveSnapshot(GameProgressModel model)
        {
            await Revisions.Check(nameof(GameProgressModel), model.ExpectedRevision);
            if (await NewDeploymentCount(model) > 0) { throw new BusinessArgumentException(ApiMessageKeys.ProgressRequestRequired); }
            await ReplaceProgressAsync(model);
            if (model.ExpectedRevision.HasValue) { model.Revision = await Revisions.Advance(nameof(GameProgressModel)); }
        }

        private async Task<int> NewDeploymentCount(GameProgressModel model)
        {
            await ValidateCharactersAsync(model.DeploymentLog ?? new());
            var previous = await deploymentService.QueryOwnedRowsAsync(orderBy: row => row.Sequence);
            return NewDeploymentCount(model, previous);
        }

        internal static int NewDeploymentCount(GameProgressModel model, IEnumerable<Deployment> previous)
        {
            var previousKeys = previous.Select(row => (row.SpotIndex, row.CharacterId, row.CellX, row.CellY)).ToHashSet();
            return (model.DeploymentLog ?? new()).Count(row =>
                !previousKeys.Contains((row.SpotIndex, row.CharacterId, row.Cell?[0], row.Cell?[1])));
        }

        /// <summary>批次版的部署角色檢查：ownedIds 為本人可部署職業（不含核心）的有效角色。</summary>
        internal static void ValidateCharacters(IEnumerable<GameProgressDeploymentModel> deployments, IReadOnlySet<Guid> ownedIds)
        {
            if (deployments.Any(row => !ownedIds.Contains(row.CharacterId)))
            { throw new BusinessArgumentException(ApiMessageKeys.ProgressCharacterNotOwned); }
        }

        internal sealed record ProgressWriteRows(Guid? CreateUserId, string Parameters, string Deployments, string Roads);

        internal sealed record ProgressRowSet(List<PlayerParams> Parameters, List<Deployment> Deployments, List<BattlefieldRoad> Roads);

        /// <summary>批次命令一次讀回的進度資料：進度／revision／指定 economy 參數、部署與道路。</summary>
        internal sealed class ProgressRows
        {
            public List<PlayerParams> Parameters { get; set; } = [];
            public List<Deployment> Deployments { get; set; } = [];
            public List<BattlefieldRoad> Roads { get; set; } = [];
        }

        /// <summary>economyNames 只能是固定名稱。</summary>
        internal static SnapshotRead<ProgressRows> StateRead(params string[] economyNames) => new($"""
            {OwnedSql.Select<PlayerParams>($"""
                (([Group] = '{RevisionCommand.RevisionGroup}' AND Name = '{nameof(GameProgressModel)}')
                    OR {ProgressRowFilter}
                    OR ([Group] = 'economy' AND Name IN ({string.Join(", ", economyNames.Select(name => $"'{name}'"))})))
                """, orderBy: "[Group], Sequence")}
            {OwnedSql.Select<Deployment>(orderBy: "Sequence")}
            {OwnedSql.Select<BattlefieldRoad>(orderBy: "Sequence")}
            """, async reader => new ProgressRows
        {
            Parameters = (await reader.ReadAsync<PlayerParams>()).ToList(),
            Deployments = (await reader.ReadAsync<Deployment>()).ToList(),
            Roads = (await reader.ReadAsync<BattlefieldRoad>()).ToList(),
        });

        /// <summary>讀回本人可部署職業（不含核心）的有效角色 Id 與種類，供部署與戰場空間驗證。</summary>
        internal static SnapshotRead<List<Character>> DeployableCharactersRead => new(
            OwnedSql.Select<Character>($"Kind IN ({CharacterKinds.DeployableSqlList})", columns: "Id, Kind"),
            async reader => (await reader.ReadAsync<Character>()).ToList());

        private static bool IsProgressRow(PlayerParams row) =>
            row.Group == ProgressGroup || row.Group.StartsWith(GameProgressParamGroups.SettingPrefix, StringComparison.Ordinal);

        /// <summary>整份覆寫不會動到的群組：由其他流程維護的解鎖、技能後綴與精煉計數（金幣另外排除）。</summary>
        private static readonly string[] PreservedGroups =
        [
            UnlockedBulletKindsGroup, UnlockedAuxiliaryKindsGroup,
            SettingGroupPrefix + "skill_suffixes", SettingGroupPrefix + "refine_item_counts",
        ];

        private static bool IsReplaced(PlayerParams row) =>
            !(row.Group == ProgressGroup && row.Name == ProgressKey.Gold) && !PreservedGroups.Contains(row.Group);

        /// <summary>由批次讀回的列組出快照；金幣列缺少時用初始金幣。</summary>
        internal static GameProgressModel? BuildFrom(ProgressRows rows, long revision)
        {
            List<PlayerParams> progressRows = rows.Parameters.Where(IsProgressRow)
                .OrderBy(row => row.Group, StringComparer.Ordinal).ThenBy(row => row.Sequence).ToList();
            PlayerParams? gold = progressRows.SingleOrDefault(row => row.Group == ProgressGroup && row.Name == ProgressKey.Gold);
            return BuildSnapshot(progressRows, rows.Deployments, rows.Roads, revision,
                gold == null ? GoldCommand.StartingGold : GoldCommand.ReadBalance(gold));
        }

        internal static ProgressRowSet BuildRows(GameProgressModel model) => new(
            BuildProgressRows(model).ToList(),
            (model.DeploymentLog ?? []).Select((deployment, sequence) => new Deployment
            {
                SpotIndex = deployment.SpotIndex, CellX = deployment.Cell?[0], CellY = deployment.Cell?[1],
                CharacterId = deployment.CharacterId, Wave = deployment.Wave, Sequence = sequence,
            }).ToList(),
            (model.Battlefield?.Roads ?? []).Select((road, sequence) => new BattlefieldRoad
            {
                X = road.X, Y = road.Y, Corner = road.Corner, Rotation = road.Rotation, Sequence = sequence,
            }).ToList());

        private static ProgressWriteRows ToWriteRows(Guid? owner, ProgressRowSet rows) => new(owner,
            JsonSerializer.Serialize(rows.Parameters), JsonSerializer.Serialize(rows.Deployments),
            JsonSerializer.Serialize(rows.Roads));

        /// <summary>批次版整份覆寫：加入寫入批次，並回傳寫入後（含新 revision 與金幣）的快照。</summary>
        internal static GameProgressModel QueueReplace(SqlWriteBatch writes, ProgressRows state,
            GameProgressModel model, long revision, long gold)
        {
            ProgressRowSet rows = BuildRows(model);
            writes.Add(ProgressWriteSql, ToWriteRows(null, rows));
            var after = new ProgressRows
            {
                Parameters = state.Parameters.Where(row => IsProgressRow(row) && !IsReplaced(row)).Concat(rows.Parameters).ToList(),
                Deployments = rows.Deployments, Roads = rows.Roads,
            };
            GameProgressModel saved = BuildFrom(after, revision)
                ?? throw new BusinessOperationException(ApiMessageKeys.ProgressMissing);
            saved.Gold = gold;
            return saved;
        }

        internal ProgressWriteRows BuildWriteRows(GameProgressModel model) => ToWriteRows(_playerId, BuildRows(model));

        private Task ReplaceProgressAsync(GameProgressModel model) =>
            ExecuteAsync(ProgressWriteSql, BuildWriteRows(model));

        internal static string ProgressWriteSql => $"""
                {OwnedSql.Delete<PlayerParams>($"""
                    {ProgressRowFilter} AND NOT ([Group] = 'game_progress' AND Name = 'gold')
                    AND [Group] NOT IN ({string.Join(", ", PreservedGroups.Select(group => $"'{group}'"))})
                    """)}

                INSERT INTO PlayerParams (Id, Sequence, CreateDate, CreateUserId, Status, Name, [Group], Value)
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, Name, [Group], Value
                FROM OPENJSON(@Parameters) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    Name nvarchar(max), [Group] nvarchar(max), Value nvarchar(max));
                {OwnedSql.Delete<Deployment>()}
                INSERT INTO Deployment (Id, Sequence, CreateDate, CreateUserId, Status, SpotIndex, CellX, CellY, CharacterId, Wave)
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, SpotIndex, CellX, CellY, CharacterId, Wave
                FROM OPENJSON(@Deployments) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    SpotIndex int, CellX int, CellY int, CharacterId uniqueidentifier, Wave int);
                {OwnedSql.Delete<BattlefieldRoad>()}
                INSERT INTO BattlefieldRoad (Id, Sequence, CreateDate, CreateUserId, Status, X, Y, Corner, Rotation)
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, X, Y, Corner, Rotation
                FROM OPENJSON(@Roads) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    X int, Y int, Corner bit, Rotation int);
                """;

        /// <summary>
        /// 寫入前檢查結構，避免格式錯誤造成資料不完整
        /// </summary>
        internal static void ValidateProgress(GameProgressModel model)
        {
            if (!StageRules.IsValid(model.Difficulty) ||
                !StageRules.IsValid(model.TrialReturnDifficulty) || StageRules.IsLimitedStage(model.TrialReturnDifficulty) ||
                model.TrialReturnWave < 1)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyWaveInvalid); }
            if (model.Battlefield != null)
            {
                if (model.Battlefield.Castle is not { Count: 2 }) { throw new BusinessArgumentException(ApiMessageKeys.ProgressCastleInvalid); }
                if ((model.Battlefield.Roads ?? new()).Any(road => road == null)) { throw new BusinessArgumentException(ApiMessageKeys.ProgressRoadsInvalid); }
            }

            if (model.CaptainCharacterId is { } captain &&
                (model.DeploymentLog ?? new()).All(row => row?.CharacterId != captain))
            { throw new BusinessArgumentException("battlefield.captain_invalid"); }

            HashSet<(int?, int?, int?)> spots = new();
            foreach (GameProgressDeploymentModel? deployment in model.DeploymentLog ?? new())
            {
                if (deployment == null) { throw new BusinessArgumentException(ApiMessageKeys.ProgressDeploymentRequired); }
                if (deployment.Cell != null && deployment.Cell.Count != 2) { throw new BusinessArgumentException(ApiMessageKeys.ProgressCellInvalid); }

                if (deployment.CharacterId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.ProgressCharacterRequired); }
                var spot = (deployment.Cell?[0], deployment.Cell?[1], deployment.Cell == null ? (int?)deployment.SpotIndex : null);
                if (!spots.Add(spot)) { throw new BusinessArgumentException(ApiMessageKeys.ProgressCellDuplicate); }
            }
        }

        private async Task ValidateCharactersAsync(List<GameProgressDeploymentModel> deployments)
        {
            Guid[] ids = deployments.Select(row => row.CharacterId).Distinct().ToArray();
            if (ids.Length == 0) { return; }
            var ownedIds = (await QueryAsync<Guid>(
                $"SELECT Id FROM Character WHERE CreateUserId = @CreateUserId AND Status != -1 AND Kind IN ({CharacterKinds.DeployableSqlList}) AND Id IN @Ids",
                new { CreateUserId = _playerId, Ids = ids })).ToHashSet();
            if (ids.Any(id => !ownedIds.Contains(id))) { throw new BusinessArgumentException(ApiMessageKeys.ProgressCharacterNotOwned); }
        }

        private static IEnumerable<PlayerParams> BuildProgressRows(GameProgressModel model)
        {
            yield return Scalar(ProgressKey.Version, model.Version);
            yield return Scalar(ProgressKey.Wave, model.Wave);
            yield return Scalar(ProgressKey.MaxWaveReached, model.MaxWaveReached);
            yield return Scalar(ProgressKey.Difficulty, model.Difficulty);
            yield return Scalar(ProgressKey.LifetimeEnemiesDefeated, model.LifetimeEnemiesDefeated);
            yield return Scalar(ProgressKey.InfiniteLevel, model.InfiniteLevel);
            yield return Scalar(ProgressKey.WaveCompleted, model.WaveCompleted);
            yield return Scalar(ProgressKey.TrialReturnDifficulty, model.TrialReturnDifficulty);
            yield return Scalar(ProgressKey.TrialReturnWave, model.TrialReturnWave);
            if (model.CaptainCharacterId is { } captainId)
            { yield return Scalar(ProgressKey.CaptainCharacterId, captainId.ToString("D")); }

            if (model.Battlefield != null)
            {
                yield return Scalar(ProgressKey.BattlefieldVersion, model.Battlefield.Version);
                yield return Scalar(ProgressKey.BattlefieldColumns, model.Battlefield.Columns);
                yield return Scalar(ProgressKey.BattlefieldRows, model.Battlefield.Rows);
                yield return Scalar(ProgressKey.CastleX, model.Battlefield.Castle[0]);
                yield return Scalar(ProgressKey.CastleY, model.Battlefield.Castle[1]);
            }

            foreach ((string group, Dictionary<string, JsonElement> values) in EnumerateSettingGroups(model))
            {
                int sequence = 0;
                foreach ((string name, JsonElement value) in values)
                {
                    yield return new PlayerParams
                    {
                        Group = group, Name = name, Value = value.GetRawText(), Sequence = sequence++,
                    };
                }
            }

            int index = 0;
            foreach (JsonElement value in model.ClearedWaves ?? [])
            {
                yield return new PlayerParams
                {
                    Group = ClearedWavesGroup, Name = index.ToString(CultureInfo.InvariantCulture),
                    Value = value.GetRawText(), Sequence = index++,
                };
            }
        }

        private static PlayerParams Scalar(string name, object value) => new()
        {
            Group = ProgressGroup, Name = name, Value = Convert.ToString(value, CultureInfo.InvariantCulture),
        };

        private static IEnumerable<(string Group, Dictionary<string, JsonElement> Values)> EnumerateSettingGroups(GameProgressModel model)
        {
            yield return (UnlockedCharacterKindsGroup, model.UnlockedCharacterKinds ?? new());
            yield return (ProgressionGroup, model.Progression ?? new());
        }

        private static Dictionary<string, JsonElement> ReadSettingGroup(ILookup<string, PlayerParams> settingRows, string group)
        {
            return settingRows[group].ToDictionary(param => param.Name, param => JsonSerializer.Deserialize<JsonElement>(param.Value));
        }

        private static int ReadInt(IReadOnlyDictionary<string, string> values, string name) =>
            values.TryGetValue(name, out string? value) ? int.Parse(value, CultureInfo.InvariantCulture) : 0;

        private static long ReadLong(IReadOnlyDictionary<string, string> values, string name) =>
            values.TryGetValue(name, out string? value) ? long.Parse(value, CultureInfo.InvariantCulture) : 0;

        private static bool ReadBool(IReadOnlyDictionary<string, string> values, string name) =>
            values.TryGetValue(name, out string? value) && bool.Parse(value);

        private static GameProgressBattlefieldModel? BuildBattlefield(IReadOnlyDictionary<string, string> values, IEnumerable<BattlefieldRoad> roadRows)
        {
            if (!values.ContainsKey(ProgressKey.BattlefieldVersion)) { return null; }

            return new GameProgressBattlefieldModel
            {
                Version = ReadInt(values, ProgressKey.BattlefieldVersion),
                Columns = ReadInt(values, ProgressKey.BattlefieldColumns),
                Rows = ReadInt(values, ProgressKey.BattlefieldRows),
                Castle = new List<int> { ReadInt(values, ProgressKey.CastleX), ReadInt(values, ProgressKey.CastleY) },
                Roads = roadRows.Select(road => new GameProgressRoadModel
                {
                    X = road.X,
                    Y = road.Y,
                    Corner = road.Corner,
                    Rotation = road.Rotation,
                }).ToList(),
            };
        }

    }
}
