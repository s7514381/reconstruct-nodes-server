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

        // LIKE 中的 [_] 代表字面底線，避免底線被當成萬用字元
        private const string ProgressRowFilter = "([Group] = 'game_progress' OR [Group] LIKE 'game[_]progress.%')";

        private const string UnlockedBulletKindsGroup = SettingGroupPrefix + "unlocked_bullet_kinds";
        private const string UnlockedAuxiliaryKindsGroup = SettingGroupPrefix + "unlocked_auxiliary_kinds";
        private const string UnlockedCharacterKindsGroup = SettingGroupPrefix + "unlocked_character_kinds";
        private const string SkillSuffixesGroup = SettingGroupPrefix + "skill_suffixes";
        private const string RefineItemCountsGroup = SettingGroupPrefix + "refine_item_counts";
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
            await LockPlayerAsync();
            string rowSql = $@"SELECT *
                                 FROM {_tableName}
                                WHERE Status != -1
                                  AND CreateUserId = @CreateUserId
                                  AND {ProgressRowFilter}
                             ORDER BY [Group], Sequence";
            List<PlayerParams> progressRows = (await QueryAsync<PlayerParams>(rowSql, new { CreateUserId = _playerId })).ToList();

            Dictionary<string, string> progressValues = progressRows
                .Where(param => param.Group == ProgressGroup)
                .ToDictionary(param => param.Name, param => param.Value);
            if (!progressValues.ContainsKey(ProgressKey.Version)) { return null; }

            ILookup<string, PlayerParams> settingRows = progressRows
                .Where(param => param.Group != ProgressGroup)
                .ToLookup(param => param.Group);

            List<Deployment> deploymentRows = await QuerySnapshotRowsAsync<Deployment>(orderBy: row => row.Sequence);
            List<BattlefieldRoad> roadRows = await QuerySnapshotRowsAsync<BattlefieldRoad>(orderBy: row => row.Sequence);

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
                Version = ReadInt(progressValues, ProgressKey.Version),
                DeploymentLog = deploymentLog,
                Wave = ReadInt(progressValues, ProgressKey.Wave),
                Gold = await _lazy.GoldService.Value.GetBalance(),
                MaxWaveReached = ReadInt(progressValues, ProgressKey.MaxWaveReached),
                UnlockedBulletKinds = ReadSettingGroup(settingRows, UnlockedBulletKindsGroup),
                UnlockedAuxiliaryKinds = ReadSettingGroup(settingRows, UnlockedAuxiliaryKindsGroup),
                UnlockedCharacterKinds = ReadSettingGroup(settingRows, UnlockedCharacterKindsGroup),
                SkillSuffixes = ReadSettingGroup(settingRows, SkillSuffixesGroup),
                RefineItemCounts = ReadSettingGroup(settingRows, RefineItemCountsGroup),
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
        public override Task Save(GameProgressModel model) => base.Save(model);

        protected override void ValidateSnapshot(GameProgressModel model) => ValidateProgress(model);

        protected override async Task SaveSnapshot(GameProgressModel model)
        {
            // 與武器刪除共用玩家鎖，避免刪武器與寫入裝備參照交錯
            await LockPlayerAsync();
            await ValidateCharactersAsync(model.DeploymentLog ?? new());
            var previous = await QuerySnapshotRowsAsync<Deployment>(orderBy: row => row.Sequence);
            var previousKeys = previous.Select(row => (row.SpotIndex, row.CharacterId, row.CellX, row.CellY)).ToHashSet();
            int newDeployments = (model.DeploymentLog ?? new()).Count(row =>
                !previousKeys.Contains((row.SpotIndex, row.CharacterId, row.Cell?[0], row.Cell?[1])));
            if (newDeployments > 0 && !model.RequestId.HasValue)
            {
                throw new BusinessArgumentException(ApiMessageKeys.ProgressRequestRequired);
            }
            if (model.RequestId.HasValue)
            {
                await _lazy.GoldService.Value.Execute(model.RequestId.Value, "progress_save", model, async account =>
                {
                    if (newDeployments > 0) { GoldService.Spend(account, checked(newDeployments * 60L)); }
                    await ReplaceProgressAsync(model);
                    return new GoldOperationResult { Gold = account.Balance };
                });
                return;
            }
            await ReplaceProgressAsync(model);
        }

        private async Task ReplaceProgressAsync(GameProgressModel model)
        {
            await DeleteProgressRowsAsync();

            await InsertScalarsAsync(model);
            await InsertSettingsAsync(model);
            await InsertDeploymentsAsync(model.DeploymentLog ?? new());
            await InsertRoadsAsync(model.Battlefield);
        }

        /// <summary>
        /// 寫入前檢查結構，避免格式錯誤造成資料不完整
        /// </summary>
        private static void ValidateProgress(GameProgressModel model)
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

        /// <summary>
        /// 依子表由深到淺刪除此玩家的進度列；子列都以 CreateUserId 歸屬，不再依賴父列
        /// </summary>
        private async Task DeleteProgressRowsAsync()
        {
            await DeleteSnapshotRowsAsync<Deployment>();
            await DeleteSnapshotRowsAsync<BattlefieldRoad>();

            await ExecuteAsync($@"DELETE FROM {_tableName} WHERE CreateUserId = @CreateUserId
                AND {ProgressRowFilter} AND NOT ([Group] = 'game_progress' AND Name = 'gold')
                AND [Group] NOT IN ('game_progress.unlocked_bullet_kinds', 'game_progress.unlocked_auxiliary_kinds')",
                new { CreateUserId = _playerId });
        }

        private async Task InsertScalarsAsync(GameProgressModel model)
        {
            await InsertScalarAsync(ProgressKey.Version, model.Version);
            await InsertScalarAsync(ProgressKey.Wave, model.Wave);
            await InsertScalarAsync(ProgressKey.MaxWaveReached, model.MaxWaveReached);
            await InsertScalarAsync(ProgressKey.Difficulty, model.Difficulty);
            await InsertScalarAsync(ProgressKey.LifetimeEnemiesDefeated, model.LifetimeEnemiesDefeated);
            await InsertScalarAsync(ProgressKey.InfiniteLevel, model.InfiniteLevel);
            await InsertScalarAsync(ProgressKey.WaveCompleted, model.WaveCompleted);

            // 沒有戰場時不寫入戰場鍵，讀取時即為 null
            if (model.Battlefield == null) { return; }

            await InsertScalarAsync(ProgressKey.BattlefieldVersion, model.Battlefield.Version);
            await InsertScalarAsync(ProgressKey.BattlefieldColumns, model.Battlefield.Columns);
            await InsertScalarAsync(ProgressKey.BattlefieldRows, model.Battlefield.Rows);
            await InsertScalarAsync(ProgressKey.CastleX, model.Battlefield.Castle[0]);
            await InsertScalarAsync(ProgressKey.CastleY, model.Battlefield.Castle[1]);
        }

        private async Task InsertScalarAsync(string name, object value)
        {
            await InsertSnapshotRowAsync(new PlayerParams
            {
                Group = ProgressGroup,
                Name = name,
                Value = Convert.ToString(value, CultureInfo.InvariantCulture),
            });
        }

        private async Task InsertSettingsAsync(GameProgressModel model)
        {
            foreach ((string group, Dictionary<string, JsonElement> values) in EnumerateSettingGroups(model))
            {
                await InsertSnapshotRowsAsync(values, (entry, _) => new PlayerParams
                {
                    Group = group, Name = entry.Key, Value = entry.Value.GetRawText(),
                });
            }

            List<JsonElement> clearedWaves = model.ClearedWaves ?? new();
            await InsertSnapshotRowsAsync(clearedWaves, (value, index) => new PlayerParams
            {
                Group = ClearedWavesGroup,
                Name = index.ToString(CultureInfo.InvariantCulture),
                Value = value.GetRawText(),
            });
        }

        private async Task InsertDeploymentsAsync(List<GameProgressDeploymentModel> deployments)
        {
            await InsertSnapshotRowsAsync(deployments, (deployment, _) => new Deployment
                {
                    SpotIndex = deployment.SpotIndex,
                    CellX = deployment.Cell?[0],
                    CellY = deployment.Cell?[1],
                    CharacterId = deployment.CharacterId,
                    Wave = deployment.Wave,
                });
        }

        private async Task InsertRoadsAsync(GameProgressBattlefieldModel? battlefield)
        {
            if (battlefield == null) { return; }

            List<GameProgressRoadModel> roads = battlefield.Roads ?? new();
            await InsertSnapshotRowsAsync(roads, (road, _) => new BattlefieldRoad
                {
                    X = road.X,
                    Y = road.Y,
                    Corner = road.Corner,
                    Rotation = road.Rotation,
                });
        }

        private static IEnumerable<(string Group, Dictionary<string, JsonElement> Values)> EnumerateSettingGroups(GameProgressModel model)
        {
            yield return (UnlockedCharacterKindsGroup, model.UnlockedCharacterKinds ?? new());
            yield return (SkillSuffixesGroup, model.SkillSuffixes ?? new());
            yield return (RefineItemCountsGroup, model.RefineItemCounts ?? new());
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
