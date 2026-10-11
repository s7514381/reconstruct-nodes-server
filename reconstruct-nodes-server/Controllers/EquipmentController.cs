using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
	public class EquipmentController(BaseApiArgument argument,
            EquipmentService service) : BaseServiceApiController<Equipment, EquipmentModel>(argument, service)
    {


    }
}
