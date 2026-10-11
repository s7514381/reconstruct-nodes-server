using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
	public class HomeController : BaseController
    {
        public HomeController() : base()
		{
        }

		[HttpGet]
		[Route("/")]
		public async Task<string> Get()
		{
            return $"歡迎來到Unity的第一個家";
		}

    }
}
