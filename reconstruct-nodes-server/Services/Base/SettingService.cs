using ReconstructNodesServer.Library.Models;
using System.Diagnostics;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public abstract class _SettingService<DBModel, ViewModel> : _ModelService<DBModel, ViewModel>
        where DBModel : ModelBase, new()
        where ViewModel : DBModel, new()
    {
        public _SettingService(BaseServiceArgument argument) : base(argument) { }

        public override string SqlDataList(string subWhere = "")
        {
            return Sql(subWhere: subWhere);
        }

    }
}