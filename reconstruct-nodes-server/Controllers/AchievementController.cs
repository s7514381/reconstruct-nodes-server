using ReconstructNodesServer.Library.Models;
using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
	public class AchievementController(BaseApiArgument argument,
        AchievementDetailService detailService,
            AchievementService service) : BaseServiceApiController<Achievement, AchievementModel>(argument, service)
    {

        [HttpPost]
        public virtual async Task<List<AchievementDetail>> Reward(List<Guid> ids)
        {
            List<AchievementDetail> list = new List<AchievementDetail>();

            if (ids == null || ids.Count == 0) { return list; }

            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                list = await detailService.Reward(ids);
            });
            return list;
        }

    }
}