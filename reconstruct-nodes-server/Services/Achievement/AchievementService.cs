
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class AchievementService(BaseServiceArgument argument) : _ModelService<Achievement, AchievementModel>(argument)
    {

        protected override async Task SetModel(AchievementModel viewModel)
        {
            await base.SetModel(viewModel);

            viewModel.DetailList = await _lazy.AchievementDetailService.Value.GetModelListByParent<Achievement>(viewModel.Id);
        }

        protected override async Task ModelSave(AchievementModel viewModel)
        {
            await base.ModelSave(viewModel);

            await _lazy.AchievementDetailService.Value.UpdateDetail(viewModel, viewModel.DetailList);
        }

    }
}
