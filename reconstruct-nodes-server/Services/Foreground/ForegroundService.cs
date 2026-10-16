using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

public class ForegroundService(
    BaseServiceArgument argument, RevisionCommand revisions, CharacterService characters,
    AchievementProgressCommand achievements, GameProgressCommand progress, EconomyCommand economy) : BaseService(argument)
{
    public Task<CommandReceipt<AchievementProgressModel>> ClaimAchievement(AchievementSnapshotClaimRequest request) =>
        revisions.Execute(request, "foreground.achievement_claim", nameof(AchievementProgressModel), request, async () =>
        {
            await achievements.ClaimReward(request.AchievementItemId);
            return (await achievements.Get())!;
        });

    public Task<CommandReceipt<GameProgressModel>> SetDeployment(DeploymentCommandRequest request) =>
        ChangeProgress(request, "foreground.deployment", request, model => model.DeploymentLog = request.DeploymentLog);

    public Task<CommandReceipt<WaveTransitionSnapshot>> SetBattlefield(BattlefieldCommandRequest request) =>
        revisions.Execute(request, "foreground.battlefield", nameof(GameProgressModel), request, async () =>
        {
            GameProgressModel model = await CurrentProgress(request);
            if (request.Battlefield == null) { throw new BusinessArgumentException(ApiMessageKeys.ProgressCastleInvalid); }
            model.Battlefield = request.Battlefield;
            if (request.DeploymentLog != null) { model.DeploymentLog = request.DeploymentLog; }
            BattlefieldRules.Validate(model.Battlefield, model.DeploymentLog);
            model.WaveCompleted = false;
            return await SaveAndStart(request.RequestId, model);
        });

    private Task<CommandReceipt<GameProgressModel>> ChangeProgress(ForegroundRequest request, string operation,
        object payload, Action<GameProgressModel> change) =>
        revisions.Execute(request, operation, nameof(GameProgressModel), payload, async () =>
        {
            GameProgressModel model = await CurrentProgress(request);
            change(model);
            BattlefieldRules.Validate(model.Battlefield, model.DeploymentLog);
            await progress.Save(model);
            return (await progress.Get())!;
        });

    public Task<CommandReceipt<WaveTransitionSnapshot>> TransitionWave(WaveTransitionRequest request) =>
        revisions.Execute(request, "foreground.wave_transition", nameof(GameProgressModel), request, async () =>
        {
            GameProgressModel model = await CurrentProgress(request);
            if (request.Difficulty is < 0 or > 3 || request.Wave < 1 || request.Difficulty != 3 && request.Wave > 100)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyWaveInvalid); }
            int cleared = model.ClearedWaves.Count > request.Difficulty &&
                model.ClearedWaves[request.Difficulty].TryGetInt32(out int value) ? value : 0;
            if (request.Challenge ? request.Wave != 1 : request.Wave > cleared)
            { throw new BusinessOperationException(request.Challenge ? "wave.challenge_first" : "wave.cleared_only"); }
            if (request.Difficulty == 3 && model.Difficulty != 3)
            {
                if (model.Difficulty is < 0 or > 2) { throw new BusinessOperationException(ApiMessageKeys.EconomyWaveInvalid); }
                int[] steps = [2,4,8];
                model.InfiniteLevel = 1 + (Math.Max(1, model.Wave) - 1) / steps[model.Difficulty];
            }
            model.Wave = request.Wave;
            model.Difficulty = request.Difficulty;
            model.MaxWaveReached = Math.Max(model.MaxWaveReached, model.Wave);
            model.WaveCompleted = false;
            return await SaveAndStart(request.RequestId, model);
        });

    private async Task<WaveTransitionSnapshot> SaveAndStart(Guid requestId, GameProgressModel model)
    {
        await progress.Save(model);
        WaveStartResult session = await economy.StartWave(new WaveStartRequest
        {
            RequestId = ChildRequestId(requestId, "wave"), Wave = model.Wave, Difficulty = model.Difficulty,
            InfiniteLevel = model.InfiniteLevel,
        });
        return new WaveTransitionSnapshot { Progress = (await progress.Get())!, Session = session };
    }

    private async Task<GameProgressModel> CurrentProgress(ForegroundRequest request)
    {
        GameProgressModel model = await progress.Get()
            ?? throw new BusinessOperationException(ApiMessageKeys.ProgressMissing);
        model.ExpectedRevision = request.ExpectedRevision;
        model.RequestId = ChildRequestId(request.RequestId, "progress");
        return model;
    }

    private static Guid ChildRequestId(Guid id, string operation) =>
        new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{id:D}/{operation}")).AsSpan(0, 16));

    public Task<CommandReceipt<CharacterModel>> ConfigureSkills(SkillLoadoutRequest request) =>
        revisions.Execute(request, "foreground.skill_loadout", nameof(CharacterModel), request, async () =>
        {
            SkillLoadoutRules.Validate(request.SkillTrees);
            CharacterModel model = await OwnedCharacter(request.CharacterId);
            model.ExpectedRevision = request.ExpectedRevision;
            model.SkillTrees = request.SkillTrees;
            await characters.Save(model);
            return (await characters.Get(request.CharacterId))!;
        }, [request.CharacterId.ToString("D")]);

    private async Task<CharacterModel> OwnedCharacter(Guid id) =>
        await characters.Get(id) ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned);
}
