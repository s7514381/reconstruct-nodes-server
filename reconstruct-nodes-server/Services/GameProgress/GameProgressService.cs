using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 遊戲進度服務：無父表。純量與設定存於 PlayerParams（Group 以 game_progress 開頭）；
    /// 部署只引用獨立保存的角色，戰場道路依 CreateUserId 拆表。
    /// </summary>
    public class GameProgressService(BaseServiceArgument argument) : _SnapshotService<PlayerParams, GameProgressModel>(argument)
    {
        private const string ProgressGroup = GameProgressParamGroups.Progress;
        private const string SettingGroupPrefix = GameProgressParamGroups.SettingPrefix;
        private const long DeploymentCost = 60;

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
                                 FROM {_tableName}
                                WHERE Status != -1
                                  AND CreateUserId = @CreateUserId
                                  AND {ProgressRowFilter}
                             ORDER BY [Group], Sequence";
            List<PlayerParams> progressRows = (await QueryAsync<PlayerParams>(rowSql, new { CreateUserId = _playerId })).ToList();
            if (!progressRows.Any(row => row.Group == ProgressGroup && row.Name == ProgressKey.Version)) { return null; }
            return BuildSnapshot(progressRows,
                await QuerySnapshotRowsAsync<Deployment>(orderBy: row => row.Sequence),
                await QuerySnapshotRowsAsync<BattlefieldRoad>(orderBy: row => row.Sequence),
                await Revisions.Read(nameof(GameProgressModel)), await _lazy.GoldService.Value.GetBalance());
        }

        public Task<GameProgressModel?> GetCommand() => ReadSnapshotCommandAsync(GetRead);

        public SnapshotRead<GameProgressModel?> GetRead => new($"""
            SELECT * FROM PlayerParams WHERE Status != -1 AND CreateUserId = @CreateUserId
                AND {ProgressRowFilter} ORDER BY [Group], Sequence;
            SELECT * FROM Deployment WHERE Status != -1 AND CreateUserId = @CreateUserId ORDER BY Sequence;
            SELECT * FROM BattlefieldRoad WHERE Status != -1 AND CreateUserId = @CreateUserId ORDER BY Sequence;
            SELECT Value FROM PlayerParams WHERE CreateUserId = @CreateUserId
                AND [Group] = 'server.revisions' AND Name = 'GameProgressModel' AND Status != -1;
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
                gold.Count == 0 ? GoldService.StartingGold : GoldService.ReadBalance(gold[0]));
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
                ClearedWaves = settingRows[ClearedWavesGroup].Select(param => JsonSerializer.Deserialize<JsonElement>(param.Value)).ToList(),
                WaveCompleted = ReadBool(progressValues, ProgressKey.WaveCompleted),
            };
        }

        /// <summary>
        /// 以整份快照覆寫遊戲進度：先刪除此玩家既有的進度列再重新寫入。需在交易內呼叫。
        /// </summary>
        public override async Task Save(GameProgressModel model)
        {
            ValidateSnapshot(model);
            await SaveSnapshot(model);
        }

        protected override void ValidateSnapshot(GameProgressModel model) => ValidateProgress(model);

        protected override async Task SaveSnapshot(GameProgressModel model)
        {
            // 與武器刪除共用玩家鎖，避免刪武器與寫入裝備參照交錯
            if (model.RequestId.HasValue)
            {
                // The existing debit receipt must replay before CAS and ownership checks.
                ProgressSaveResult result = await _lazy.GoldService.Value.Execute(model.RequestId.Value, "progress_save", model, async account =>
                {
                    await Revisions.Check(nameof(GameProgressModel), model.ExpectedRevision);
                    int newDeployments = await NewDeploymentCount(model);
                    if (newDeployments > 0) { GoldService.Spend(account, checked(newDeployments * DeploymentCost)); }
                    await ReplaceProgressAsync(model);
                    if (model.ExpectedRevision.HasValue) { model.Revision = await Revisions.Advance(nameof(GameProgressModel)); }
                    return new ProgressSaveResult { Gold = account.Balance, Revision = model.Revision };
                });
                model.Revision = result.Revision;
                return;
            }
            await Revisions.Check(nameof(GameProgressModel), model.ExpectedRevision);
            if (await NewDeploymentCount(model) > 0) { throw new BusinessArgumentException(ApiMessageKeys.ProgressRequestRequired); }
            await ReplaceProgressAsync(model);
            if (model.ExpectedRevision.HasValue) { model.Revision = await Revisions.Advance(nameof(GameProgressModel)); }
        }

        private async Task<int> NewDeploymentCount(GameProgressModel model)
        {
            await ValidateCharactersAsync(model.DeploymentLog ?? new());
            var previous = await QuerySnapshotRowsAsync<Deployment>(orderBy: row => row.Sequence);
            var previousKeys = previous.Select(row => (row.SpotIndex, row.CharacterId, row.CellX, row.CellY)).ToHashSet();
            return (model.DeploymentLog ?? new()).Count(row =>
                !previousKeys.Contains((row.SpotIndex, row.CharacterId, row.Cell?[0], row.Cell?[1])));
        }

        internal sealed record ProgressWriteRows(Guid? CreateUserId, string Parameters, string Deployments, string Roads);

        internal ProgressWriteRows BuildWriteRows(GameProgressModel model)
        {
            List<PlayerParams> parameters = BuildProgressRows(model).ToList();
            List<Deployment> deployments = (model.DeploymentLog ?? []).Select((deployment, sequence) => new Deployment
            {
                SpotIndex = deployment.SpotIndex, CellX = deployment.Cell?[0], CellY = deployment.Cell?[1],
                CharacterId = deployment.CharacterId, Wave = deployment.Wave, Sequence = sequence,
            }).ToList();
            List<BattlefieldRoad> roads = (model.Battlefield?.Roads ?? []).Select((road, sequence) => new BattlefieldRoad
            {
                X = road.X, Y = road.Y, Corner = road.Corner, Rotation = road.Rotation, Sequence = sequence,
            }).ToList();

            return new(_playerId, JsonSerializer.Serialize(parameters),
                JsonSerializer.Serialize(deployments), JsonSerializer.Serialize(roads));
        }

        private Task ReplaceProgressAsync(GameProgressModel model) =>
            ExecuteAsync(ProgressWriteSql, BuildWriteRows(model));

        internal static string ProgressWriteSql => $"""
                DELETE FROM PlayerParams WHERE CreateUserId = @CreateUserId
                    AND {ProgressRowFilter} AND NOT ([Group] = 'game_progress' AND Name = 'gold')
                    AND [Group] NOT IN ('game_progress.unlocked_bullet_kinds', 'game_progress.unlocked_auxiliary_kinds',
                        'game_progress.skill_suffixes', 'game_progress.refine_item_counts');

                INSERT INTO PlayerParams (Id, Sequence, CreateDate, CreateUserId, Status, Name, [Group], Value)
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, Name, [Group], Value
                FROM OPENJSON(@Parameters) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    Name nvarchar(max), [Group] nvarchar(max), Value nvarchar(max));
                DELETE FROM Deployment WHERE CreateUserId = @CreateUserId;
                INSERT INTO Deployment (Id, Sequence, CreateDate, CreateUserId, Status, SpotIndex, CellX, CellY, CharacterId, Wave)
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, SpotIndex, CellX, CellY, CharacterId, Wave
                FROM OPENJSON(@Deployments) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    SpotIndex int, CellX int, CellY int, CharacterId uniqueidentifier, Wave int);
                DELETE FROM BattlefieldRoad WHERE CreateUserId = @CreateUserId;
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
            if (model.Battlefield != null)
            {
                if (model.Battlefield.Castle is not { Count: 2 }) { throw new BusinessArgumentException(ApiMessageKeys.ProgressCastleInvalid); }
                if ((model.Battlefield.Roads ?? new()).Any(road => road == null)) { throw new BusinessArgumentException(ApiMessageKeys.ProgressRoadsInvalid); }
            }

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
                "SELECT Id FROM Character WHERE CreateUserId = @CreateUserId AND Status != -1 AND Kind IN (0, 1, 2) AND Id IN @Ids",
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
