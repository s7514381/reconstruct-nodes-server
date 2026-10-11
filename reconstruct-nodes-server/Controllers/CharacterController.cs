using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class CharacterController(BaseApiArgument argument,
            CharacterService service) : BaseServiceApiController<Character, CharacterModel>(argument, service)
    {

    }
}
