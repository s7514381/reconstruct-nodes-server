using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
	public class AbilitySelectController : BaseServiceApiController<AbilitySelect, AbilitySelectModel>
    {
        public AbilitySelectController(BaseApiArgument argument,
            AbilitySelectService service) : base(argument, service)
		{
            //_service.Save() 底層的service這樣用
        }

	}
}
