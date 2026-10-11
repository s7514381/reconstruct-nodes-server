using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Utility;
using System.Diagnostics;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public abstract class _ModelSettingService<DBModel, ViewModel, Setting, SettingModel> : _ModelService<DBModel, ViewModel>
        where DBModel : ModelBase, new()
        where ViewModel : DBModel, new()
        where Setting : ModelBase, new()
        where SettingModel : Setting, new()
    {
        protected _SettingService<Setting, SettingModel> _settingService { get; set; }

        public _ModelSettingService(BaseServiceArgument argument
            , _SettingService<Setting, SettingModel> settingService) : base(argument) 
        {
            _settingService = settingService;
        }

        public override async Task<IEnumerable<ViewModel>> GetModelList()
        {
            List<ViewModel> list = (await base.GetModelList()).ToList();

            //跟Setting做聯集，如果Setting有多資料則需要補上
            IEnumerable<SettingModel> settingList = await _settingService.GetModelList();

            string settingKey = CommonUtility.GetForeignKeyField<DBModel, Setting>();
             
            foreach (var setting in settingList)
            {
                var model = list.FirstOrDefault(x => (Guid?)x.GetFieldValue(settingKey) == setting.Id);
                if (model == null)
                {
                    ViewModel viewModel = setting.MapSetting<ViewModel, Setting>();
                    list.Add(viewModel);
                }
            }
            return list;
        }

    }
}