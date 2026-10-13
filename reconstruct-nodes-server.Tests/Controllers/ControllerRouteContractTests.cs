using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ReconstructNodesServer.Controllers;

namespace ReconstructNodesServer.Tests.Controllers
{
    /// <summary>
    /// 以真正的 endpoint routing（UseRouting + MapControllers）驗證 Controller action 的 URL 映射。
    /// 管線在路由比對後即中止，不執行 action；不啟動 TestHost、不載入 Program.cs、不需要 DB 或正式 credentials。
    /// 改名造成的 breaking URL：新 URL 必須對應到 action，舊 URL 不得再對應任何 action（不提供 alias／redirect）。
    /// </summary>
    [TestClass]
    public class ControllerRouteContractTests
    {
        private const string EndpointItemKey = "matched-endpoint";

        private static readonly (RequestDelegate Pipeline, IServiceProvider Services) Routing = BuildRouting();

        private static (RequestDelegate Pipeline, IServiceProvider Services) BuildRouting()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers().AddApplicationPart(typeof(WeaponController).Assembly);
            var provider = builder.Build().Services;

            var app = new ApplicationBuilder(provider);
            app.UseRouting();
            // 路由比對後即停止，不呼叫後續 middleware，因此 action 不會被執行。
            app.Use((HttpContext context, RequestDelegate next) =>
            {
                context.Items[EndpointItemKey] = context.GetEndpoint();
                return Task.CompletedTask;
            });
            app.UseEndpoints(endpoints => endpoints.MapControllers());
            return (app.Build(), provider);
        }

        private static async Task<Endpoint?> MatchAsync(string method, string path)
        {
            var context = new DefaultHttpContext { RequestServices = Routing.Services };
            context.Request.Method = method;
            context.Request.Path = path;
            await Routing.Pipeline(context);
            return context.Items[EndpointItemKey] as Endpoint;
        }

        private static string PathOf(Type controllerType, string actionName) =>
            $"/{controllerType.Name.Replace("Controller", string.Empty)}/{actionName}";

        [TestMethod]
        [DataRow(typeof(WeaponController), "GetInstanceList")]
        [DataRow(typeof(WeaponController), "Save")]
        [DataRow(typeof(WeaponController), "Delete")]
        [DataRow(typeof(TalentController), "GetSnapshot")]
        [DataRow(typeof(TalentController), "Save")]
        [DataRow(typeof(ItemController), "GetInStockList")]
        [DataRow(typeof(ItemController), "AdjustAmount")]
        [DataRow(typeof(ItemController), "Delete")]
        [DataRow(typeof(GameProgressController), "Get")]
        [DataRow(typeof(GameProgressController), "Save")]
        [DataRow(typeof(CharacterController), "GetList")]
        [DataRow(typeof(CharacterController), "Get")]
        [DataRow(typeof(CharacterController), "Save")]
        [DataRow(typeof(ShopController), "GetCatalog")]
        [DataRow(typeof(ShopController), "GetGold")]
        [DataRow(typeof(ShopController), "Draw")]
        [DataRow(typeof(EconomyController), "StartWave")]
        [DataRow(typeof(EconomyController), "ReportKill")]
        [DataRow(typeof(EconomyController), "PurchaseSkill")]
        [DataRow(typeof(AchievementProgressController), "Get")]
        [DataRow(typeof(AchievementProgressController), "Save")]
        [DataRow(typeof(ManagerController), "GetSession")]
        public async Task NewRoute_Post_MapsToExpectedAction(Type controllerType, string actionName)
        {
            string path = PathOf(controllerType, actionName);

            Endpoint? endpoint = await MatchAsync(HttpMethods.Post, path);

            Assert.IsNotNull(endpoint, $"POST {path} 必須對應到 action。");
            var descriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            Assert.IsNotNull(descriptor, $"POST {path} 的 endpoint 必須帶有 MVC ActionDescriptor。");
            Assert.AreEqual(controllerType, descriptor.ControllerTypeInfo.AsType());
            Assert.AreEqual(actionName, descriptor.ActionName);

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            Assert.IsNotNull(methods, $"POST {path} 必須限制 HTTP method。");
            CollectionAssert.AreEqual(new[] { HttpMethods.Post }, methods.ToArray(), $"{path} 必須只接受 POST。");

            Endpoint? getEndpoint = await MatchAsync(HttpMethods.Get, path);
            Assert.IsNull(getEndpoint?.Metadata.GetMetadata<ControllerActionDescriptor>(),
                $"GET {path} 不應對應 MVC action。");
        }

        [TestMethod]
        [DataRow(typeof(WeaponController), "GetWeaponList")]
        [DataRow(typeof(WeaponController), "SaveWeapon")]
        [DataRow(typeof(WeaponController), "DeleteWeapon")]
        [DataRow(typeof(TalentController), "GetTalent")]
        [DataRow(typeof(TalentController), "SaveTalent")]
        [DataRow(typeof(ItemController), "GetItemList")]
        [DataRow(typeof(ItemController), "AddItem")]
        [DataRow(typeof(ItemController), "DeleteItem")]
        [DataRow(typeof(GameProgressController), "GetProgress")]
        [DataRow(typeof(GameProgressController), "SaveProgress")]
        [DataRow(typeof(AchievementProgressController), "GetProgress")]
        [DataRow(typeof(AchievementProgressController), "SaveProgress")]
        [DataRow(typeof(ManagerController), "GetManagerSession")]
        public async Task OldRoute_Post_IsNoLongerMapped(Type controllerType, string oldActionName)
        {
            string path = PathOf(controllerType, oldActionName);

            Endpoint? endpoint = await MatchAsync(HttpMethods.Post, path);

            Assert.IsNull(endpoint, $"舊 URL POST {path} 不應再對應任何 action（breaking change，無 alias）。");
        }
    }
}
