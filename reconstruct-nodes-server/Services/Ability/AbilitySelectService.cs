
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using System.Diagnostics;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Utility;

namespace ReconstructNodesServer.Services
{
    public class AbilitySelectService : _ModelService<AbilitySelect, AbilitySelectModel>
    {
        private ManyToManyService _manyService => _lazy.ManyToManyService.Value;

        public AbilitySelectService(BaseServiceArgument argument) : base(argument) { }

        protected override async Task SetModel(AbilitySelectModel viewModel)
        {
            await base.SetModel(viewModel);

            viewModel.AbilityCategoryList = viewModel.AbilityCategoryArray.GetEnumListByString<AbilityCategory>();
        }

        protected override async Task ModelSave(AbilitySelectModel viewModel)
        {
            viewModel.AbilityCategoryArray = string.Join(",", viewModel.AbilityCategoryList.Select(x => ((int)x).ToString()));

            await base.ModelSave(viewModel);
        }
    }
}
