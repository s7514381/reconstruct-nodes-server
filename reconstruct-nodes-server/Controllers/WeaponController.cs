using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class WeaponController(BaseApiArgument argument, WeaponService service, WeaponCommandService commands) : AuthorizeApiController(argument)
    {
        [HttpPost]
        public virtual Task<IActionResult> GetInstanceList() =>
            TransactionResultAsync<IEnumerable<WeaponInstanceModel>>(() => service.GetInstanceList(), ApiMessageKeys.ErrorUnexpected);

        [HttpPost]
        public Task<IActionResult> Acquire(WeaponAcquireRequest request) =>
            TransactionResultAsync(() => commands.Acquire(request), ApiMessageKeys.WeaponSaveFailed);

        [HttpPost]
        public Task<IActionResult> Refine(WeaponRefineRequest request) =>
            TransactionResultAsync(() => commands.Refine(request), ApiMessageKeys.WeaponSaveFailed);

        [HttpPost]
        public Task<IActionResult> Disassemble(WeaponDisassembleRequest request) =>
            TransactionResultAsync(() => commands.Disassemble(request), ApiMessageKeys.WeaponDeleteFailed);
    }
}
