using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
	public class GameCollectionController : BaseServiceApiController<GameCollection, GameCollectionModel>
    {
        public GameCollectionController(BaseApiArgument argument,
            GameCollectionService service) : base(argument, service)
		{
            //_service.Save() 底層的service這樣用
        }

	}
}
