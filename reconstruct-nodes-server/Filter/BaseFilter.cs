using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using System.Text;
using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Filter
{
    public class BaseFilter : ActionFilterAttribute
    {
        public T? GetSession<T>(ActionExecutingContext context, string sessionName)
        {
            context.HttpContext.Session.TryGetValue(sessionName, out byte[]? sessionBytes);
            if (sessionBytes == null) { return default; }

            var jsonSession = Encoding.UTF8.GetString(sessionBytes);
            T? sessionModel = JsonConvert.DeserializeObject<T>(jsonSession);

            return sessionModel;
        }

    }
}
