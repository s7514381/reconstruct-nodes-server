
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class ModifierService : _ModelService<Modifier, ModifierModel>
    {
        public ModifierService(BaseServiceArgument argument) : base(argument) 
        {
        }


    }
}
