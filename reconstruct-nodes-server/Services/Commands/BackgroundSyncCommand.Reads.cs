using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

public partial class BackgroundSyncCommand
{
    // 每個領域各自一段讀取，只有請求用得到的領域才會加入批次；所有片段共用 @CreateUserId。
    private SnapshotReadBatch BuildReads(BackgroundSyncRequest request, BatchState state, out object parameters)
    {
        bool kills = request.Kills.Count > 0, waves = request.WaveStarts.Count > 0;
        var batch = new SnapshotReadBatch();
        batch.Add(OwnerRead);
        batch.Add(ReceiptsRead(state));
        batch.Add(ParametersRead(state, includeProgress: kills || waves, includeActiveWave: waves));
        if (request.Talent != null || kills) { AddTalentReads(batch, state); }
        if (kills) { batch.Add(GoldTalentsRead(state)); }
        if (request.WeaponAcquisitions.Count > 0) { AddWeaponReads(batch, state); }
        if (kills) { batch.Add(ClaimedKillsRead(state)); }

        Guid[] ids = request.WaveStarts.Select(w => w.RequestId)
            .Concat(request.Kills.SelectMany(k => new[] { k.RequestId, k.SessionId }))
            .Concat(request.WeaponAcquisitions.Select(w => w.RequestId))
            .Append(request.RequestId).Distinct().ToArray();
        parameters = new
        {
            CreateUserId = _playerId,
            Ids = JsonSerializer.Serialize(ids),
            KillPayloads = JsonSerializer.Serialize(request.Kills
                .Select(k => JsonSerializer.Serialize(new { k.SessionId, k.EnemyIndex }))),
            Scopes = JsonSerializer.Serialize(request.WeaponAcquisitions
                .Where(w => w.Weapon != null && !string.IsNullOrWhiteSpace(w.Weapon.Id))
                .Select(w => RevisionCommand.ScopeKey(nameof(WeaponInstanceModel), w.Weapon.Id))),
        };
        return batch;
    }

    private static SnapshotRead<List<T>> Rows<T>(string sql, Action<List<T>> assign) => new(sql, async reader =>
    {
        var rows = (await reader.ReadAsync<T>()).ToList();
        assign(rows);
        return rows;
    });

    private static readonly SnapshotRead<object?> OwnerRead = new("""
        DECLARE @Owner uniqueidentifier;
        SELECT @Owner = Id FROM Player WHERE Id = @CreateUserId;
        IF @Owner IS NULL THROW 51002, 'Background owner is unavailable', 1;
        """, _ => Task.FromResult<object?>(null));

    private static SnapshotRead<List<EconomyRequest>> ReceiptsRead(BatchState state) => Rows<EconomyRequest>("""
        SELECT * FROM EconomyRequest WHERE CreateUserId = @CreateUserId
            AND RequestId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@Ids));
        """, rows => state.Receipts = rows);

    private static SnapshotRead<List<PlayerParams>> ParametersRead(BatchState state, bool includeProgress,
        bool includeActiveWave) => Rows<PlayerParams>($"""
        SELECT * FROM PlayerParams WHERE CreateUserId = @CreateUserId AND Status != -1
            AND (([Group] = 'server.revisions' AND (Name IN
                ('GameProgressModel', 'TalentModel', 'AchievementProgressModel', 'WeaponInstanceModel', 'BackgroundSyncResponse')
                OR Name IN (SELECT value FROM OPENJSON(@Scopes))))
                {(includeProgress ? "OR ([Group] = 'game_progress' OR [Group] LIKE 'game[_]progress.%')" : "")}
                OR ([Group] = 'game_progress' AND Name = 'gold')
                {(includeActiveWave ? "OR ([Group] = 'economy' AND Name = 'active_wave')" : "")})
            ORDER BY [Group], Sequence;
        """, rows => state.Parameters = rows);

    private static void AddTalentReads(SnapshotReadBatch batch, BatchState state)
    {
        batch.Add(new SnapshotRead<Talent?>("""
            SELECT TOP 1 * FROM Talent WHERE CreateUserId = @CreateUserId AND Status != -1;
            """, async reader => state.Talent = await reader.ReadSingleOrDefaultAsync<Talent>()));
        batch.Add(Rows<TalentAcquired>("""
            SELECT c.* FROM TalentAcquired c INNER JOIN Talent p ON p.Id = c.TalentId
            WHERE c.CreateUserId = @CreateUserId AND p.CreateUserId = @CreateUserId AND p.Status != -1 ORDER BY c.Sequence;
            """, rows => state.Acquired = rows));
        batch.Add(Rows<TalentLegacyCategory>("""
            SELECT c.* FROM TalentLegacyCategory c INNER JOIN Talent p ON p.Id = c.TalentId
            WHERE c.CreateUserId = @CreateUserId AND p.CreateUserId = @CreateUserId AND p.Status != -1 ORDER BY c.Sequence;
            """, rows => state.Legacy = rows));
    }

    private static SnapshotRead<List<TalentAcquired>> GoldTalentsRead(BatchState state) => Rows<TalentAcquired>("""
        SELECT * FROM TalentAcquired WHERE CreateUserId = @CreateUserId AND Status != -1
            AND TreeId = 'utility' AND NodeId IN ('gold_1', 'gold_2', 'gold_3');
        """, rows => state.GoldTalents = rows);

    private static void AddWeaponReads(SnapshotReadBatch batch, BatchState state)
    {
        batch.Add(Rows<Weapon>("""
            SELECT * FROM Weapon WHERE CreateUserId = @CreateUserId AND Status != -1 ORDER BY CreateDate;
            """, rows => state.Weapons = rows));
        batch.Add(Rows<WeaponAffix>("""
            SELECT c.* FROM WeaponAffix c INNER JOIN Weapon p ON p.Id = c.WeaponId
            WHERE c.CreateUserId = @CreateUserId AND p.CreateUserId = @CreateUserId AND p.Status != -1 ORDER BY c.Sequence;
            """, rows => state.Affixes = rows));
        batch.Add(Rows<WeaponAffixValue>("""
            SELECT c.* FROM WeaponAffixValue c INNER JOIN WeaponAffix p ON p.Id = c.WeaponAffixId INNER JOIN Weapon w ON w.Id = p.WeaponId
            WHERE c.CreateUserId = @CreateUserId AND p.CreateUserId = @CreateUserId
                AND w.CreateUserId = @CreateUserId AND w.Status != -1 ORDER BY c.Sequence;
            """, rows => state.Values = rows));
        batch.Add(Rows<WeaponUniqueValue>("""
            SELECT c.* FROM WeaponUniqueValue c INNER JOIN Weapon p ON p.Id = c.WeaponId
            WHERE c.CreateUserId = @CreateUserId AND p.CreateUserId = @CreateUserId AND p.Status != -1 ORDER BY c.Sequence;
            """, rows => state.Unique = rows));
    }

    private static SnapshotRead<List<string>> ClaimedKillsRead(BatchState state) => Rows<string>("""
        SELECT DISTINCT k.Payload FROM EconomyRequest e INNER JOIN OPENJSON(@KillPayloads)
            WITH (Payload nvarchar(max) '$') k ON e.Payload = k.Payload
        WHERE e.CreateUserId = @CreateUserId AND e.Operation = 'enemy_kill';
        """, rows => state.ClaimedKills = rows.ToHashSet(StringComparer.Ordinal));
}
