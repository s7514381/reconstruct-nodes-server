
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class MapService : _ModelService<Map, MapModel>
    {
        public MapService(BaseServiceArgument argument) : base(argument) { }


    }
}
