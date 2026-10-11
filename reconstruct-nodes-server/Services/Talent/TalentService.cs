using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 天賦樹服務：父表 Talent 每位玩家一列，已取得節點與舊分支節點各自拆表。
    /// </summary>
    public class TalentService(BaseServiceArgument argument) : _ModelService<Talent, Talent>(argument)
    {
        private const string AcquiredTable = nameof(TalentAcquired);
        private const string LegacyCategoryTable = nameof(TalentLegacyCategory);

        /// <summary>
        /// 讀取目前玩家的天賦樹，沒有資料時回傳 null
        /// </summary>
        public virtual async Task<TalentModel?> GetTalent()
        {
            string sql = $@"SELECT TOP 1 *
                              FROM {_tableName}
                             WHERE Status != -1
                               AND CreateUserId = @CreateUserId";
            Talent? talent = await QueryFirstAsync<Talent?>(sql, new { CreateUserId = _playerId });
            if (talent == null) { return null; }

            List<TalentAcquired> acquired = await QueryChildrenAsync<TalentAcquired>(AcquiredTable, talent.Id);
            List<TalentLegacyCategory> legacy = await QueryChildrenAsync<TalentLegacyCategory>(LegacyCategoryTable, talent.Id);

            return new TalentModel
            {
                Version = talent.Version,
                TalentPoints = talent.TalentPoints,
                TotalSpentPoints = talent.TotalSpentPoints,
                PointsRecalculationVersion = talent.PointsRecalculationVersion,
                AcquiredTalents = ToNodeMap(acquired.Select(a => (a.TreeId, a.NodeId, a.Value))),
                LegacyCategoryTalents = ToNodeMap(legacy.Select(l => (l.TreeId, l.NodeId, l.Value))),
            };
        }

        /// <summary>
        /// 以整份天賦快照覆寫目前玩家的資料：父表 upsert，子表整批刪除後重插。需在交易內呼叫。
        /// </summary>
        public virtual async Task SaveTalent(TalentModel model)
        {
            ValidateTalent(model);

            // UPDLOCK + HOLDLOCK 鎖住既有列與不存在時的鍵區間，避免並行儲存重複建立父列
            string lockSql = $@"SELECT TOP 1 *
                                  FROM {_tableName} WITH (UPDLOCK, HOLDLOCK)
                                 WHERE Status != -1
                                   AND CreateUserId = @CreateUserId";
            Talent? existing = await QueryFirstAsync<Talent?>(lockSql, new { CreateUserId = _playerId });

            Talent talent = existing ?? new Talent { CreateUserId = _playerId };
            talent.Version = model.Version;
            talent.TalentPoints = model.TalentPoints;
            talent.TotalSpentPoints = model.TotalSpentPoints;
            talent.PointsRecalculationVersion = model.PointsRecalculationVersion;

            if (existing == null)
            {
                await InsertAsync(talent);
            }
            else
            {
                await UpdateAsync(talent);
                await DeleteOwnedRowsAsync(AcquiredTable, "TalentId", talent.Id);
                await DeleteOwnedRowsAsync(LegacyCategoryTable, "TalentId", talent.Id);
            }

            await InsertNodesAsync(model.AcquiredTalents ?? new(), (treeId, nodeId, value) => new TalentAcquired
            {
                TalentId = talent.Id,
                TreeId = treeId,
                NodeId = nodeId,
                Value = value,
            });
            await InsertNodesAsync(model.LegacyCategoryTalents ?? new(), (treeId, nodeId, value) => new TalentLegacyCategory
            {
                TalentId = talent.Id,
                TreeId = treeId,
                NodeId = nodeId,
                Value = value,
            });
        }

        private async Task<List<T>> QueryChildrenAsync<T>(string table, Guid talentId)
        {
            string sql = $@"SELECT *
                              FROM {table}
                             WHERE TalentId = @TalentId
                               AND CreateUserId = @CreateUserId
                          ORDER BY Sequence";
            IEnumerable<T> rows = await QueryAsync<T>(sql, new { TalentId = talentId, CreateUserId = _playerId });
            return rows.ToList();
        }

        private async Task InsertNodesAsync<TNode>(Dictionary<string, Dictionary<string, JsonElement>> treeNodes, Func<string, string, string, TNode> create)
            where TNode : ModelBase
        {
            int sequence = 0;
            foreach ((string treeId, Dictionary<string, JsonElement> nodes) in treeNodes)
            {
                foreach ((string nodeId, JsonElement value) in nodes)
                {
                    TNode row = create(treeId, nodeId, value.GetRawText());
                    row.Sequence = sequence++;
                    row.CreateUserId = _playerId;
                    await InsertAsync(row);
                }
            }
        }

        private static Dictionary<string, Dictionary<string, JsonElement>> ToNodeMap(IEnumerable<(string TreeId, string NodeId, string Value)> rows)
        {
            Dictionary<string, Dictionary<string, JsonElement>> treeNodes = new();
            foreach ((string treeId, string nodeId, string value) in rows)
            {
                if (!treeNodes.TryGetValue(treeId, out Dictionary<string, JsonElement>? nodes))
                {
                    nodes = new();
                    treeNodes[treeId] = nodes;
                }
                nodes[nodeId] = JsonSerializer.Deserialize<JsonElement>(value);
            }
            return treeNodes;
        }

        /// <summary>
        /// 寫入前檢查數值與節點格式，避免格式錯誤造成資料不完整
        /// </summary>
        private static void ValidateTalent(TalentModel model)
        {
            if (model.Version < 0 || model.PointsRecalculationVersion < 0)
            {
                throw new ArgumentException("天賦存檔版本欄位不可為負數");
            }
            if (model.TalentPoints < 0 || model.TotalSpentPoints < 0)
            {
                throw new ArgumentException("天賦點數不可為負數");
            }

            ValidateNodes(model.AcquiredTalents, IsNonNegativeInteger, "已取得天賦");
            ValidateNodes(model.LegacyCategoryTalents, IsBoolean, "舊天賦分支");
        }

        private static void ValidateNodes(Dictionary<string, Dictionary<string, JsonElement>>? treeNodes, Func<JsonElement, bool> isValidValue, string label)
        {
            foreach ((string treeId, Dictionary<string, JsonElement>? nodes) in treeNodes ?? new())
            {
                if (string.IsNullOrWhiteSpace(treeId) || nodes == null)
                {
                    throw new ArgumentException($"{label}的樹資料不完整");
                }

                foreach ((string nodeId, JsonElement value) in nodes)
                {
                    if (string.IsNullOrWhiteSpace(nodeId) || !isValidValue(value))
                    {
                        throw new ArgumentException($"{label} {treeId}/{nodeId} 的資料格式錯誤");
                    }
                }
            }
        }

        private static bool IsNonNegativeInteger(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int level) && level >= 0;
        }

        private static bool IsBoolean(JsonElement value)
        {
            return value.ValueKind is JsonValueKind.True or JsonValueKind.False;
        }
    }
}
