using Azure.Data.Tables;
using System.Collections.Concurrent;


namespace ReconstructNodesServer.Services
{
    public class AzureStoreService
    {
        private IServiceScopeFactory _scopeFactory;

        private string connectionString;
        private ConcurrentDictionary<TableClientType, TableClient> tableClientDic = new();

        public AzureStoreService(IConfiguration configuration, IServiceScopeFactory scopeFactory) 
        {
            _scopeFactory = scopeFactory;
            connectionString = configuration["AzureStorage:ConnectionString"]
                ?? throw new InvalidOperationException("AzureStorage:ConnectionString is not configured.");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("AzureStorage:ConnectionString is not configured.");
            }

            TableClientType[] tableClientTypes = (TableClientType[])Enum.GetValues(typeof(TableClientType));
            foreach (TableClientType item in tableClientTypes) 
            {
                TableClient tableClient = new TableClient(connectionString, item.ToString());
                tableClient.CreateIfNotExists();
                tableClientDic.TryAdd(item, tableClient);
            }
        }

        public async ValueTask Transfer<TService>(TableClientType tableClientType, Type type, List<TableEntity> entities, Func<TService, TableEntity, Task> func) where TService : BaseService
        {
            using var scope = _scopeFactory.CreateScope();
            TService? service = scope.ServiceProvider.GetRequiredService(type) as TService ?? null;
            if (service == null) { return; }

            if(!tableClientDic.TryGetValue(tableClientType, out TableClient? tableClient))
                return;

            List<Task> tasks = new List<Task>();
            foreach (var entity in entities)
            {
                tasks.Add(Task.Run(async () =>
                {
                    using Azure.Response resp = await tableClient.UpsertEntityAsync(entity);

                    if (resp?.Status == 204 || resp?.Status == 201)
                    {
                        await func(service, entity);
                    }
                }));
            }
            await Task.WhenAll(tasks);
        }

        public async ValueTask Transfer(TableClientType tableClientType, TableEntity entity, Func<TableEntity, Task>? func = null)
        {
            if (!tableClientDic.TryGetValue(tableClientType, out TableClient? tableClient))
                return;

            using Azure.Response resp = await tableClient.UpsertEntityAsync(entity);

            if (func != null
                && (resp?.Status == 204 || resp?.Status == 201))
            {
                await func(entity);
            }
        }
    }

    public enum TableClientType 
    { 
        Delete,Log
    }

}

