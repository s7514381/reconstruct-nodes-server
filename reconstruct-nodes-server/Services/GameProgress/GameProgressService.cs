using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 遊戲進度服務：無父表。純量與設定存於 PlayerParams（Group 以 game_progress 開頭）；
    /// 部署、技能、戰場道路與角色裝備依 CreateUserId 拆表。裝備只存 EntryKey，詳情以 Weapon 為準。
    /// </summary>
    public class GameProgressService(BaseServiceArgument argument) : _SnapshotService<PlayerParams, GameProgressModel>(argument)
    {
        private const string DeploymentTable = nameof(Deployment);
        private const string SkillTable = nameof(DeploymentSkill);
        private const string NodeTable = nameof(DeploymentSkillNode);
        private const string RoadTable = nameof(BattlefieldRoad);
        private const string CharacterWeaponTable = nameof(CharacterWeapon);

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

            List<Deployment> deploymentRows = await QueryOwnedAsync<Deployment>(DeploymentTable);
            List<DeploymentSkill> skillRows = await QueryOwnedAsync<DeploymentSkill>(SkillTable);
            List<DeploymentSkillNode> nodeRows = await QueryOwnedAsync<DeploymentSkillNode>(NodeTable);
            List<BattlefieldRoad> roadRows = await QueryOwnedAsync<BattlefieldRoad>(RoadTable);
            List<CharacterWeapon> characterWeaponRows = await QueryOwnedAsync<CharacterWeapon>(CharacterWeaponTable);

            Dictionary<string, WeaponInstanceModel> liveWeapons = (await _lazy.WeaponService.Value.GetInstanceList())
                .ToDictionary(weapon => weapon.Id);

            ILookup<Guid?, DeploymentSkill> skillsByDeployment = skillRows.ToLookup(skill => skill.DeploymentId);
            ILookup<Guid?, DeploymentSkillNode> nodesBySkill = nodeRows.ToLookup(node => node.DeploymentSkillId);

            List<GameProgressDeploymentModel> deploymentLog = deploymentRows.Select(row =>
            {
                GameProgressDeploymentModel deployment = new()
                {
                    SpotIndex = row.SpotIndex,
                    CharacterName = row.CharacterName,
                    Wave = row.Wave,
                    Kind = row.Kind,
                    Cell = row.CellX.HasValue && row.CellY.HasValue ? new List<int> { row.CellX.Value, row.CellY.Value } : null,
                    SkillLoadout = skillsByDeployment[row.Id].Select(skill => new GameProgressSkillLoadoutModel
                    {
                        Kind = skill.Kind,
                        AuxiliaryNodes = nodesBySkill[skill.Id].Select(ToNodeModel).ToList(),
                    }).ToList(),
                    WeaponQualityVersion = row.WeaponQualityVersion,
                };
                ApplyEquipment(deployment, row.WeaponEntryKey, liveWeapons);
                return deployment;
            }).ToList();

            Dictionary<string, GameProgressEquipmentModel> characterWeapons = new();
            foreach (CharacterWeapon characterWeapon in characterWeaponRows)
            {
                GameProgressEquipmentModel equipment = new();
                ApplyEquipment(equipment, characterWeapon.EntryKey, liveWeapons);
                characterWeapons[characterWeapon.CharacterKind.ToString(CultureInfo.InvariantCulture)] = equipment;
            }

            return new GameProgressModel
            {
                Version = ReadInt(progressValues, ProgressKey.Version),
                DeploymentLog = deploymentLog,
                Wave = ReadInt(progressValues, ProgressKey.Wave),
                Gold = ReadLong(progressValues, ProgressKey.Gold),
                MaxWaveReached = ReadInt(progressValues, ProgressKey.MaxWaveReached),
                UnlockedBulletKinds = ReadSettingGroup(settingRows, UnlockedBulletKindsGroup),
                UnlockedAuxiliaryKinds = ReadSettingGroup(settingRows, UnlockedAuxiliaryKindsGroup),
                UnlockedCharacterKinds = ReadSettingGroup(settingRows, UnlockedCharacterKindsGroup),
                SkillSuffixes = ReadSettingGroup(settingRows, SkillSuffixesGroup),
                RefineItemCounts = ReadSettingGroup(settingRows, RefineItemCountsGroup),
                Progression = ReadSettingGroup(settingRows, ProgressionGroup),
                CharacterWeapons = characterWeapons,
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
            await DeleteProgressRowsAsync();

            await InsertScalarsAsync(model);
            await InsertSettingsAsync(model);
            await InsertDeploymentsAsync(model.DeploymentLog ?? new());
            await InsertCharacterWeaponsAsync(model.CharacterWeapons ?? new());
            await InsertRoadsAsync(model.Battlefield);
        }

        /// <summary>
        /// 讀取此玩家在指定表的全部列，依 Sequence 排序
        /// </summary>
        private async Task<List<T>> QueryOwnedAsync<T>(string table)
        {
            string sql = $@"SELECT *
                              FROM {table}
                             WHERE Status != -1
                               AND CreateUserId = @CreateUserId
                          ORDER BY Sequence";
            return (await QueryAsync<T>(sql, new { CreateUserId = _playerId })).ToList();
        }

        /// <summary>
        /// 寫入前檢查結構，避免格式錯誤造成資料不完整
        /// </summary>
        private static void ValidateProgress(GameProgressModel model)
        {
            if (model.Battlefield != null)
            {
                if (model.Battlefield.Castle is not { Count: 2 }) { throw new ArgumentException("戰場城堡座標必須為 [x, y]"); }
                if ((model.Battlefield.Roads ?? new()).Any(road => road == null)) { throw new ArgumentException("戰場道路格不可為空"); }
            }

            foreach (GameProgressDeploymentModel? deployment in model.DeploymentLog ?? new())
            {
                if (deployment == null) { throw new ArgumentException("部署紀錄不可為空"); }
                if (deployment.Cell != null && deployment.Cell.Count != 2) { throw new ArgumentException("部署座標必須為 [x, y]"); }

                foreach (GameProgressSkillLoadoutModel? loadout in deployment.SkillLoadout ?? new())
                {
                    if (loadout == null) { throw new ArgumentException("技能配置不可為空"); }

                    foreach (GameProgressSkillNodeModel? node in loadout.AuxiliaryNodes ?? new())
                    {
                        ValidateNode(node);
                    }
                }
            }

            HashSet<int> kinds = new();
            foreach (KeyValuePair<string, GameProgressEquipmentModel> entry in model.CharacterWeapons ?? new())
            {
                if (!int.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind))
                {
                    throw new ArgumentException($"角色武器的鍵必須為整數：{entry.Key}");
                }
                // 例如 "3" 與 "03" 解析後相同，視為重複
                if (!kinds.Add(kind)) { throw new ArgumentException($"角色武器的種類重複：{entry.Key}"); }
                if (entry.Value == null) { throw new ArgumentException($"角色武器資料不可為空：{entry.Key}"); }
            }
        }

        private static void ValidateNode(GameProgressSkillNodeModel? node)
        {
            if (node == null) { throw new ArgumentException("技能節點不可為空"); }

            if (node.Type == GameProgressSkillNodeModel.AuxiliaryType)
            {
                if (string.IsNullOrEmpty(node.LootId)) { throw new ArgumentException("輔助技能節點缺少 loot_id"); }
                return;
            }

            if (node.Type == GameProgressSkillNodeModel.ActiveType)
            {
                if (node.Kind == null) { throw new ArgumentException("主動技能節點缺少 kind"); }
                return;
            }

            throw new ArgumentException($"未知的技能節點類型：{node.Type}");
        }

        /// <summary>
        /// 依子表由深到淺刪除此玩家的進度列；子列都以 CreateUserId 歸屬，不再依賴父列
        /// </summary>
        private async Task DeleteProgressRowsAsync()
        {
            foreach (string table in new[] { NodeTable, SkillTable, DeploymentTable, RoadTable, CharacterWeaponTable })
            {
                await DeleteOwnedRowsAsync(table);
            }

            await ExecuteAsync($"DELETE FROM {_tableName} WHERE CreateUserId = @CreateUserId AND {ProgressRowFilter}", new { CreateUserId = _playerId });
        }

        private async Task InsertScalarsAsync(GameProgressModel model)
        {
            await InsertScalarAsync(ProgressKey.Version, model.Version);
            await InsertScalarAsync(ProgressKey.Wave, model.Wave);
            await InsertScalarAsync(ProgressKey.Gold, model.Gold);
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
            await InsertAsync(new PlayerParams
            {
                Group = ProgressGroup,
                Name = name,
                Value = Convert.ToString(value, CultureInfo.InvariantCulture),
                CreateUserId = _playerId,
            });
        }

        private async Task InsertSettingsAsync(GameProgressModel model)
        {
            foreach ((string group, Dictionary<string, JsonElement> values) in EnumerateSettingGroups(model))
            {
                int position = 0;
                foreach (KeyValuePair<string, JsonElement> entry in values)
                {
                    await InsertSettingAsync(group, entry.Key, entry.Value, position);
                    position++;
                }
            }

            List<JsonElement> clearedWaves = model.ClearedWaves ?? new();
            for (int index = 0; index < clearedWaves.Count; index++)
            {
                await InsertSettingAsync(ClearedWavesGroup, index.ToString(CultureInfo.InvariantCulture), clearedWaves[index], index);
            }
        }

        private async Task InsertSettingAsync(string group, string name, JsonElement value, int sequence)
        {
            await InsertAsync(new PlayerParams
            {
                Group = group,
                Name = name,
                Value = value.GetRawText(),
                Sequence = sequence,
                CreateUserId = _playerId,
            });
        }

        private async Task InsertDeploymentsAsync(List<GameProgressDeploymentModel> deployments)
        {
            for (int index = 0; index < deployments.Count; index++)
            {
                GameProgressDeploymentModel deployment = deployments[index];
                Deployment row = new()
                {
                    SpotIndex = deployment.SpotIndex,
                    CellX = deployment.Cell?[0],
                    CellY = deployment.Cell?[1],
                    CharacterName = deployment.CharacterName,
                    Wave = deployment.Wave,
                    Kind = deployment.Kind,
                    WeaponEntryKey = NullIfEmpty(deployment.WeaponEntryId),
                    WeaponQualityVersion = deployment.WeaponQualityVersion,
                    Sequence = index,
                    CreateUserId = _playerId,
                };
                await InsertAsync(row);

                await InsertSkillsAsync(row.Id, deployment.SkillLoadout ?? new());
            }
        }

        private async Task InsertSkillsAsync(Guid deploymentId, List<GameProgressSkillLoadoutModel> loadouts)
        {
            for (int index = 0; index < loadouts.Count; index++)
            {
                GameProgressSkillLoadoutModel loadout = loadouts[index];
                DeploymentSkill skill = new()
                {
                    DeploymentId = deploymentId,
                    Kind = loadout.Kind,
                    Sequence = index,
                    CreateUserId = _playerId,
                };
                await InsertAsync(skill);

                await InsertNodesAsync(skill.Id, loadout.AuxiliaryNodes ?? new());
            }
        }

        private async Task InsertNodesAsync(Guid skillId, List<GameProgressSkillNodeModel> nodes)
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                GameProgressSkillNodeModel node = nodes[index];
                await InsertAsync(new DeploymentSkillNode
                {
                    DeploymentSkillId = skillId,
                    NodeType = node.Type,
                    // 主動節點可能沒有 loot_id，空字串存為 NULL
                    LootId = string.IsNullOrEmpty(node.LootId) ? null : node.LootId,
                    Kind = node.Kind,
                    Sequence = index,
                    CreateUserId = _playerId,
                });
            }
        }

        private async Task InsertCharacterWeaponsAsync(Dictionary<string, GameProgressEquipmentModel> characterWeapons)
        {
            foreach (KeyValuePair<string, GameProgressEquipmentModel> entry in characterWeapons)
            {
                await InsertAsync(new CharacterWeapon
                {
                    CharacterKind = int.Parse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture),
                    EntryKey = NullIfEmpty(entry.Value.WeaponEntryId),
                    CreateUserId = _playerId,
                });
            }
        }

        private async Task InsertRoadsAsync(GameProgressBattlefieldModel? battlefield)
        {
            if (battlefield == null) { return; }

            List<GameProgressRoadModel> roads = battlefield.Roads ?? new();
            for (int index = 0; index < roads.Count; index++)
            {
                GameProgressRoadModel road = roads[index];
                await InsertAsync(new BattlefieldRoad
                {
                    X = road.X,
                    Y = road.Y,
                    Corner = road.Corner,
                    Rotation = road.Rotation,
                    Sequence = index,
                    CreateUserId = _playerId,
                });
            }
        }

        private static IEnumerable<(string Group, Dictionary<string, JsonElement> Values)> EnumerateSettingGroups(GameProgressModel model)
        {
            yield return (UnlockedBulletKindsGroup, model.UnlockedBulletKinds ?? new());
            yield return (UnlockedAuxiliaryKindsGroup, model.UnlockedAuxiliaryKinds ?? new());
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

        private static GameProgressSkillNodeModel ToNodeModel(DeploymentSkillNode node) => new()
        {
            Type = node.NodeType,
            LootId = node.LootId,
            Kind = node.Kind,
        };

        /// <summary>
        /// 只存 EntryKey 的裝備，依 Weapon 列補上詳情；找不到有效武器列時清空槽位，不回傳懸空的 EntryKey
        /// </summary>
        private static void ApplyEquipment(GameProgressEquipmentModel target, string? entryKey, IReadOnlyDictionary<string, WeaponInstanceModel> liveWeapons)
        {
            if (string.IsNullOrEmpty(entryKey)) { return; }
            if (!liveWeapons.TryGetValue(entryKey, out WeaponInstanceModel? weapon)) { return; }

            target.WeaponEntryId = entryKey;
            target.WeaponId = weapon.WeaponId;
            target.WeaponRarity = weapon.Rarity;
            target.WeaponAffixes = weapon.Affixes;
            target.WeaponUnique = weapon.Unique;
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
