using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Filter;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    [ServiceFilter(typeof(AuthorizeFilter))]
    public class AuthorizeApiController : BaseApiController
    {
        protected AuthorizeApiController(BaseApiArgument argument) : base(argument) { }
    }
}



