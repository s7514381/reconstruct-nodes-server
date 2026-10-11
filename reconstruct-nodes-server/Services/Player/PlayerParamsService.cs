
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class PlayerParamsService : _ModelService<PlayerParams, PlayerParamsModel>
    {
        public PlayerParamsService(BaseServiceArgument argument) : base(argument) 
        {
        }


    }
}
