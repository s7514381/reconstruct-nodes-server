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
    public class AchievementProgressService(BaseServiceArgument argument, ItemService itemService) : _SnapshotService<Achievement, AchievementProgressModel>(argument)
    {
        // 與用戶端 AchievementItem.REWARD_ID、REWARD_AMOUNT 保持一致
        private const string RewardItemKey = "diamond";
        private const int RewardAmount = 1;

        private const string DetailTable = nameof(AchievementDetail);

        /// <summary>
        /// 讀取目前玩家的成就進度，尚未儲存過時回傳 null
        /// </summary>
        public virtual async Task<AchievementProgressModel?> Get()
        {
            await LockPlayerAsync();
            List<Achievement> groups = await QueryGroupsAsync(lockRows: false);
            if (groups.Count == 0) { return null; }

            ILookup<Guid?, AchievementDetail> details = (await QuerySnapshotChildrenAsync<AchievementDetail, Achievement>()).ToLookup(d => d.AchievementId);
            ILookup<Guid?, AchievementCollected> collected = (await QuerySnapshotChildrenAsync<AchievementCollected, Achievement>()).ToLookup(c => c.AchievementId);

            return new AchievementProgressModel
            {
                Revision = await Revisions.Read(nameof(AchievementProgressModel)),
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
            Dictionary<string, AchievementDetail> claimed = (await QuerySnapshotChildrenAsync<AchievementDetail, Achievement>())
                .Where(d => d.IsReward)
                .ToDictionary(d => d.Name);

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

            // One command retains the caller's transaction and avoids a network round trip per row.
            await ExecuteAsync("""
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
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, AchievementId, Name, Target,
                       IsReward, RewardDate, GainCoins, Type, Version
                FROM OPENJSON(@Details) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    AchievementId uniqueidentifier, Name nvarchar(max), Target int, IsReward bit,
                    RewardDate datetime2, GainCoins real, Type int, Version nvarchar(max));

                INSERT INTO AchievementCollected (Id, Sequence, CreateDate, CreateUserId, Status, AchievementId, Name)
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, AchievementId, Name
                FROM OPENJSON(@Collected) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    AchievementId uniqueidentifier, Name nvarchar(max));
                """, new
            {
                CreateUserId = _playerId,
                Groups = JsonSerializer.Serialize(groups),
                Details = JsonSerializer.Serialize(details),
                Collected = JsonSerializer.Serialize(collected),
            });
        }

        /// <summary>
        /// 領取已達成項目的獎勵：標記為已領取並發放獎勵物品。需在交易內呼叫，任一步失敗會整筆回滾。
        /// </summary>
        public virtual async Task ClaimReward(string achievementItemId)
        {
            if (string.IsNullOrWhiteSpace(achievementItemId)) { throw new BusinessArgumentException(ApiMessageKeys.AchievementIdRequired); }
            await LockPlayerAsync();

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
            await Revisions.Advance(nameof(AchievementProgressModel));
        }

        private Task<List<Achievement>> QueryGroupsAsync(bool lockRows) =>
            QuerySnapshotRowsAsync<Achievement>(orderBy: row => row.Sequence, lockRows: lockRows);

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
