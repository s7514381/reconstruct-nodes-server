using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Utility;

internal static partial class Fixture
{
    private static readonly ConcurrentDictionary<string, int> ItemCorrelations = new();

    private static async Task ItemCommandRegressions(HttpClient http, DapperRepository repository,
        Guid playerId, List<string> checks)
    {
        async Task<int> Adjust(string key, int delta, string category = "currency")
        {
            var result = await PostBackground(http, new ItemDeltaRequest { ItemKey = key, Delta = delta, Category = category });
            Require(ItemCorrelations.TryAdd(result.Sample.RequestId, 200), "unique item correlation");
            return result.Data!.GetValue<int>();
        }
        async Task Reject(string key, int delta, string message)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "GameProgress/BackgroundSync")
            {
                Content = JsonContent.Create(BackgroundBatch(new ItemDeltaRequest { ItemKey = key, Delta = delta })),
            };
            request.Headers.Add("Encode", Encode.New());
            using var response = await http.SendAsync(request);
            JsonNode value = JsonNode.Parse(await response.Content.ReadAsByteArrayAsync())!;
            Require(response.StatusCode == HttpStatusCode.BadRequest &&
                value["Success"]!.GetValue<bool>() == false &&
                value["MessageKey"]!.GetValue<string>() == message, "item rejection contract");
            string id = response.Headers.GetValues("X-Request-Timing-Id").Single();
            Require(ItemCorrelations.TryAdd(id, 400), "unique rejected item correlation");
        }
        await Reject("missing", -1, ApiMessageKeys.ItemInsufficient);
        checks.Add("Missing item decrement rejected.");
        Require(await Adjust("pilot", 2, "\u2003\t") == 2, "new amount");
        Require(await Adjust("pilot", 1, "refine") == 3, "existing increment");
        Require(await repository.QueryFirstAsync<string>(
            "SELECT Category FROM Item WHERE CreateUserId=@PlayerId AND ItemKey='pilot' AND Status != -1",
            new { PlayerId = playerId }) == "refine", "unicode blank category replaced");
        checks.Add("Unicode blank category normalized and replaced.");
        await Reject("pilot", -4, ApiMessageKeys.ItemInsufficient);
        checks.Add("Insufficient decrement rejected.");
        Require(await Adjust("pilot", -3) == 0, "zero amount");
        Require(await Adjust("pilot", 1) == 1, "recreate tombstoned item");
        checks.Add("Zero amount and tombstone recreation passed.");
        Require(await Adjust("overflow", int.MaxValue) == int.MaxValue, "maximum amount");
        await Reject("overflow", 1, ApiMessageKeys.ErrorUnexpected);
        Require(await Adjust("overflow", -1) == int.MaxValue - 1, "overflow rollback");
        checks.Add("Int overflow rejected and rolled back.");
        List<Task<int>> increments = Enumerable.Range(0, 10).Select(_ => Adjust("pilot", 1)).ToList();
        int[] values = await Task.WhenAll(increments);
        Require(values.Order().SequenceEqual(Enumerable.Range(2, 10)), "concurrent increments preserved");
        checks.Add("Ten List<Task> increments preserved exactly.");
        using (var transaction = repository.OpenTransaction())
        {
            var argument = CreateServiceArgument(repository, playerId);
            Require(await new ItemService(argument).AdjustAmount("pilot", -1) == 10, "composed adjustment");
            transaction.Rollback();
        }
        Require(await Adjust("pilot", -1) == 10, "outer transaction rollback");
        checks.Add("Composed AdjustAmount rolled back with outer transaction.");
    }

    private static ReconstructNodesServer.Argument.BaseServiceArgument CreateServiceArgument(
        DapperRepository repository, Guid playerId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SmtpConfig:Password"] = "unused-fixture" }).Build();
        var constructor = typeof(ReconstructNodesServer.Argument.LazyServiceArgument).GetConstructors().Single();
        var unused = typeof(Fixture).GetMethod(nameof(UnusedDependency),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        object?[] dependencies = constructor.GetParameters().Select(p =>
            unused.MakeGenericMethod(p.ParameterType.GetGenericArguments()[0]).Invoke(null, null)).ToArray();
        var lazy = (ReconstructNodesServer.Argument.LazyServiceArgument)constructor.Invoke(dependencies);
        var argument = new ReconstructNodesServer.Argument.BaseServiceArgument(lazy, repository, configuration);
        lazy.MailSender = new Lazy<MailSender>(() => new MailSender(configuration));
        lazy.PlayerService = new Lazy<PlayerService>(() => new PlayerService(argument)
            { Player = new ReconstructNodesServer.Library.DBModel.Player { Id = playerId } });
        lazy.WeaponService = new Lazy<WeaponService>(() => new WeaponService(argument));
        lazy.GoldService = new Lazy<GoldService>(() => new GoldService(argument));
        return argument;
    }

    private static Lazy<T> UnusedDependency<T>() => new(() => throw new InvalidOperationException("Unused dependency"));
}
