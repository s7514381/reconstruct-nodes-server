
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class AbilityCardService : _ModelService<AbilityCard, AbilityCardModel>
    {
        public AbilityCardService(BaseServiceArgument argument) : base(argument) { }


    }
}
