using System;
using System.Collections.Generic;
#nullable enable

namespace ReconstructNodesServer.Library.Models
{
    public sealed class ApiMessage
    {
        public string Key { get; }
        public IReadOnlyDictionary<string, string> Arguments { get; }

        public ApiMessage(string key, IReadOnlyDictionary<string, string>? arguments = null)
        {
            Key = key;
            Arguments = arguments ?? new Dictionary<string, string>();
        }

        public static implicit operator ApiMessage(string key) => new ApiMessage(key);
    }

    public interface IApiBusinessError
    {
        ApiMessage ApiMessage { get; }
    }

    public sealed class BusinessArgumentException : ArgumentException, IApiBusinessError
    {
        public ApiMessage ApiMessage { get; }
        public BusinessArgumentException(ApiMessage message) : base(message.Key) => ApiMessage = message;
    }

    public sealed class BusinessOperationException : InvalidOperationException, IApiBusinessError
    {
        public ApiMessage ApiMessage { get; }
        public BusinessOperationException(ApiMessage message) : base(message.Key) => ApiMessage = message;
    }

    public sealed class BusinessFormatException : FormatException, IApiBusinessError
    {
        public ApiMessage ApiMessage { get; }
        public BusinessFormatException(ApiMessage message) : base(message.Key) => ApiMessage = message;
    }
}
