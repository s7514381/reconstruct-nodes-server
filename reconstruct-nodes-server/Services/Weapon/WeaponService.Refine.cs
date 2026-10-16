using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Services;

public partial class WeaponService
{
    internal async Task<CommandReceipt<WeaponRefineSnapshot>> RefineForCommand(WeaponRefineRequest request)
    {
        if (request.RequestId == Guid.Empty)
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        string payload = RevisionService.CanonicalPayload(request);
        long expectedRevision = 0;
        string? replay = null;
        try
        {
            const string readSql = """
                DECLARE @Owner uniqueidentifier;
                SELECT @Owner = Id FROM Player WHERE Id = @CreateUserId;
                IF @Owner IS NULL THROW 51002, 'Weapon owner is unavailable', 1;
                SELECT * FROM EconomyRequest WHERE CreateUserId = @CreateUserId AND RequestId = @RequestId;
                DECLARE @WeaponId uniqueidentifier;
                SELECT @WeaponId = Id FROM Weapon
                WHERE CreateUserId = @CreateUserId AND EntryKey = @EntryKey AND Status != -1;
                IF @@ROWCOUNT > 1 THROW 51004, 'Duplicate weapon ownership', 1;
                SELECT * FROM Weapon WHERE Id = @WeaponId AND CreateUserId = @CreateUserId;
                SELECT * FROM WeaponAffix WHERE WeaponId = @WeaponId AND CreateUserId = @CreateUserId ORDER BY Sequence;
                SELECT v.* FROM WeaponAffixValue v INNER JOIN WeaponAffix a ON a.Id = v.WeaponAffixId
                WHERE a.WeaponId = @WeaponId AND a.CreateUserId = @CreateUserId AND v.CreateUserId = @CreateUserId ORDER BY v.Sequence;
                SELECT * FROM WeaponUniqueValue WHERE WeaponId = @WeaponId AND CreateUserId = @CreateUserId ORDER BY Sequence;
                SELECT * FROM PlayerParams WHERE CreateUserId = @CreateUserId AND Status != -1
                    AND [Group] = 'server.revisions' AND Name = 'WeaponInstanceModel';
                """;
            var parameters = new { CreateUserId = _playerId, request.RequestId, EntryKey = request.WeaponEntryId };
            Func<ManagedGridReader, Task<IReadOnlyList<SqlBatchCommand>>> prepare = async reader =>
                {
                    EconomyRequest? prior = await reader.ReadSingleOrDefaultAsync<EconomyRequest>();
                    Weapon? row = await reader.ReadSingleOrDefaultAsync<Weapon>();
                    var affixes = await reader.ReadAsync<WeaponAffix>();
                    var values = await reader.ReadAsync<WeaponAffixValue>();
                    var unique = await reader.ReadAsync<WeaponUniqueValue>();
                    PlayerParams? revision = await reader.ReadSingleOrDefaultAsync<PlayerParams>();
                    if (prior != null)
                    {
                        if (prior.Operation != "weapon.refine" || prior.Payload != payload)
                        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
                        replay = prior.Result;
                        return [new SqlBatchCommand("SELECT @Receipt;", new { Receipt = prior.Result })];
                    }
                    if (row == null) { throw new BusinessArgumentException(ApiMessageKeys.WeaponNotOwned); }
                    long current = revision == null ? 0 : long.Parse(revision.Value, CultureInfo.InvariantCulture);
                    expectedRevision = current;
                    WeaponInstanceModel original = BuildSnapshots([row], affixes, values, unique, current).Single();
                    WeaponInstanceModel candidate = RefinementRules.Refine(original, request.CrystalId);
                    long next = checked(current + 1);
                    candidate.Revision = next;
                    row.Rarity = candidate.Rarity;
                    var parsedUnique = WeaponSnapshotCodec.ReadUnique(candidate.Unique);
                    row.UniqueKey = parsedUnique?.UniqueKey;
                    var writes = new SqlWriteBatch(_playerId);
                    writes.Update([row]);
                    if (!TryQueueRefinedValues(writes, candidate, affixes, values, unique, parsedUnique))
                    {
                        writes.Add("""
                        DELETE v FROM WeaponAffixValue v INNER JOIN WeaponAffix a ON a.Id = v.WeaponAffixId
                        WHERE a.WeaponId = @WeaponId AND a.CreateUserId = @CreateUserId AND v.CreateUserId = @CreateUserId;
                        DELETE FROM WeaponAffix WHERE WeaponId = @WeaponId AND CreateUserId = @CreateUserId;
                        DELETE FROM WeaponUniqueValue WHERE WeaponId = @WeaponId AND CreateUserId = @CreateUserId;
                        """, new { WeaponId = row.Id });
                        QueueChildren(writes, row.Id, WeaponSnapshotCodec.ReadAffixes(candidate.Affixes), parsedUnique);
                    }
                    var now = DateTime.Now;
                    foreach (string name in new[] { nameof(WeaponInstanceModel),
                        RevisionService.ScopeKey(nameof(WeaponInstanceModel), row.EntryKey),
                        RevisionService.ScopeKey(nameof(WeaponInstanceModel), request.WeaponEntryId) }.Distinct())
                    {
                        writes.Add("""
                            UPDATE PlayerParams SET Value = @Value WHERE CreateUserId = @CreateUserId
                                AND Status != -1 AND [Group] = 'server.revisions' AND Name = @Name;
                            IF @@ROWCOUNT = 0
                                INSERT INTO PlayerParams (Id, CreateDate, CreateUserId, Status, [Group], Name, Value)
                                VALUES (NEWID(), @Now, @CreateUserId, @Enabled, 'server.revisions', @Name, @Value);
                            """, new
                        {
                            Name = name,
                            Value = next.ToString(CultureInfo.InvariantCulture),
                            Now = now,
                            Enabled = (int)Library.Models.Enum.EStatus.啟用
                        });
                    }
                    var receipt = new CommandReceipt<WeaponRefineSnapshot>
                    {
                        RequestId = request.RequestId,
                        Revision = next,
                        Snapshot = new() { Weapon = candidate, CrystalId = request.CrystalId },
                    };
                    writes.Add(ItemService.AdjustmentSql + """
                        DECLARE @Result nvarchar(max) = JSON_MODIFY(@Receipt, '$.snapshot.material_count', @Amount);
                        INSERT INTO EconomyRequest (Id, CreateDate, CreateUserId, Status, RequestId, Operation, Payload, Result)
                        VALUES (NEWID(), @CreateDate, @CreateUserId, @Status, @RequestId, 'weapon.refine', @Payload, @Result);
                        SELECT @Result;
                        """, new
                    {
                        ItemKey = request.CrystalId,
                        Delta = -1,
                        Category = (string?)null,
                        NewId = Guid.NewGuid(),
                        CreateDate = now,
                        Status = (int)Library.Models.Enum.EStatus.啟用,
                        Whitespace = ItemService.WhitespaceCharacters,
                        Receipt = JsonSerializer.Serialize(receipt),
                        request.RequestId,
                        Payload = payload
                    });
                    return writes.Commands;
                };
            string json = _repository.Transaction != null
                ? await _repository.ExecuteAtomicJsonAsync(readSql, parameters, prepare)
                : await ExecuteShortRefine(readSql, parameters, prepare, () => expectedRevision, () => replay,
                    request.RequestId, payload);
            return JsonSerializer.Deserialize<CommandReceipt<WeaponRefineSnapshot>>(json)
                ?? throw new InvalidOperationException("Invalid weapon refinement receipt.");
        }
        catch (System.Data.SqlClient.SqlException error) when (error.Number == 51001)
        { throw new BusinessOperationException(ApiMessageKeys.ItemInsufficient); }
        catch (System.Data.SqlClient.SqlException error) when (error.Number == 8115)
        { throw new OverflowException("Weapon refinement amount overflowed.", error); }
        catch (System.Data.SqlClient.SqlException error) when (error.Number == 51003)
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
    }

    private async Task<string> ExecuteShortRefine(string readSql, object parameters,
        Func<ManagedGridReader, Task<IReadOnlyList<SqlBatchCommand>>> prepare,
        Func<long> expectedRevision, Func<string?> replay, Guid requestId, string payload)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                IReadOnlyList<SqlBatchCommand> writes;
                using (RequestTiming.Measure("RefineReadSnapshot"))
                {
                    await using (var reader = await _repository.QueryMultipleAsync("""
                        BEGIN TRY
                            SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
                            BEGIN TRAN;
                        """ + readSql + """
                            COMMIT;
                            SELECT CAST(1 AS int);
                        END TRY
                        BEGIN CATCH
                            IF XACT_STATE() <> 0 ROLLBACK;
                            THROW;
                        END CATCH;
                        """, parameters))
                    {
                        // Finish the consistent read before opening the conditional write transaction.
                        writes = await prepare(reader);
                        if (await reader.ReadSingleAsync<int>() != 1 || !reader.IsConsumed)
                        { throw new InvalidOperationException("Refine snapshot transaction did not finish."); }
                    }
                }
                if (replay() is { } prior) { return prior; }
                if (writes.Count != 1) { throw new InvalidOperationException("Refine must fit in one write batch."); }
                var command = writes[0];
                var arguments = new Dapper.DynamicParameters(command.Parameters);
                arguments.Add("ExpectedRevision", expectedRevision());
                arguments.Add("IntentRequestId", requestId);
                arguments.Add("IntentPayload", payload);
                using var writeTiming = RequestTiming.Measure("RefineWriteCommit");
                return await _repository.QueryFirstAsync<string>("""
                        BEGIN TRY
                            SET XACT_ABORT ON;
                            SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
                            BEGIN TRAN;
                            DECLARE @ExistingResult nvarchar(max), @ExistingOperation nvarchar(max), @ExistingPayload nvarchar(max);
                            SELECT @ExistingResult = Result, @ExistingOperation = Operation, @ExistingPayload = Payload
                            FROM EconomyRequest WITH (UPDLOCK, HOLDLOCK)
                            WHERE CreateUserId = @CreateUserId AND RequestId = @IntentRequestId;
                            IF @ExistingOperation IS NOT NULL
                            BEGIN
                                IF @ExistingOperation COLLATE Latin1_General_100_BIN2 <> N'weapon.refine'
                                    OR DATALENGTH(@ExistingOperation) <> DATALENGTH(N'weapon.refine')
                                    OR @ExistingPayload COLLATE Latin1_General_100_BIN2 <> @IntentPayload COLLATE Latin1_General_100_BIN2
                                    OR DATALENGTH(@ExistingPayload) <> DATALENGTH(@IntentPayload)
                                    THROW 51003, 'Weapon request conflict', 1;
                                COMMIT;
                                SELECT @ExistingResult;
                            END
                            ELSE
                            BEGIN
                                DECLARE @ActualRevision bigint;
                                SELECT @ActualRevision = CONVERT(bigint, Value) FROM PlayerParams WITH (UPDLOCK, HOLDLOCK)
                                WHERE CreateUserId = @CreateUserId AND [Group] = 'server.revisions'
                                    AND Name = 'WeaponInstanceModel' AND Status != -1;
                                IF COALESCE(@ActualRevision, 0) <> @ExpectedRevision
                                    THROW 51006, 'Weapon changed during refinement preparation', 1;
                        """ + command.Sql + """
                                COMMIT;
                            END
                        END TRY
                        BEGIN CATCH
                            IF XACT_STATE() <> 0 ROLLBACK;
                            THROW;
                        END CATCH;
                        """, arguments);
            }
            catch (Exception error) when (attempt < 2 && SqlConcurrency.IsRetryable(error))
            {
                using var retry = RequestTiming.Measure("ConcurrencyRetry");
                Console.WriteLine($"Weapon refinement resource retry {attempt + 1}.");
            }
        }
    }
    private static bool TryQueueRefinedValues(SqlWriteBatch writes, WeaponInstanceModel candidate,
        IEnumerable<WeaponAffix> affixes, IEnumerable<WeaponAffixValue> values, IEnumerable<WeaponUniqueValue> unique,
        WeaponUniqueSnapshot? nextUnique)
    {
        if (nextUnique != null || unique.Any()) { return false; }
        var next = WeaponSnapshotCodec.ReadAffixes(candidate.Affixes);
        var previous = affixes.OrderBy(row => row.Sequence).ToArray();
        if (previous.Length != next.Count) { return false; }
        var updates = new List<(WeaponAffixValue Row, string Value)>();
        for (int i = 0; i < previous.Length; i++)
        {
            if (previous[i].Sequence != i || previous[i].AffixKey != next[i].AffixKey ||
                previous[i].Rolled != next[i].Rolled) { return false; }
            var current = values.Where(row => row.WeaponAffixId == previous[i].Id).OrderBy(row => row.Sequence).ToArray();
            if (current.Length != next[i].Values.Count) { return false; }
            for (int j = 0; j < current.Length; j++)
            {
                if (current[j].Sequence != j) { return false; }
                string value = WeaponSnapshotCodec.FormatValue(next[i].Values[j]);
                if (current[j].Value != value) { updates.Add((current[j], value)); }
            }
        }
        foreach (var update in updates) { update.Row.Value = update.Value; }
        writes.Update(updates.Select(update => update.Row));
        return true;
    }

    internal static (List<WeaponAffix> Affixes, List<WeaponAffixValue> Values, List<WeaponUniqueValue> Unique)
        QueueChildren(SqlWriteBatch writes, Guid weaponId,
        IReadOnlyList<WeaponAffixSnapshot> affixes, WeaponUniqueSnapshot? unique)
    {
        var children = affixes.Select((a, i) => new WeaponAffix
        { WeaponId = weaponId, AffixKey = a.AffixKey, Rolled = a.Rolled, Sequence = i }).ToList();
        writes.Insert(children);
        var values = affixes.SelectMany((a, i) => a.Values.Select((v, j) => new WeaponAffixValue
        {
            WeaponAffixId = children[i].Id,
            ValueKey = j.ToString(CultureInfo.InvariantCulture),
            Value = WeaponSnapshotCodec.FormatValue(v),
            Sequence = j,
        })).ToList();
        var uniques = unique?.Values.Select((v, i) => new WeaponUniqueValue
        {
            WeaponId = weaponId,
            ValueKey = i.ToString(CultureInfo.InvariantCulture),
            Value = WeaponSnapshotCodec.FormatValue(v),
            Sequence = i,
        }).ToList() ?? [];
        writes.Insert(values);
        writes.Insert(uniques);
        return (children, values, uniques);
    }
}
