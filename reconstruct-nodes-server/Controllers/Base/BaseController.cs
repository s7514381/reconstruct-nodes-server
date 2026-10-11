using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Filter;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class BaseController() : Controller
	{
        /// <summary>
        /// ��e�� Controller �W�١C
        /// </summary>
        public string? CurrentController
        {
            get { return ControllerContext?.RouteData?.Values["controller"]?.ToString(); }
        }

        public string? CurrentControllerPrefix
        {
            get { return CurrentController?.Replace("Controller", ""); }
        }

        /// <summary>
        /// ��e�� Action �W�١C
        /// </summary>
        public string? CurrentAction
        {
            get { return ControllerContext?.RouteData?.Values["action"]?.ToString(); }
        }

    }
}

