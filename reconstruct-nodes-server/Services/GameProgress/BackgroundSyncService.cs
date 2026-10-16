using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

public partial class BackgroundSyncService(BaseServiceArgument argument) : _ModelService<PlayerParams, PlayerParams>(argument)
{
    public async Task<BackgroundSyncResponse> Execute(BackgroundSyncRequest request)
    {
        Validate(request);
        if (request.RequestId == Guid.Empty)
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        try
        {
            string result = await ExecuteBatch(request);
            return JsonSerializer.Deserialize<CommandReceipt<BackgroundSyncResponse>>(result)?.Snapshot
                ?? throw new InvalidOperationException("Invalid background receipt.");
        }
        catch (System.Data.SqlClient.SqlException error) when (error.Number == 51001)
        { throw new BusinessOperationException(ApiMessageKeys.ItemInsufficient); }
        catch (System.Data.SqlClient.SqlException error) when (error.Number == 51003)
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
        catch (System.Data.SqlClient.SqlException error) when (error.Number == 8115)
        { throw new OverflowException("Background value overflowed.", error); }
    }

    private static void Validate(BackgroundSyncRequest request)
    {
        if (request.Talent != null) { TalentService.ValidateTalent(request.Talent); }
        if (request.Kills == null || request.WaveStarts == null || request.Items == null ||
            request.WeaponAcquisitions == null ||
            request.Kills.Any(k => k == null || k.RequestId == Guid.Empty) ||
            request.WaveStarts.Any(w => w == null || w.RequestId == Guid.Empty) ||
            request.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.ItemKey) || i.Delta == 0) ||
            request.WeaponAcquisitions.Any(w => w == null || w.RequestId == Guid.Empty))
        { throw new BusinessArgumentException(ApiMessageKeys.RequestInvalid); }
        Guid[] children = request.Kills.Select(k => k.RequestId)
            .Concat(request.WaveStarts.Select(w => w.RequestId))
            .Concat(request.WeaponAcquisitions.Select(w => w.RequestId)).ToArray();
        if (children.Contains(request.RequestId) || children.Distinct().Count() != children.Length)
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
    }

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))
        ?? throw new InvalidOperationException("Invalid background snapshot.");

    public static void MergeTalent(TalentModel incoming, TalentModel current)
    {
        long earned = Math.Max(checked(incoming.TalentPoints + incoming.TotalSpentPoints),
            checked(current.TalentPoints + current.TotalSpentPoints));
        incoming.TalentPoints = checked(earned - current.TotalSpentPoints);
        incoming.TotalSpentPoints = current.TotalSpentPoints;
        incoming.AcquiredTalents = current.AcquiredTalents;
        incoming.LegacyCategoryTalents = current.LegacyCategoryTalents;
        incoming.PointsRecalculationVersion = Math.Max(incoming.PointsRecalculationVersion,
            current.PointsRecalculationVersion);
    }
}
