
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using System.Diagnostics;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class AbilityService : _ModelService<Ability, AbilityModel>
    {
        public AbilityService(BaseServiceArgument argument) : base(argument) { }

        protected override async Task SetModel(AbilityModel viewModel) 
        {
            await base.SetModel(viewModel);

            viewModel.AbilityCardList = await _lazy.AbilityCardService.Value.GetModelListByParent<Ability>(viewModel.Id);
        }

        protected override async Task ModelSave(AbilityModel viewModel) 
        {
            await base.ModelSave(viewModel);

            await _lazy.AbilityCardService.Value.UpdateDetail(viewModel, viewModel.AbilityCardList);
        }
    }
}
