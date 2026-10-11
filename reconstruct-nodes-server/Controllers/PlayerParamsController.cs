using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Controllers
{
    public class PlayerParamsController(BaseApiArgument argument, PlayerParamsService service)
        : BaseServiceApiController<PlayerParams, PlayerParamsModel>(argument, service)
    {
       

    }
}
