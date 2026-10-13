using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Library.Models.Enum;
using ReconstructNodesServer.Utility;
using ReconstructNodesServer.Filter;
using Microsoft.Extensions.Caching.Memory;

namespace ReconstructNodesServer.Controllers
{
    public class LoginController(
        BaseApiArgument argument,
        PlayerService playerService,
        IMemoryCache cache
        ) : BaseApiController(argument)
    {
        [HttpPost]
        [Route("/[controller]")]
        public async Task<IActionResult> Index(LoginRequest model)
        {
            if (model.ClientId == null
                || !model.Type.HasValue) { return BadRequest(BackendApiResult<object>.Create(400, ApiMessageKeys.RequestInvalid)); }

            // Google 必須由後端驗證 ID Token 後取得 sub；客戶端傳入的 GoogleId 不可信，改由 GoogleAuth/Login 處理
            if (model.Type == ELoginType.Google) { return BadRequest(BackendApiResult<object>.Create(400, ApiMessageKeys.AuthGoogleRequired)); }

            ResultModel<LoginResponse> result = new();
            result.Data = new();

            await TransactionFunc(async () =>
            {
                // 訪客由 PlayerService 統一查找或建立（與 DeviceAuth 共用），其餘登入方式維持原流程
                Player? player = model.Type == ELoginType.訪客
                    ? await playerService.FindOrCreateGuest(model.ClientId)
                    : await playerService.GetDataByLogin(model);
                if (player == null)
                {
                    //如果沒有的話要建帳號
                    player = new Player()
                    {
                        Name = $"",
                        Token = Guid.NewGuid(),
                        ClientId = model.ClientId,
                    };

                    switch (model.Type)
                    {
                        case ELoginType.Google:
                            //這邊驗證是做在前端
                            player.GoogleId = model.GoogleId;
                            break;
                        case ELoginType.Email:
                            (player, result) = await _lazy.EmailVerifyService.Value.LoginEmailVerify(player, model.EmailVerifyId);
                            if (!result.IsSuccess) { goto Final; }
                            break;
                    }

                    await playerService.InsertAsync(player);
                }
                else
                {
                    //驗證裝置
                    if (player.ClientId != model.ClientId)
                    {
                        //如果有帳號，但是ClientId(unity的playerprefs)不一致，代表換裝置或是重裝遊戲
                        //回到前端警示使用者是否要捨棄掉目前帳號改成之前綁定的
                        if (model.ClientIdChangeConfirm == true)
                        {
                            //換裝置需要重新驗證
                            bool canChange = false;

                            switch (model.Type)
                            {
                                case ELoginType.Email:
                                    (player, result) = await _lazy.EmailVerifyService.Value.LoginEmailVerify(player, model.EmailVerifyId);
                                    if (!result.IsSuccess) { goto Final; }
                                    else
                                    {
                                        canChange = true;
                                    }
                                    break;
                            }
                            if (canChange) { player.ClientId = model.ClientId; }
                        }
                        else { result.Data.Type = LoginResponseType.裝置變更; goto Final; }
                    }
                }

            Final:
                if (player != null)
                {
                    player.LoginResponseType = result.Data.Type;

                    if (!result.Data.Type.HasValue)
                    {
                        //每次登入成功重置Token，並且其他裝置不可使用
                        if (player.Token.HasValue) { cache.Remove(player.Token); }
                        player.Token = Guid.NewGuid();

                        result.Data.Token = player.Token;
                        player.LastLoginDate = DateTime.Now;
                        player.Locale = model.Locale;
                        result.IsSuccess = true;
                    }

                    await playerService.UpdateAsync(player);
                }
            });
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> EmailVerification(LoginRequest model)
        {
            ResultModel<LoginResponse> result = new();
            LoginResponse response = result.Data = new();

            await TransactionFunc(async () =>
            {
                if (model.EmailVerifyId.HasValue)
                {
                    if (await _lazy.EmailVerifyService.Value.Verification(model.EmailVerifyId.Value, model.EmailVerificationCode))
                    {
                        await _lazy.EmailVerifyService.Value.VerificationSuccess(model.EmailVerifyId.Value);
                        result.IsSuccess = true;
                    }
                    else
                    {
                        response.Type = LoginResponseType.驗證碼錯誤; goto Final;
                    }
                }
                else
                {
                    if (!BaseFunction.IsValidEmail(model.Email)) { response.Type = LoginResponseType.失敗; goto Final; }

                    EmailVerify emailVerify = new EmailVerify()
                    {
                        Email = model.Email,
                        IsVerification = false,
                        VerificationCode = BaseFunction.GetVerificationCode(),
                        VerificationDeadline = DateTime.Now.AddMinutes(30),
                    };
                    await _lazy.EmailVerifyService.Value.InsertAsync(emailVerify);

                    _ = playerService.SendEmail(emailVerify.Email, emailVerify.VerificationCode);

                    result.IsSuccess = true;
                    response.EmailVerifyId = emailVerify.Id;
                    response.Type = LoginResponseType.取得驗證碼;
                }

            // goto Final 的跳轉目標，標籤後必須接一個陳述式
            Final: { }
            });
            return Json(result);
        }

        

    }
}
