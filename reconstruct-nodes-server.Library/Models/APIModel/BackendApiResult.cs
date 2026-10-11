using System;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>對應前端 BackendApiResult 的 { Status, Success, Message, Data } 格式。</summary>
    public class BackendApiResult<T>
    {
        public int Status { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T Data { get; set; }

        public static BackendApiResult<T> Create(int status, string message = "", T data = default)
        {
            return new BackendApiResult<T>
            {
                Status = status,
                Success = status >= 200 && status < 300,
                Message = message,
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
