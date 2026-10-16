using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

public partial class BackgroundSyncCommand
{
    internal sealed class BatchState
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Parameters { get; set; } = [];
        public Talent? Talent { get; set; }
        public List<TalentAcquired> Acquired { get; set; } = [];
        public List<TalentLegacyCategory> Legacy { get; set; } = [];
        public List<TalentAcquired> GoldTalents { get; set; } = [];
        public List<Weapon> Weapons { get; set; } = [];
        public List<WeaponAffix> Affixes { get; set; } = [];
        public List<WeaponAffixValue> Values { get; set; } = [];
        public List<WeaponUniqueValue> Unique { get; set; } = [];
        public bool TalentLoaded { get; set; }
    }

    /// <summary>升級才需要天賦列；一般擊殺同步不讀天賦，遇到升級時以天賦讀取重跑整個交易。</summary>
    private sealed class TalentReadRequired : Exception;

    private async Task<string> ExecuteBatch(BackgroundSyncRequest request)
    {
        try { return await ExecuteBatch(request, loadTalent: request.Talent != null); }
        catch (TalentReadRequired) { return await ExecuteBatch(request, loadTalent: true); }
    }

    private Task<string> ExecuteBatch(BackgroundSyncRequest request, bool loadTalent)
    {
        var state = new BatchState { TalentLoaded = loadTalent };
        var reads = BuildReads(request, state, out object parameters);
        return _repository.ExecuteAtomicJsonAsync(reads.Sql, parameters, async reader =>
        {
            await reads.ReadAsync(reader);
            return PrepareBatch(request, state);
        });
    }

    internal IReadOnlyList<SqlBatchCommand> PrepareBatch(BackgroundSyncRequest request, BatchState state)
    {
        string payload = RevisionCommand.CanonicalPayload(request);
        var receipts = state.Receipts.ToDictionary(r => r.RequestId);
        PlayerParams? ringRow = state.Parameters.SingleOrDefault(p => p.Group == SyncReceiptRing.Group && p.Name == SyncReceiptRing.Name);
        var ring = SyncReceiptRing.Parse(ringRow?.Value);
        if (ring.Replay(request.RequestId, payload) is { } ringed)
        {
            // 回執不含庫存快照，重放時在同一段 SQL 補上目前在庫物品。
            return [new SqlBatchCommand(StockSql + "SELECT @Result;",
                new { CreateUserId = _playerId, Receipt = ringed.Result, Items = ringed.Stock })];
        }
        // 舊版每批一列 background.sync 收據（不再新增）仍可重播。
        if (Replay<CommandReceipt<BackgroundSyncResponse>>(receipts, request.RequestId, "background.sync", payload) is { } prior)
        { return [new SqlBatchCommand("SELECT @Receipt;", new { Receipt = JsonSerializer.Serialize(prior) })]; }
        if (request.Progress != null) { throw new BusinessArgumentException(ApiMessageKeys.RequestInvalid); }

        var writes = new SqlWriteBatch(_playerId);
        var addedReceipts = new List<EconomyRequest>();
        var newParameters = new List<PlayerParams>();
        var updatedParameters = new Dictionary<Guid, PlayerParams>();
        long Revision(string domain) => state.Parameters
            .Where(p => p.Group == RevisionCommand.RevisionGroup && p.Name == domain)
            .Select(p => long.Parse(p.Value, CultureInfo.InvariantCulture)).SingleOrDefault();
        void CheckMissingSnapshot(bool exists, string domain)
        {
            if (!exists && Revision(domain) != 0)
            {
                throw new BusinessOperationException(new ApiMessage("concurrency.revision_conflict",
                    new Dictionary<string, string> { ["domain"] = domain,
                        ["revision"] = Revision(domain).ToString(CultureInfo.InvariantCulture) }));
            }
        }
        void Parameter(string group, string name, string value)
        {
            PlayerParams? row = state.Parameters.SingleOrDefault(p => p.Group == group && p.Name == name);
            if (row == null)
            {
                row = new PlayerParams { Group = group, Name = name, Value = value };
                state.Parameters.Add(row);
                newParameters.Add(row);
            }
            else if (row.Value != value && !newParameters.Contains(row)) { updatedParameters[row.Id] = row; }
            row.Value = value;
        }
        long Advance(string domain, string? scope = null)
        {
            long next = checked(Revision(domain) + 1);
            Parameter(RevisionCommand.RevisionGroup, domain, next.ToString(CultureInfo.InvariantCulture));
            if (scope != null)
            { Parameter(RevisionCommand.RevisionGroup, RevisionCommand.ScopeKey(domain, scope), next.ToString(CultureInfo.InvariantCulture)); }
            return next;
        }
        void Receipt<T>(Guid id, string operation, string intent, T result)
        {
            var row = new EconomyRequest { RequestId = id, Operation = operation, Payload = intent,
                Result = JsonSerializer.Serialize(result) };
            receipts.Add(id, row);
            addedReceipts.Add(row);
        }

        PlayerParams? goldRow = state.Parameters.SingleOrDefault(p => p.Group == "game_progress" && p.Name == "gold");
        long balance = goldRow == null ? GoldCommand.StartingGold : GoldCommand.ReadBalance(goldRow);
        bool goldChanged = false;
        int talentPointsEarned = 0;
        bool progressChanged = false;
        var response = new BackgroundSyncResponse { RequestId = request.RequestId };
        var serverProgress = request.Kills.Count > 0 || request.WaveStarts.Count > 0
            ? GameProgressCommand.BuildSnapshot(state.Parameters.Where(p => p.Group == "game_progress" ||
                p.Group.StartsWith("game_progress.", StringComparison.Ordinal)).ToList(), [], [],
                Revision(nameof(GameProgressModel)), balance)
                ?? throw new BusinessOperationException(ApiMessageKeys.ProgressMissing)
            : null;
        string? ActiveWave() => state.Parameters.SingleOrDefault(p => p.Group == KillLedger.Group && p.Name == "active_wave")?.Value;
        KillLedger? killLedger = null;
        KillLedger Ledger() => killLedger ??= KillLedger.Parse(state.Parameters
            .SingleOrDefault(p => p.Group == KillLedger.Group && p.Name == KillLedger.Name)?.Value);
        bool killLedgerChanged = false;
        foreach (WaveStartRequest wave in request.WaveStarts)
        {
            int enemyCount = EconomyCommand.WaveEnemyCount(wave);
            string intent = JsonSerializer.Serialize(new { wave.Wave, wave.Difficulty });
            var session = Replay<WaveStartResult>(receipts, wave.RequestId, "wave_start", intent);
            if (session == null)
            {
                var target = serverProgress!.WaveCompleted
                    ? ProgressionRules.NextWave(serverProgress.Wave, serverProgress.Difficulty, serverProgress.InfiniteLevel)
                    : (serverProgress.Wave, serverProgress.Difficulty, serverProgress.InfiniteLevel);
                if (wave.Wave != target.Item1 || wave.Difficulty != target.Item2)
                { throw new BusinessArgumentException(ApiMessageKeys.EconomyWaveInvalid); }
                session = new WaveStartResult { SessionId = wave.RequestId, Wave = wave.Wave,
                    Difficulty = wave.Difficulty, InfiniteLevel = target.Item3, EnemyCount = enemyCount, Gold = balance };
                serverProgress.Wave = wave.Wave;
                serverProgress.Difficulty = wave.Difficulty;
                serverProgress.InfiniteLevel = target.Item3;
                serverProgress.WaveCompleted = false;
                serverProgress.MaxWaveReached = Math.Max(serverProgress.MaxWaveReached, wave.Wave);
                Parameter("game_progress", "wave", wave.Wave.ToString(CultureInfo.InvariantCulture));
                Parameter("game_progress", "difficulty", wave.Difficulty.ToString(CultureInfo.InvariantCulture));
                Parameter("game_progress", "infinite_level", target.Item3.ToString(CultureInfo.InvariantCulture));
                Parameter("game_progress", "wave_completed", bool.FalseString);
                Parameter("game_progress", "max_wave_reached", serverProgress.MaxWaveReached.ToString(CultureInfo.InvariantCulture));
                progressChanged = true;
                Parameter("economy", "active_wave", wave.RequestId.ToString());
                Ledger().Start(wave.RequestId);
                killLedgerChanged = true;
                Receipt(wave.RequestId, "wave_start", intent, session);
                goldChanged = true;
            }
            response.Sessions.Add(session);
        }
        foreach (EnemyKillRequest kill in request.Kills)
        {
            if (kill.SessionId == Guid.Empty || kill.EnemyIndex < 0)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyKillInvalid); }
            // 已領取的擊殺記在最近幾個波次的位元圖；重送（同批次或新信封）視為已套用，不再發獎或計數。
            // 只接受進行中或位元圖內（最近幾個）的波次。
            var ledger = Ledger();
            if (!(Guid.TryParse(ActiveWave(), out Guid activeWave) && activeWave == kill.SessionId) && !ledger.Contains(kill.SessionId))
            { throw new BusinessArgumentException(ApiMessageKeys.EconomySessionNotOwned); }
            if (ledger.Has(kill.SessionId, kill.EnemyIndex)) { continue; }
            receipts.TryGetValue(kill.SessionId, out EconomyRequest? session);
            balance = checked(balance + KillRewardRules.CalculateClaim(
                session?.Operation == "wave_start" ? session.Result : null,
                false, kill.EnemyIndex, state.GoldTalents));
            var waveSession = ProgressionRules.ReadSession(session!, serverProgress!.InfiniteLevel);
            talentPointsEarned = checked(talentPointsEarned +
                ProgressionRules.ApplyKill(serverProgress!.Progression, waveSession, kill.EnemyIndex));
            serverProgress.LifetimeEnemiesDefeated = checked(serverProgress.LifetimeEnemiesDefeated + 1);
            progressChanged = true;
            ledger.Add(kill.SessionId, kill.EnemyIndex);
            killLedgerChanged = true;
            goldChanged = true;
        }
        if (killLedgerChanged) { Parameter(KillLedger.Group, KillLedger.Name, killLedger!.ToString()); }
        if (progressChanged)
        {
            Parameter("game_progress", "lifetime_enemies_defeated",
                serverProgress!.LifetimeEnemiesDefeated.ToString(CultureInfo.InvariantCulture));
            foreach (var (key, value) in serverProgress.Progression)
            { Parameter("game_progress.progression", key, value.GetRawText()); }
            serverProgress.Revision = Advance(nameof(GameProgressModel));
            serverProgress.Gold = balance;
            response.Progress = serverProgress;
        }
        if (talentPointsEarned > 0)
        {
            if (state.Talent == null && !state.TalentLoaded) { throw new TalentReadRequired(); }
            if (state.Talent == null) { throw new BusinessOperationException(ApiMessageKeys.ProgressMissing); }
            state.Talent.TalentPoints = checked(state.Talent.TalentPoints + talentPointsEarned);
        }
        if (request.Talent != null)
        {
            CheckMissingSnapshot(state.Talent != null, nameof(TalentModel));
            var incoming = Clone(request.Talent);
            var current = state.Talent == null ? new TalentModel() : TalentService.BuildSnapshot(
                state.Talent, state.Acquired, state.Legacy, Revision(nameof(TalentModel)));
            MergeTalent(incoming, current);
            TalentService.ValidateTalent(incoming);
            Talent row = state.Talent ?? new Talent();
            row.Version = incoming.Version;
            row.TalentPoints = incoming.TalentPoints;
            row.TotalSpentPoints = incoming.TotalSpentPoints;
            row.PointsRecalculationVersion = incoming.PointsRecalculationVersion;
            // Background merge preserves both child collections; do not delete/reinsert unchanged nodes.
            if (state.Talent == null) { writes.Insert([row]); }
            else { writes.Update([row]); }
            incoming.Revision = Advance(nameof(TalentModel));
            response.Talent = TalentService.BuildSnapshot(row, state.Acquired, state.Legacy, incoming.Revision);
        }
        else if (talentPointsEarned > 0)
        {
            writes.Update([state.Talent!]);
            response.Talent = TalentService.BuildSnapshot(state.Talent!, state.Acquired, state.Legacy,
                Advance(nameof(TalentModel)));
        }
        if (request.Items.Count > 0)
        {
            Item row = new();
            writes.Add(ItemService.BulkAdjustmentSql, new { Items = JsonSerializer.Serialize(
                    request.Items.Select(i => new { k = i.ItemKey, d = i.Delta, c = i.Category })),
                row.CreateDate, Status = (int?)row.Status, Whitespace = ItemService.WhitespaceCharacters });
        }
        foreach (WeaponAcquireRequest acquisition in request.WeaponAcquisitions)
        {
            string intent = RevisionCommand.CanonicalPayload(acquisition);
            var replay = Replay<CommandReceipt<WeaponInstanceModel>>(receipts, acquisition.RequestId, "weapon.acquire", intent);
            if (replay != null) { response.Acquisitions.Add(replay); continue; }
            if (acquisition.Weapon == null) { throw new BusinessArgumentException(ApiMessageKeys.RequestInvalid); }
            WeaponService.ValidateWeapon(acquisition.Weapon);
            QueueWeapon(writes, state, acquisition.Weapon);
            long revision = Advance(nameof(WeaponInstanceModel), acquisition.Weapon.Id);
            var snapshot = WeaponService.BuildSnapshots(state.Weapons, state.Affixes, state.Values, state.Unique, revision)
                .Single(w => w.Id == acquisition.Weapon.Id);
            var receipt = new CommandReceipt<WeaponInstanceModel> { RequestId = acquisition.RequestId,
                Revision = revision, Snapshot = snapshot };
            Receipt(acquisition.RequestId, "weapon.acquire", intent, receipt);
            response.Acquisitions.Add(receipt);
        }
        if (goldChanged) { Parameter("game_progress", "gold", balance.ToString(CultureInfo.InvariantCulture)); }
        response.Gold = balance;
        writes.Insert(newParameters);
        writes.Update(updatedParameters.Values);
        writes.Insert(addedReceipts);
        var batchReceipt = new CommandReceipt<BackgroundSyncResponse> { RequestId = request.RequestId,
            Revision = Revision(nameof(BackgroundSyncResponse)), Snapshot = response };
        writes.Add(StockSql + """
            DECLARE @Stocked bit = @Items;
            DECLARE @Stored nvarchar(max) = JSON_MODIFY(@Receipt, '$.snapshot.items', NULL);
            DECLARE @Ring nvarchar(max) = JSON_MODIFY(@Retained, 'append $',
                JSON_QUERY((SELECT @RequestId AS i, @PayloadHash AS h, @Stored AS r, @Stocked AS s FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)));
            IF @RingId IS NULL
                INSERT INTO PlayerParams (Id, CreateDate, CreateUserId, Status, [Group], Name, Value)
                VALUES (NEWID(), @Now, @CreateUserId, @Enabled, @RingGroup, @RingName, @Ring);
            ELSE
                UPDATE PlayerParams SET Value = @Ring WHERE Id = @RingId AND CreateUserId = @CreateUserId;
            SELECT @Result;
            """, new { Receipt = JsonSerializer.Serialize(batchReceipt), Items = request.Items.Count > 0,
                RequestId = request.RequestId, PayloadHash = SyncReceiptRing.Hash(payload), Now = DateTime.Now,
                Retained = ring.Retained(request.RequestId), RingId = ringRow?.Id,
                RingGroup = SyncReceiptRing.Group, RingName = SyncReceiptRing.Name,
                Enabled = (int)ReconstructNodesServer.Library.Models.Enum.EStatus.啟用 });
        return writes.Commands;
    }

    // 回應用的目前在庫物品（@Items = 1 時才附上）；結果放在 @Result。
    private const string StockSql = """
        DECLARE @Result nvarchar(max) = @Receipt;
        IF @Items = 1
        BEGIN
            DECLARE @Stock nvarchar(max) = (SELECT Id, Sequence, CreateDate, CreateUserId, Status, ItemKey, Amount, Category
                FROM Item WHERE CreateUserId = @CreateUserId AND Status != -1 AND Amount > 0
                FOR JSON PATH, INCLUDE_NULL_VALUES);
            SET @Result = JSON_MODIFY(@Result, '$.snapshot.items', JSON_QUERY(@Stock));
        END;

        """;

    private static T? Replay<T>(Dictionary<Guid, EconomyRequest> receipts, Guid id, string operation, string payload)
        where T : class
    {
        if (!receipts.TryGetValue(id, out var row)) { return null; }
        if (row.Operation != operation || row.Payload != payload)
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
        return JsonSerializer.Deserialize<T>(row.Result) ?? throw new InvalidOperationException("Invalid command receipt.");
    }

    private static void QueueWeapon(SqlWriteBatch writes, BatchState state, WeaponInstanceModel model)
    {
        if (state.Weapons.Any(w => w.EntryKey == model.Id))
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
        var affixes = WeaponSnapshotCodec.ReadAffixes(model.Affixes ?? []);
        var unique = WeaponSnapshotCodec.ReadUnique(model.Unique);
        Weapon row = new() { EntryKey = model.Id, WeaponKey = model.WeaponId, WeaponFamilyKey = model.WeaponFamilyId,
            Rarity = model.Rarity, UniqueKey = unique?.UniqueKey };
        // Let SQL collation handle aliases, including tombstones and earlier acquisitions in this batch.
        writes.Add("""
            IF EXISTS (SELECT 1 FROM Weapon WITH (UPDLOCK, HOLDLOCK) WHERE CreateUserId = @CreateUserId AND EntryKey = @EntryKey)
                THROW 51003, 'Weapon entry conflict', 1;
            """, new { EntryKey = model.Id });
        writes.Insert([row]);
        var children = WeaponService.QueueChildren(writes, row.Id, affixes, unique);
        state.Weapons.Add(row);
        state.Affixes.AddRange(children.Affixes);
        state.Values.AddRange(children.Values);
        state.Unique.AddRange(children.Unique);
    }
}
