using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 波次完成：沿用 BackgroundSync 的「單次批次讀取 + 單次批次寫入並 COMMIT」流程，
/// 只更新變動的 PlayerParams 列，取代逐筆查詢與整份進度覆寫（原本約 25 次 SQL 往返）。
/// </summary>
public class WaveCompleteCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private const string Operation = "wave.complete";
    private const string ProgressGroup = "game_progress";
    private const string ClearedWavesGroup = GameProgressParamGroups.SettingPrefix + "cleared_waves";
    private const string RevisionName = nameof(GameProgressModel);

    private sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Parameters { get; set; } = [];
        public List<Deployment> Deployments { get; set; } = [];
        public List<BattlefieldRoad> Roads { get; set; } = [];
        public List<int> KillIndexes { get; set; } = [];
    }

    public async Task<CommandReceipt<WaveCompleteResult>> Execute(WaveCompleteRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(new SnapshotRead<object?>("""
            DECLARE @Owner uniqueidentifier;
            SELECT @Owner = Id FROM Player WHERE Id = @CreateUserId;
            IF @Owner IS NULL THROW 51002, 'Wave owner is unavailable', 1;
            """, _ => Task.FromResult<object?>(null)));
        reads.Add(new SnapshotRead<object?>("""
            SELECT * FROM EconomyRequest WHERE CreateUserId = @CreateUserId
                AND RequestId IN (@RequestId, @SessionId);
            """, async reader => { state.Receipts = (await reader.ReadAsync<EconomyRequest>()).ToList(); return null; }));
        reads.Add(new SnapshotRead<object?>("""
            SELECT * FROM PlayerParams WHERE CreateUserId = @CreateUserId AND Status != -1
                AND (([Group] = 'server.revisions' AND Name = 'GameProgressModel')
                    OR [Group] = 'game_progress' OR [Group] LIKE 'game[_]progress.%'
                    OR ([Group] = 'economy' AND Name = 'active_wave'))
                ORDER BY [Group], Sequence;
            SELECT * FROM Deployment WHERE Status != -1 AND CreateUserId = @CreateUserId ORDER BY Sequence;
            SELECT * FROM BattlefieldRoad WHERE Status != -1 AND CreateUserId = @CreateUserId ORDER BY Sequence;
            """, async reader =>
        {
            state.Parameters = (await reader.ReadAsync<PlayerParams>()).ToList();
            state.Deployments = (await reader.ReadAsync<Deployment>()).ToList();
            state.Roads = (await reader.ReadAsync<BattlefieldRoad>()).ToList();
            return null;
        }));
        // Kill payloads are {"SessionId":"<guid>","EnemyIndex":n}; the prefix avoids parsing every kill ever made.
        reads.Add(new SnapshotRead<object?>("""
            SELECT DISTINCT TRY_CONVERT(int, JSON_VALUE(Payload, '$.EnemyIndex'))
            FROM EconomyRequest WHERE CreateUserId = @CreateUserId AND Operation = 'enemy_kill'
                AND Payload LIKE @KillPrefix;
            """, async reader => { state.KillIndexes = (await reader.ReadAsync<int>()).ToList(); return null; }));

        string payload = RevisionCommand.CanonicalPayload(request);
        string json = await _repository.ExecuteAtomicJsonAsync(reads.Sql, new
        {
            CreateUserId = _playerId, request.RequestId, request.SessionId,
            KillPrefix = $"{{\"SessionId\":\"{request.SessionId:D}\",%",
        }, async reader =>
        {
            await reads.ReadAsync(reader);
            return Prepare(request, payload, state);
        });
        return JsonSerializer.Deserialize<CommandReceipt<WaveCompleteResult>>(json)
            ?? throw new InvalidOperationException("Invalid command receipt.");
    }

    private IReadOnlyList<SqlBatchCommand> Prepare(WaveCompleteRequest request, string payload, State state)
    {
        var prior = state.Receipts.FirstOrDefault(r => r.RequestId == request.RequestId);
        if (prior != null)
        {
            if (prior.Operation != Operation || prior.Payload != payload)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
            return [new SqlBatchCommand("SELECT @Receipt;", new { Receipt = prior.Result })];
        }
        if (request.SessionId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyKillInvalid); }
        var receipt = state.Receipts.FirstOrDefault(r => r.RequestId == request.SessionId && r.Operation == "wave_start")
            ?? throw new BusinessArgumentException(ApiMessageKeys.EconomySessionNotOwned);

        var newRows = new List<PlayerParams>();
        var updatedRows = new Dictionary<Guid, PlayerParams>();
        PlayerParams? Find(string group, string name) =>
            state.Parameters.SingleOrDefault(p => p.Group == group && p.Name == name);
        void Set(string group, string name, string value, int sequence = 0)
        {
            PlayerParams? row = Find(group, name);
            if (row == null)
            {
                row = new PlayerParams { Group = group, Name = name, Value = value, Sequence = sequence };
                state.Parameters.Add(row);
                newRows.Add(row);
            }
            else if (row.Value != value)
            {
                row.Value = value;
                if (!newRows.Contains(row)) { updatedRows[row.Id] = row; }
            }
        }
        GameProgressModel BuildProgress(long revision)
        {
            var progressRows = state.Parameters
                .Where(p => p.Group == ProgressGroup || p.Group.StartsWith("game_progress.", StringComparison.Ordinal))
                .OrderBy(p => p.Group, StringComparer.Ordinal).ThenBy(p => p.Sequence).ToList();
            PlayerParams? gold = Find(ProgressGroup, "gold");
            return GameProgressCommand.BuildSnapshot(progressRows, state.Deployments, state.Roads, revision,
                gold == null ? GoldCommand.StartingGold : GoldCommand.ReadBalance(gold))
                ?? throw new BusinessOperationException(ApiMessageKeys.ProgressMissing);
        }
        long Revision() => Find(RevisionCommand.RevisionGroup, RevisionName) is { } row
            ? long.Parse(row.Value, CultureInfo.InvariantCulture) : 0;

        GameProgressModel model = BuildProgress(Revision());
        var session = ProgressionRules.ReadSession(receipt, model.InfiniteLevel);
        if (!Guid.TryParse(Find("economy", "active_wave")?.Value, out var activeId) || activeId != request.SessionId)
        { throw new BusinessArgumentException(ApiMessageKeys.EconomySessionNotOwned); }
        var indexes = state.KillIndexes.ToHashSet();
        if (session.EnemyCount < 1 || indexes.Count != session.EnemyCount ||
            indexes.Any(i => i < 0 || i >= session.EnemyCount))
        { throw new BusinessOperationException(ApiMessageKeys.EconomyEnemyInvalid); }
        if (model.Wave != session.Wave || model.Difficulty != session.Difficulty)
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyWaveInvalid); }
        var next = ProgressionRules.NextWave(model.Wave, model.Difficulty, model.InfiniteLevel);

        long revision = Revision();
        if (!model.WaveCompleted)
        {
            int cleared = Math.Max(model.ClearedWaves.Count, 4);
            for (int i = 0; i < cleared; i++)
            {
                string value = i < model.ClearedWaves.Count ? model.ClearedWaves[i].GetRawText() : "0";
                if (i == model.Difficulty)
                { value = Math.Max(i < model.ClearedWaves.Count ? model.ClearedWaves[i].GetInt32() : 0, model.Wave)
                    .ToString(CultureInfo.InvariantCulture); }
                Set(ClearedWavesGroup, i.ToString(CultureInfo.InvariantCulture), value, i);
            }
            Set(ProgressGroup, "max_wave_reached",
                Math.Max(model.MaxWaveReached, model.Wave).ToString(CultureInfo.InvariantCulture));
            Set(ProgressGroup, "wave_completed", bool.TrueString);
            revision = checked(revision + 1);
            Set(RevisionCommand.RevisionGroup, RevisionName, revision.ToString(CultureInfo.InvariantCulture));
        }

        var result = new CommandReceipt<WaveCompleteResult>
        {
            RequestId = request.RequestId, Revision = revision,
            Snapshot = new WaveCompleteResult
            {
                Progress = BuildProgress(revision), NextWave = next.Wave,
                NextDifficulty = next.Difficulty, NextInfiniteLevel = next.InfiniteLevel,
            },
        };
        string resultJson = JsonSerializer.Serialize(result);
        var writes = new SqlWriteBatch(_playerId);
        writes.Insert(newRows);
        writes.Update(updatedRows.Values);
        writes.Insert([new EconomyRequest { RequestId = request.RequestId, Operation = Operation,
            Payload = payload, Result = resultJson }]);
        writes.Add("SELECT @Result;", new { Result = resultJson });
        return writes.Commands;
    }
}
