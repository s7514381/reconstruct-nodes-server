using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers;

/// <summary>All commands execute inside the existing request-scoped SQL transaction.</summary>
public class ForegroundController(BaseApiArgument argument, ForegroundService service, TalentCommandService talents,
    RevisionService revisions) : AuthorizeApiController(argument)
{
    [HttpPost]
    public Task<IActionResult> Revisions() =>
        TransactionResultAsync(() => revisions.GetVector(), ApiMessageKeys.ErrorUnexpected);
    [HttpPost]
    public Task<IActionResult> LearnTalent(TalentNodeCommandRequest request) =>
        TransactionResultAsync(() => talents.Learn(request), ApiMessageKeys.ErrorUnexpected);
    [HttpPost]
    public Task<IActionResult> RefundTalent(TalentNodeCommandRequest request) =>
        TransactionResultAsync(() => talents.RefundNode(request), ApiMessageKeys.ErrorUnexpected);
    [HttpPost]
    public Task<IActionResult> ResetTalents(TalentResetRequest request) =>
        TransactionResultAsync(() => talents.Reset(request), ApiMessageKeys.ErrorUnexpected);
    [HttpPost]
    public Task<IActionResult> ClaimAchievement(AchievementSnapshotClaimRequest request) =>
        TransactionResultAsync(() => service.ClaimAchievement(request), ApiMessageKeys.ErrorUnexpected);

    [HttpPost]
    public Task<IActionResult> SetDeployment(DeploymentCommandRequest request) =>
        TransactionResultAsync(() => service.SetDeployment(request), ApiMessageKeys.ErrorUnexpected);

    [HttpPost]
    public Task<IActionResult> SetBattlefield(BattlefieldCommandRequest request) =>
        TransactionResultAsync(() => service.SetBattlefield(request), ApiMessageKeys.ErrorUnexpected);

    [HttpPost]
    public Task<IActionResult> TransitionWave(WaveTransitionRequest request) =>
        TransactionResultAsync(() => service.TransitionWave(request), ApiMessageKeys.ErrorUnexpected);

    [HttpPost]
    public Task<IActionResult> ConfigureSkills(SkillLoadoutRequest request) =>
        TransactionResultAsync(() => service.ConfigureSkills(request), ApiMessageKeys.ErrorUnexpected);
}
