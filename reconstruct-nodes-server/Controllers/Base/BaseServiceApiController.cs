using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public abstract class BaseServiceApiController<DBModel, ViewModel>(BaseApiArgument argument,
            _ModelService<DBModel, ViewModel> service) : AuthorizeApiController(argument)
        where DBModel : ModelBase, new()
        where ViewModel : DBModel, new()
    {
        [HttpPost]
        public virtual async Task<DBModel?> GetData(RequestModel request)
        {
            if (!request.Id.HasValue) { return null; }

            return await service.GetData(request.Id.Value);
        }

        [HttpPost]
        public virtual async Task<ViewModel?> GetModel(RequestModel request)
        {
            if (!request.Id.HasValue) { return null; }

            return await service.GetModel(request.Id.Value);
        }

        [HttpPost]
        public virtual async Task<bool?> IsExist(RequestModel request) 
        {
            if (!request.Id.HasValue) { return null; }

            return await service.IsExist(request.Id.Value);
        }

        [HttpPost]
        public virtual async Task<IActionResult> Save(ViewModel viewModel) 
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.Save(viewModel);
            });

            if (success) { return Ok(); }
            else { return BadRequest(); }
        }

        [HttpPost]
        public virtual async Task<IActionResult> SaveList(List<ViewModel> list)
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.Save(list);
            });

            if (success) { return Ok(); }
            else { return BadRequest(); }
        }

        [HttpPost]
        public virtual async Task<IActionResult> SaveData(DBModel viewModel)
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.SaveData(viewModel);
            });

            if (success) { return Ok(); }
            else { return BadRequest(); }
        }

        [HttpPost]
        public virtual async Task<IActionResult> SaveDataList(List<DBModel> list)
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.SaveData(list);
            });

            if (success) { return Ok(); }
            else { return BadRequest(); }
        }

        [HttpPost]
        public virtual async Task<IEnumerable<DBModel>?> GetDataList()
        {
            return await service.GetDataList();
        }

        [HttpPost]
        public virtual async Task<IEnumerable<ViewModel>?> GetModelList()
        {
            return await service.GetModelList();
        }

        [HttpPost]
        public virtual async Task<IActionResult> Delete(ViewModel viewModel) 
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.DeleteModel(viewModel);
            });

            if (success) { return Ok(); }
            else { return BadRequest(); }
        }

        [HttpPost]
        public virtual async Task<IActionResult> DeleteList(List<ViewModel> list)
        {
            if (list.Count == 0) { return BadRequest(); }

            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.DeleteModel(list);
            });

            if (success) { return Ok(); }
            else { return BadRequest(); }
        }

#if DEBUG
        [HttpPost]
        public virtual async Task<IActionResult> TestSave()
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.TestSave();
            });

            if (success) { return Ok(); }
            else { return BadRequest(); }
        }

        [HttpPost]
        public virtual async Task<IActionResult> TestSaveList()
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.TestSaveList();
            });

            if (success) { return Ok(); }
            else { return BadRequest(); }
        }

        [HttpPost]
        public virtual async Task<IActionResult> TestDelete()
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.TestDelete();
            });

            if (success) { return Ok(); }
            else { return BadRequest(); }
        }

        [HttpPost]
        public async Task<IActionResult> Seed()
        {
            await service.Seed();
            return Ok();
        }
#endif

    }
}

