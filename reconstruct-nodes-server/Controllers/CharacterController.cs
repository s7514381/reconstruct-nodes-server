using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class CharacterController(BaseApiArgument argument, CharacterService service) : AuthorizeApiController(argument)
    {
        [HttpPost]
        public virtual Task<IActionResult> GetList() =>
            TransactionResultAsync<List<CharacterModel>>(() => service.GetList(), ApiMessageKeys.CharacterReadFailed);

        [HttpPost]
        public virtual Task<IActionResult> Get(Guid id) =>
            TransactionResultAsync<CharacterModel?>(() => service.Get(id), ApiMessageKeys.CharacterReadFailed, ApiMessageKeys.CharacterMissing);

        [HttpPost]
        public virtual async Task<IActionResult> Save(CharacterModel model)
        {
            (bool success, ApiMessage? message) = await TransactionFunc(() => service.Save(model));
            if (!success) { return StatusCode(400, BackendApiResult<string>.Create(400, message ?? ApiMessageKeys.CharacterSaveFailed)); }
            return Ok(BackendApiResult<Guid?>.Create(200, string.Empty, model.Id));
        }
    }
}
