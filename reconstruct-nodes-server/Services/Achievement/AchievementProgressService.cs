using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 成就進度服務：Achievement 每個群組一列（Total 為進度），AchievementDetail 為群組內項目與領取狀態，
    /// AchievementCollected 為已收集的武器與詞綴。
    /// </summary>
    public class AchievementProgressService(BaseServiceArgument argument, ItemService itemService) : _SnapshotService<Achievement, AchievementProgressModel>(argument)
    {
        // 與用戶端 AchievementItem.REWARD_ID、REWARD_AMOUNT 保持一致
        private const string RewardItemKey = "diamond";
        private const int RewardAmount = 1;

        private const string DetailTable = nameof(AchievementDetail);
        private const string CollectedTable = nameof(AchievementCollected);

        /// <summary>
        /// 讀取目前玩家的成就進度，尚未儲存過時回傳 null
        /// </summary>
        public virtual async Task<AchievementProgressModel?> Get()
        {
            List<Achievement> groups = await QueryGroupsAsync(lockRows: false);
            if (groups.Count == 0) { return null; }

            ILookup<Guid?, AchievementDetail> details = (await QueryChildrenAsync<AchievementDetail>(DetailTable)).ToLookup(d => d.AchievementId);
            ILookup<Guid?, AchievementCollected> collected = (await QueryChildrenAsync<AchievementCollected>(CollectedTable)).ToLookup(c => c.AchievementId);

            return new AchievementProgressModel
            {
                Groups = groups.Select(group => new AchievementGroupModel
                {
                    Name = group.Name,
                    Total = group.Total ?? 0,
                    Items = details[group.Id].Select(d => new AchievementItemModel
                    {
                        Name = d.Name,
                        Target = d.Target,
                        IsReward = d.IsReward,
                        RewardDate = d.RewardDate,
                    }).ToList(),
                    Collected = collected[group.Id].Select(c => c.Name).ToList(),
                }).ToList(),
            };
        }

        /// <summary>
        /// 以整份成就快照覆寫目前玩家的資料。已領取紀錄由伺服器保留，用戶端無法撤銷或重複領取。需在交易內呼叫。
        /// </summary>
        public override Task Save(AchievementProgressModel model) => base.Save(model);

        protected override void ValidateSnapshot(AchievementProgressModel model) => ValidateProgress(model);

        protected override async Task SaveSnapshot(AchievementProgressModel model)
        {
            // 與 ClaimReward 相同的鎖順序（先群組、後項目），避免死結
            await QueryGroupsAsync(lockRows: true);
            Dictionary<string, AchievementDetail> claimed = (await QueryChildrenAsync<AchievementDetail>(DetailTable))
                .Where(d => d.IsReward)
                .ToDictionary(d => d.Name);

            await DeleteOwnedRowsAsync(CollectedTable);
            await DeleteOwnedRowsAsync(DetailTable);
            await DeleteOwnedRowsAsync(_tableName!);

            int groupSequence = 0;
            foreach (AchievementGroupModel group in model.Groups ?? new())
            {
                Achievement row = new()
                {
                    Name = group.Name,
                    Total = group.Total,
                    Sequence = groupSequence++,
                    CreateUserId = _playerId,
                };
                await InsertAsync(row);

                int itemSequence = 0;
                foreach (AchievementItemModel item in group.Items ?? new())
                {
                    claimed.TryGetValue(item.Name, out AchievementDetail? previous);
                    await InsertAsync(new AchievementDetail
                    {
                        AchievementId = row.Id,
                        Name = item.Name,
                        Target = item.Target,
                        IsReward = previous != null,
                        RewardDate = previous?.RewardDate,
                        Sequence = itemSequence++,
                        CreateUserId = _playerId,
                    });
                }

                int collectedSequence = 0;
                foreach (string name in group.Collected ?? new())
                {
                    await InsertAsync(new AchievementCollected
                    {
                        AchievementId = row.Id,
                        Name = name,
                        Sequence = collectedSequence++,
                        CreateUserId = _playerId,
                    });
                }
            }
        }

        /// <summary>
        /// 領取已達成項目的獎勵：標記為已領取並發放獎勵物品。需在交易內呼叫，任一步失敗會整筆回滾。
        /// </summary>
        public virtual async Task ClaimReward(string achievementItemId)
        {
            if (string.IsNullOrWhiteSpace(achievementItemId)) { throw new BusinessArgumentException(ApiMessageKeys.AchievementIdRequired); }

            List<Achievement> groups = await QueryGroupsAsync(lockRows: true);
            if (groups.Count == 0) { throw new BusinessOperationException(ApiMessageKeys.AchievementMissing); }

            string sql = $@"SELECT TOP 1 *
                              FROM {DetailTable} WITH (UPDLOCK, HOLDLOCK)
                             WHERE Name = @Name
                               AND CreateUserId = @CreateUserId";
            AchievementDetail? detail = await QueryFirstAsync<AchievementDetail?>(sql, new { Name = achievementItemId, CreateUserId = _playerId });
            if (detail == null) { throw new BusinessOperationException(ApiMessageKeys.AchievementItemMissing); }
            if (detail.IsReward) { throw new BusinessOperationException(ApiMessageKeys.AchievementClaimed); }

            Achievement? group = groups.FirstOrDefault(g => g.Id == detail.AchievementId);
            if (group == null) { throw new BusinessOperationException(ApiMessageKeys.AchievementGroupMissing); }
            if ((group.Total ?? 0) < detail.Target) { throw new BusinessOperationException(ApiMessageKeys.AchievementIncomplete); }

            detail.IsReward = true;
            detail.RewardDate = DateTime.Now;
            await UpdateAsync(detail);
            await itemService.AdjustAmount(RewardItemKey, RewardAmount);
        }

        private async Task<List<Achievement>> QueryGroupsAsync(bool lockRows)
        {
            string hint = lockRows ? "WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
            string sql = $@"SELECT *
                              FROM {_tableName} {hint}
                             WHERE Status != -1
                               AND CreateUserId = @CreateUserId
                          ORDER BY Sequence";
            return (await QueryAsync<Achievement>(sql, new { CreateUserId = _playerId })).ToList();
        }

        /// <summary>
        /// 只讀取仍有效群組底下的子表資料
        /// </summary>
        private async Task<List<T>> QueryChildrenAsync<T>(string table)
        {
            string sql = $@"SELECT c.*
                              FROM {table} c
                             INNER JOIN {_tableName} a ON a.Id = c.AchievementId
                             WHERE a.Status != -1
                               AND a.CreateUserId = @CreateUserId
                               AND c.CreateUserId = @CreateUserId
                          ORDER BY c.Sequence";
            return (await QueryAsync<T>(sql, new { CreateUserId = _playerId })).ToList();
        }

        /// <summary>
        /// 寫入前檢查名稱與數值，避免空白或重複的成就資料進入資料庫
        /// </summary>
        private static void ValidateProgress(AchievementProgressModel model)
        {
            HashSet<string> groupNames = new();
            HashSet<string> itemNames = new();
            foreach (AchievementGroupModel group in model.Groups ?? new())
            {
                if (string.IsNullOrWhiteSpace(group.Name) || !groupNames.Add(group.Name))
                {
                    throw new BusinessArgumentException(ApiMessageKeys.AchievementGroupInvalid);
                }
                if (!float.IsFinite(group.Total) || group.Total < 0)
                {
                    throw new BusinessArgumentException(ApiMessageKeys.AchievementProgressInvalid);
                }

                foreach (AchievementItemModel item in group.Items ?? new())
                {
                    if (string.IsNullOrWhiteSpace(item.Name) || !itemNames.Add(item.Name))
                    {
                        throw new BusinessArgumentException(ApiMessageKeys.AchievementItemsInvalid);
                    }
                    if (item.Target <= 0)
                    {
                        throw new BusinessArgumentException(ApiMessageKeys.AchievementTargetInvalid);
                    }
                }

                HashSet<string> collected = new();
                foreach (string name in group.Collected ?? new())
                {
                    if (string.IsNullOrWhiteSpace(name) || !collected.Add(name))
                    {
                        throw new BusinessArgumentException(ApiMessageKeys.AchievementCollectionInvalid);
                    }
                }
            }
        }
    }
}
