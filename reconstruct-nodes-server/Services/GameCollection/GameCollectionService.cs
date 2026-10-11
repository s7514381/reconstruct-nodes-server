
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class GameCollectionService : _ModelService<GameCollection, GameCollectionModel>
    {
        public GameCollectionService(BaseServiceArgument argument) : base(argument) { }

    }
}
