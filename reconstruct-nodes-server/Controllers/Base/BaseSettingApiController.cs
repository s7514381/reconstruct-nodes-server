using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public abstract class BaseSettingApiController<DBModel, ViewModel> : AuthorizeApiController
        where DBModel : ModelBase, new()
        where ViewModel : DBModel, new()
    {
        protected _ModelService<DBModel, ViewModel> _service;

        protected BaseSettingApiController(BaseApiArgument argument,
            _ModelService<DBModel, ViewModel> service) : base(argument)
        {
            _service = service;
        }

        [HttpPost]
        public virtual async Task<IEnumerable<ViewModel>?> GetModelList()
        {
            return await _service.GetModelList();
        }

#if DEBUG
        [HttpPost]
        public async Task<IActionResult> Seed()
        {
            await _service.Seed();
            return Ok();
        }
#endif

    }
}

