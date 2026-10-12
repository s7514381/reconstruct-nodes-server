using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
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
        public async Task<string?> GetCompressCompleteModel()
        {
            if (!service._playerId.HasValue) { return null; }

            PlayerCompleteModel? model = await service.GetCompleteModel();
            string originalString = JsonConvert.SerializeObject(model);
            string compressString = StringCompressor.Compress(originalString);

            return compressString;
        }

        [HttpPost]
        public async Task<IActionResult> Bind(LoginRequest model)
        {
            if (model.ClientId == null
                || !model.Type.HasValue) { return BadRequest(); }

            // 拒絕訪客→Google 的綁定遷移：Google 身分必須經 GoogleAuth/Login 驗證，且不得覆寫既有訪客進度
            if (model.Type == ELoginType.Google) { return BadRequest("Google 登入請改用 GoogleAuth/Login，不支援綁定遷移"); }

            ResultModel<LoginResponse> result = new();
            LoginResponse response = result.Data = new();

            Player? player = await service.GetData(service._playerId ?? throw new InvalidOperationException("未取得登入者 Id。"));
            if (player == null) { return BadRequest(); }

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
