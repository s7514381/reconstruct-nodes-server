using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Models;

namespace ReconstructNodesServer.Services;

public class PlayerLifecycleService(BaseServiceArgument argument, RevisionCommand revisions,
    GameProgressCommand progress, TalentService talents, CharacterService characters,
    ItemService items, WeaponService weapons, AchievementProgressCommand achievements)
    : _ModelService<PlayerParams, PlayerParams>(argument)
{
    public async Task<PlayerInitializationResponse> Initialize()
    {
        var snapshot = await ReadAll();
        if (snapshot.Progress == null || snapshot.Talent == null)
        {
            await CreateMissing(snapshot);
            snapshot = await ReadAll();
        }
        return snapshot;
    }

    // 所有領域在同一個交易內以單次 round-trip 讀取。
    protected virtual async Task<PlayerInitializationResponse> ReadAll()
    {
        var batch = new SnapshotReadBatch();
        var progressRead = batch.Add(progress.GetRead);
        var talentRead = batch.Add(talents.GetSnapshotRead);
        var charactersRead = batch.Add(characters.GetListRead);
        var achievementRead = batch.Add(achievements.GetRead);
        var itemsRead = batch.Add(items.InStockListRead);
        var weaponsRead = batch.Add(weapons.GetInstanceListRead);
        var revisionsRead = batch.Add(revisions.VectorRead);
        await batch.ExecuteAsync(_repository, _playerId);
        return new PlayerInitializationResponse
        {
            Characters = charactersRead.Value, Gold = progressRead.Value?.Gold ?? GoldCommand.StartingGold,
            Catalog = ShopCommand.Catalog, Progress = progressRead.Value, Talent = talentRead.Value,
            Achievement = achievementRead.Value, Items = itemsRead.Value,
            Weapons = weaponsRead.Value.ToList(), Revisions = revisionsRead.Value,
        };
    }

    private async Task CreateMissing(PlayerInitializationResponse snapshot)
    {
        GameProgressModel? model = snapshot.Progress;
        if (model == null)
        {
            if (snapshot.Revisions.GameProgress != 0) { throw new BusinessOperationException(ApiMessageKeys.ProgressMissing); }
            model = InitialProgress();
            model.ExpectedRevision = 0;
            await progress.Save(model);
        }
        if (snapshot.Talent == null)
        {
            int level = model.Progression.TryGetValue("level", out var savedLevel) ? savedLevel.GetInt32() : 1;
            if (level is < 1 or > ProgressionRules.MaxLevel)
            { throw new InvalidOperationException("Invalid persisted player level."); }
            await talents.Save(new TalentModel { Version = 3, TalentPoints = level, ExpectedRevision = 0 });
        }
    }

    internal static GameProgressModel InitialProgress()
    {
        var roads = Enumerable.Range(0, 5).Select(x => new GameProgressRoadModel { X = x, Y = 1 }).ToList();
        roads.Add(new GameProgressRoadModel { X = 5, Y = 1, Corner = true, Rotation = 1 });
        roads.AddRange(Enumerable.Range(2, 6).Select(y => new GameProgressRoadModel { X = 5, Y = y, Rotation = 1 }));
        return new GameProgressModel
        {
            Version = 8, Wave = 1, InfiniteLevel = 1,
            ClearedWaves = Enumerable.Range(0, 4).Select(_ => JsonSerializer.SerializeToElement(0)).ToList(),
            Progression = new() { ["level"] = JsonSerializer.SerializeToElement(1),
                ["experience"] = JsonSerializer.SerializeToElement(0), ["total_experience"] = JsonSerializer.SerializeToElement(0),
                ["experience_fifths"] = JsonSerializer.SerializeToElement(0) },
            Battlefield = new() { Version = 1, Columns = 7, Rows = 10, Castle = [5, 8], Roads = roads },
        };
    }

    public Task<CommandReceipt<WaveCompleteResult>> Complete(WaveCompleteRequest request) =>
        revisions.ExecuteIntent(request, "wave.complete", nameof(GameProgressModel), request, async () =>
        {
            if (request.SessionId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyKillInvalid); }
            var receipt = await QueryFirstAsync<EconomyRequest?>("""
                SELECT * FROM EconomyRequest WHERE CreateUserId = @CreateUserId
                    AND RequestId = @SessionId AND Operation = 'wave_start';
                """, new { CreateUserId = _playerId, request.SessionId });
            if (receipt == null) { throw new BusinessArgumentException(ApiMessageKeys.EconomySessionNotOwned); }
            GameProgressModel model = await progress.Get()
                ?? throw new BusinessOperationException(ApiMessageKeys.ProgressMissing);
            var session = ProgressionRules.ReadSession(receipt, model.InfiniteLevel);
            string? active = await QueryFirstAsync<string?>("""
                SELECT Value FROM PlayerParams WHERE CreateUserId = @CreateUserId
                    AND [Group] = 'economy' AND Name = 'active_wave' AND Status != -1;
                """, new { CreateUserId = _playerId });
            if (!Guid.TryParse(active, out var activeId) || activeId != request.SessionId)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomySessionNotOwned); }
            var indexes = (await QueryAsync<int>("""
                SELECT DISTINCT TRY_CONVERT(int, JSON_VALUE(Payload, '$.EnemyIndex'))
                FROM EconomyRequest WHERE CreateUserId = @CreateUserId AND Operation = 'enemy_kill'
                    AND TRY_CONVERT(uniqueidentifier, JSON_VALUE(Payload, '$.SessionId')) = @SessionId;
                """, new { CreateUserId = _playerId, request.SessionId })).ToHashSet();
            if (session.EnemyCount < 1 || indexes.Count != session.EnemyCount ||
                indexes.Any(i => i < 0 || i >= session.EnemyCount))
            { throw new BusinessOperationException(ApiMessageKeys.EconomyEnemyInvalid); }
            if (model.Wave != session.Wave || model.Difficulty != session.Difficulty)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyWaveInvalid); }
            var next = ProgressionRules.NextWave(model.Wave, model.Difficulty, model.InfiniteLevel);
            if (!model.WaveCompleted)
            {
                while (model.ClearedWaves.Count < 4) { model.ClearedWaves.Add(JsonSerializer.SerializeToElement(0)); }
                model.ClearedWaves[model.Difficulty] = JsonSerializer.SerializeToElement(
                    Math.Max(model.ClearedWaves[model.Difficulty].GetInt32(), model.Wave));
                model.MaxWaveReached = Math.Max(model.MaxWaveReached, model.Wave);
                model.WaveCompleted = true;
                model.ExpectedRevision = model.Revision;
                await progress.Save(model);
            }
            return new WaveCompleteResult { Progress = (await progress.Get())!,
                NextWave = next.Wave, NextDifficulty = next.Difficulty, NextInfiniteLevel = next.InfiniteLevel };
        });
}
