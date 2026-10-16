using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 天賦樹服務：父表 Talent 每位玩家一列，已取得節點與舊分支節點各自拆表。
    /// </summary>
    public class TalentService(BaseServiceArgument argument, TalentAcquiredService acquiredService,
        TalentLegacyCategoryService legacyService) : _SnapshotService<Talent, TalentModel>(argument)
    {
        /// <summary>
        /// 讀取目前玩家的天賦樹，沒有資料時回傳 null
        /// </summary>
        public virtual async Task<TalentModel?> GetSnapshot()
        {
            Talent? talent = await QueryOwnedFirstAsync();
            if (talent == null) { return null; }

            List<TalentAcquired> acquired = await acquiredService.QueryChildrenAsync<Talent>([talent.Id]);
            List<TalentLegacyCategory> legacy = await legacyService.QueryChildrenAsync<Talent>([talent.Id]);

            return BuildSnapshot(talent, acquired, legacy, await Revisions.Read(nameof(TalentModel)));
        }

        public Task<TalentModel?> GetSnapshotCommand() => ReadSnapshotCommandAsync(GetSnapshotRead);

        public SnapshotRead<TalentModel?> GetSnapshotRead => new("""
            SELECT TOP 1 * FROM Talent WHERE Status != -1 AND CreateUserId = @CreateUserId;
            SELECT c.* FROM TalentAcquired c INNER JOIN Talent p ON p.Id = c.TalentId
            WHERE p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId AND p.Status != -1 ORDER BY c.Sequence;
            SELECT c.* FROM TalentLegacyCategory c INNER JOIN Talent p ON p.Id = c.TalentId
            WHERE p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId AND p.Status != -1 ORDER BY c.Sequence;
            SELECT Value FROM PlayerParams WHERE CreateUserId = @CreateUserId
                AND [Group] = 'server.revisions' AND Name = 'TalentModel' AND Status != -1;
            """, async reader =>
        {
            Talent? talent = await reader.ReadFirstOrDefaultAsync<Talent>();
            var acquired = await reader.ReadAsync<TalentAcquired>();
            var legacy = await reader.ReadAsync<TalentLegacyCategory>();
            string? revision = await reader.ReadSingleOrDefaultAsync<string>();
            return talent == null ? null : BuildSnapshot(talent, acquired, legacy,
                revision == null ? 0 : long.Parse(revision, System.Globalization.CultureInfo.InvariantCulture));
        });

        internal static TalentModel BuildSnapshot(Talent talent, IEnumerable<TalentAcquired> acquired,
            IEnumerable<TalentLegacyCategory> legacy, long revision) =>
            new TalentModel
            {
                Revision = revision,
                Version = talent.Version,
                TalentPoints = talent.TalentPoints,
                TotalSpentPoints = talent.TotalSpentPoints,
                PointsRecalculationVersion = talent.PointsRecalculationVersion,
                AcquiredTalents = ToNodeMap(acquired.Select(a => (a.TreeId, a.NodeId, a.Value))),
                LegacyCategoryTalents = ToNodeMap(legacy.Select(l => (l.TreeId, l.NodeId, l.Value))),
            };

        /// <summary>
        /// 以整份天賦快照覆寫目前玩家的資料：父表 upsert，子表整批刪除後重插。需在交易內呼叫。
        /// </summary>
        public override Task Save(TalentModel model) => base.Save(model);

        private static IEnumerable<(string TreeId, string NodeId, string Value)> EnumerateNodes(
            Dictionary<string, Dictionary<string, JsonElement>> nodes) =>
            nodes.SelectMany(tree => tree.Value.Select(node => (tree.Key, node.Key, node.Value.GetRawText())));

        protected override void ValidateSnapshot(TalentModel model) => ValidateTalent(model);

        protected override async Task SaveSnapshot(TalentModel model)
        {
            (Talent talent, bool isNew) = await UpsertSnapshotParentAsync(row =>
            {
                row.Version = model.Version;
                row.TalentPoints = model.TalentPoints;
                row.TotalSpentPoints = model.TotalSpentPoints;
                row.PointsRecalculationVersion = model.PointsRecalculationVersion;
            });

            if (!isNew)
            {
                await acquiredService.DeleteChildrenAsync<Talent>(talent.Id);
                await legacyService.DeleteChildrenAsync<Talent>(talent.Id);
            }

            await InsertNodesAsync(acquiredService, model.AcquiredTalents ?? new(), (treeId, nodeId, value) => new TalentAcquired
            {
                TalentId = talent.Id,
                TreeId = treeId,
                NodeId = nodeId,
                Value = value,
            });
            await InsertNodesAsync(legacyService, model.LegacyCategoryTalents ?? new(), (treeId, nodeId, value) => new TalentLegacyCategory
            {
                TalentId = talent.Id,
                TreeId = treeId,
                NodeId = nodeId,
                Value = value,
            });
        }

        private async Task InsertNodesAsync<TNode>(_RowService<TNode> service,
            Dictionary<string, Dictionary<string, JsonElement>> treeNodes, Func<string, string, string, TNode> create)
            where TNode : ModelBase, new()
        {
            var nodes = EnumerateNodes(treeNodes);
            await service.InsertOwnedRowsAsync(nodes, (node, _) => create(node.TreeId, node.NodeId, node.Value));
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
        internal static void ValidateTalent(TalentModel model)
        {
            if (model.Version < 0 || model.PointsRecalculationVersion < 0)
            {
                throw new BusinessArgumentException(ApiMessageKeys.TalentVersionInvalid);
            }
            if (model.TalentPoints < 0 || model.TotalSpentPoints < 0)
            {
                throw new BusinessArgumentException(ApiMessageKeys.TalentPointsInvalid);
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
                    throw new BusinessArgumentException(ApiMessageKeys.TalentTreeInvalid);
                }

                foreach ((string nodeId, JsonElement value) in nodes)
                {
                    if (string.IsNullOrWhiteSpace(nodeId) || !isValidValue(value))
                    {
                        throw new BusinessArgumentException(ApiMessageKeys.TalentNodeInvalid);
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
