using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Controllers
{
	public class HomeController : BaseController
    {
        public HomeController() : base()
		{
        }

		[HttpGet]
		[Route("/")]
		public BackendApiResult<object> Get()
		{
            return BackendApiResult<object>.Create(200, ApiMessageKeys.HomeWelcome);
		}

    }
}
