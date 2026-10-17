using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

public partial class BackgroundSyncCommand
{
    // 每個領域各自一段讀取，只有請求用得到的領域才會加入批次；所有片段共用 @CreateUserId。
    private SnapshotReadBatch BuildReads(BackgroundSyncRequest request, BatchState state, out object parameters)
    {
        bool kills = request.Kills.Count > 0, waves = request.WaveStart != null;
        var batch = new SnapshotReadBatch();
        batch.Add(OwnerRead);
        batch.Add(ReceiptsRead(state));
        batch.Add(ParametersRead(state, includeProgress: kills || waves, includeActiveWave: kills || waves));
        if (state.TalentLoaded) { AddTalentReads(batch, state); }
        if (kills) { batch.Add(GoldTalentsRead(state)); }

        Guid[] ids = (request.WaveStart == null ? Array.Empty<Guid>() : new[] { request.WaveStart.RequestId })
            .Concat(request.Kills.Select(k => k.SessionId))
            .Concat(request.WeaponAcquisitions.Select(w => w.RequestId))
            .Append(request.RequestId).Distinct().ToArray();
        parameters = new
        {
            CreateUserId = _playerId,
            Ids = JsonSerializer.Serialize(ids),
            Scopes = JsonSerializer.Serialize(request.WeaponAcquisitions
                .Where(w => w.Weapon != null && !string.IsNullOrWhiteSpace(w.Weapon.Id))
                .Select(w => RevisionCommand.ScopeKey(nameof(WeaponInstanceModel), w.Weapon.Id))),
        };
        return batch;
    }

    private static readonly SnapshotRead<object?> OwnerRead = OwnedSql.OwnerRead("Background");

    private static SnapshotRead<List<EconomyRequest>> ReceiptsRead(BatchState state) => OwnedSql.Rows<EconomyRequest>(
        "RequestId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@Ids))", rows => state.Receipts = rows, activeOnly: false);

    private static SnapshotRead<List<PlayerParams>> ParametersRead(BatchState state, bool includeProgress,
        bool includeActiveWave) => OwnedSql.Rows<PlayerParams>($"""
        (([Group] = 'server.revisions' AND (Name IN
            ('GameProgressModel', 'TalentModel', 'AchievementProgressModel', 'WeaponInstanceModel', 'BackgroundSyncResponse')
            OR Name IN (SELECT value FROM OPENJSON(@Scopes))))
            {(includeProgress ? "OR ([Group] = 'game_progress' OR [Group] LIKE 'game[_]progress.%')" : "")}
            OR ([Group] = 'game_progress' AND Name = 'gold')
            OR ([Group] = '{KillLedger.Group}' AND Name IN ({(includeActiveWave
                ? $"'active_wave', '{KillLedger.Name}', '{SyncReceiptRing.Name}'" : $"'{SyncReceiptRing.Name}'")})))
        """, rows => state.Parameters = rows, orderBy: "[Group], Sequence");

    private static void AddTalentReads(SnapshotReadBatch batch, BatchState state)
    {
        batch.Add(new SnapshotRead<Talent?>(OwnedSql.Select<Talent>(top: 1), async reader => state.Talent = await reader.ReadSingleOrDefaultAsync<Talent>()));
        batch.Add(OwnedSql.ChildrenRows<TalentAcquired, Talent>(rows => state.Acquired = rows));
        batch.Add(OwnedSql.ChildrenRows<TalentLegacyCategory, Talent>(rows => state.Legacy = rows));
    }

    private static SnapshotRead<List<TalentAcquired>> GoldTalentsRead(BatchState state) => OwnedSql.Rows<TalentAcquired>(
        GoldCommand.GoldTalentsCondition, rows => state.GoldTalents = rows);
}
