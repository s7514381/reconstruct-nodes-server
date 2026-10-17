using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

public partial class BackgroundSyncCommand(BaseServiceArgument argument) : BaseService(argument)
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
        if (request.Kills == null || request.Items == null || request.WeaponAcquisitions == null ||
            (request.WaveStart != null && request.WaveStart.RequestId == Guid.Empty) ||
            request.Kills.Any(k => k == null || k.EnemyIndices == null || k.EnemyIndices.Count == 0) ||
            request.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.ItemKey) || i.Delta == 0) ||
            request.WeaponAcquisitions.Any(w => w == null || w.RequestId == Guid.Empty))
        { throw new BusinessArgumentException(ApiMessageKeys.RequestInvalid); }
        Guid[] children = request.WeaponAcquisitions.Select(w => w.RequestId)
            .Concat(request.WaveStart == null ? Array.Empty<Guid>() : new[] { request.WaveStart.RequestId }).ToArray();
        if (children.Contains(request.RequestId) || children.Distinct().Count() != children.Length)
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
    }
}
