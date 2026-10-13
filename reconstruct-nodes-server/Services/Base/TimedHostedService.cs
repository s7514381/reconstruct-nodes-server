using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Filter;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Host
{
    public class TimedHostedService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                //foreach (var item in AuthorizeFilter.playerDic)
                //{
                //    if (item.Value?.ExpireTime < DateTime.Now)
                //    {
                //        AuthorizeFilter.playerDic.TryRemove(item.Key, out PlayerValidateModel? _);
                //    }
                //}

                //await Task.Delay(TimeSpan.FromSeconds(600), stoppingToken);
            }
        }
    }

}

