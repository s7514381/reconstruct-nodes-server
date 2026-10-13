using System;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>系統訊息由前端依 MessageKey 與 MessageArgs 翻譯。</summary>
    public class BackendApiResult<T>
    {
        public int Status { get; set; }
        public bool Success { get; set; }
        public string MessageKey { get; set; } = string.Empty;
        public Dictionary<string, string> MessageArgs { get; set; } = new();
        public T Data { get; set; }

        public static BackendApiResult<T> Create(int status, string messageKey = "", T data = default)
            => Create(status, new ApiMessage(messageKey), data);

        public static BackendApiResult<T> Create(int status, ApiMessage message, T data = default)
        {
            return new BackendApiResult<T>
            {
                Status = status,
                Success = status >= 200 && status < 300,
                MessageKey = message.Key,
                MessageArgs = new Dictionary<string, string>(message.Arguments),
                Data = data,
            };
        }
    }

    /// <summary>登入成功後回傳的 Token 與玩家名稱。</summary>
    public class BackendLoginData
    {
        public BackendLoginData(Guid token, string name)
        {
            Token = token;
            Name = name;
        }

        public Guid Token { get; }
        public string Name { get; }
    }
}
