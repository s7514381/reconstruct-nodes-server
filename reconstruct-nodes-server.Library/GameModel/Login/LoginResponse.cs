using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using System;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class LoginResponse
    {
        public Guid? Token { get; set; }
        public LoginResponseType? Type { get; set; }
        public Guid? EmailVerifyId { get; set; }
    }

    public enum LoginResponseType 
    { 
        驗證信箱, 驗證碼錯誤, 裝置變更, 重複綁定, 驗證碼已過期, 取得驗證碼, 失敗
    }
}
