
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class EquipmentService : _ModelService<Equipment, EquipmentModel>
    {
        private ModifierService _modifierService => _lazy.ModifierService.Value;
        private ManyToManyService _manyToManyService => _lazy.ManyToManyService.Value;

        public EquipmentService(BaseServiceArgument argument) : base(argument) { }

        protected override async Task SetModel(EquipmentModel viewModel)
        {
            await base.SetModel(viewModel);

            viewModel.ModifierList = (await _modifierService.GetMTMModelList(viewModel.Id)).ToList();
        }

        protected override async Task ModelSave(EquipmentModel viewModel)
        {
            await base.ModelSave(viewModel);

            List<Task> tasks = new() { 
                Task.Run(async () => { await _modifierService.Save(viewModel.ModifierList); }),
                Task.Run(async () => { await _manyToManyService.SaveById(viewModel, viewModel.ModifierList); }),
            };
            await Task.WhenAll(tasks);
        }

        protected override async Task ModelDelete(EquipmentModel viewModel)
        {
            await _modifierService.DeleteModel(viewModel.ModifierList);
        }

    }
}
