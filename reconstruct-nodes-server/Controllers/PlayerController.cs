using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Controllers
{
    public class PlayerController(BaseApiArgument argument, PlayerService service)
        : BaseServiceApiController<Player, PlayerModel>(argument, service)
    {
        [HttpPost]
        public override async Task<PlayerModel?> GetModel(RequestModel request)
        {
            if (!service._playerId.HasValue) { return null; }

            return await service.GetModel(service._playerId.Value);
        }

        [HttpPost]
        public async Task<IActionResult> Bind(LoginRequest model)
        {
            if (model.ClientId == null
                || !model.Type.HasValue) { return BadRequest(BackendApiResult<object>.Create(400, ApiMessageKeys.RequestInvalid)); }

            // 拒絕訪客→Google 的綁定遷移：Google 身分必須經 GoogleAuth/Login 驗證，且不得覆寫既有訪客進度
            if (model.Type == ELoginType.Google) { return BadRequest(BackendApiResult<object>.Create(400, ApiMessageKeys.AuthGoogleRequired)); }

            ResultModel<LoginResponse> result = new();
            LoginResponse response = result.Data = new();

            Player? player = await service.GetData(service._playerId ?? throw new InvalidOperationException("未取得登入者 Id。"));
            if (player == null) { return BadRequest(BackendApiResult<object>.Create(400, ApiMessageKeys.AuthInvalid)); }

            switch (model.Type)
            {
                case ELoginType.Google:
                    //這邊驗證是做在前端
                    player.GoogleId = model.GoogleId;
                    break;
                case ELoginType.Email:
                    if (await service.GetDataByEmail(model.Email) != null) { response.Type = LoginResponseType.重複綁定; goto Final; }

                    (player, result) = await _lazy.EmailVerifyService.Value.LoginEmailVerify(player, model.EmailVerifyId);
                    if (!result.IsSuccess) { goto Final; }
                    break;
            }
            player.IsClientLogin = false;
            result.Success(response);

        Final:
            await service.UpdateAsync(player);
            return Json(result);
        }

        [HttpPost]
        public string ServiceNow()
        {
            return DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        }

    }
}
