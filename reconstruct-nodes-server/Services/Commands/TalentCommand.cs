using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

public class TalentCommand(RevisionCommand revisions, TalentService talents)
{
    public Task<CommandReceipt<TalentModel>> Learn(TalentNodeCommandRequest request) =>
        Change(request, "foreground.talent_learn", request, model => TalentRules.Learn(model, request.TreeId, request.NodeId));
    public Task<CommandReceipt<TalentModel>> RefundNode(TalentNodeCommandRequest request) =>
        Change(request, "foreground.talent_refund", request, model => TalentRules.RefundNode(model, request.TreeId, request.NodeId));
    public Task<CommandReceipt<TalentModel>> Reset(TalentResetRequest request) =>
        Change(request, "foreground.talent_reset", request, model => TalentRules.Reset(model, request.TreeId));

    private Task<CommandReceipt<TalentModel>> Change(ForegroundRequest request, string operation, object payload,
        Action<TalentModel> change) =>
        revisions.Execute(request, operation, nameof(TalentModel), payload, async () =>
        {
            TalentModel model = await talents.GetSnapshot() ?? throw new BusinessOperationException(ApiMessageKeys.TalentMissing);
            model.ExpectedRevision = request.ExpectedRevision;
            change(model);
            await talents.Save(model);
            return (await talents.GetSnapshot())!;
        });
}
