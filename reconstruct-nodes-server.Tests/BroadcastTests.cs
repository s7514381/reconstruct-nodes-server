using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ReconstructNodesServer.Broadcast;
using ReconstructNodesServer.Controllers;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Tests;

/// <summary>廣播驗證、容量／過期規則與管理端點授權；以記憶體 store 取代 Firebase，不連網。</summary>
[TestClass]
public class BroadcastTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static long NowUnix => Now.ToUnixTimeSeconds();

    private sealed class MemoryStore : IBroadcastStore
    {
        public Dictionary<string, BroadcastItem> Items { get; } = new();
        public bool Unavailable { get; set; }

        public Task<IReadOnlyList<BroadcastItem>> ListAsync(CancellationToken cancellationToken)
        {
            Guard();
            return Task.FromResult<IReadOnlyList<BroadcastItem>>([.. Items.Values]);
        }

        public Task PutAsync(BroadcastItem item, CancellationToken cancellationToken)
        {
            Guard();
            Items[item.Id] = item;
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
        {
            Guard();
            return Task.FromResult(Items.Remove(id));
        }

        private void Guard()
        {
            if (Unavailable) { throw new BroadcastUnavailableException("down"); }
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static BroadcastPublishRequest Request(string? id = null, string kind = BroadcastKinds.Maintenance) => new()
    {
        Id = id,
        Kind = kind,
        Title = new() { ["zh_TW"] = "停機維護", ["en"] = "Maintenance" },
        Body = new() { ["zh_TW"] = "今晚維護", ["en"] = "Tonight" },
        StartsAt = NowUnix,
        EndsAt = NowUnix + 3600,
    };

    private static BroadcastController Controller(MemoryStore store, string adminKey, string? headerKey)
    {
        var options = new FirebaseBroadcastOptions { AdminKey = adminKey };
        var controller = new BroadcastController(new BroadcastService(store, new FixedTime()), options,
            NullLogger<BroadcastController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        if (headerKey != null) { controller.Request.Headers[BroadcastController.AdminKeyHeader] = headerKey; }
        return controller;
    }

    private static int Status(IActionResult result) => ((ObjectResult)result).StatusCode ?? 200;

    [TestMethod]
    public void Validator_AcceptsValidRequestAndGeneratesSafeId()
    {
        string? error = BroadcastValidator.TryBuild(Request(), NowUnix, out BroadcastItem item);

        Assert.IsNull(error);
        Assert.IsTrue(BroadcastValidator.IsValidId(item.Id));
        Assert.AreEqual(NowUnix, item.PublishedAt);
        Assert.AreEqual("停機維護", item.Title["zh_TW"]);
    }

    [TestMethod]
    [DataRow("a/b", "id")]
    [DataRow("a.b", "id")]
    [DataRow("a b", "id")]
    public void Validator_RejectsUnsafeFirebaseKeys(string id, string field)
    {
        Assert.AreEqual(field, BroadcastValidator.TryBuild(Request(id), NowUnix, out _));
    }

    [TestMethod]
    public void Validator_RejectsBadKindLocaleLengthAndTimeWindow()
    {
        Assert.AreEqual("kind", BroadcastValidator.TryBuild(Request(kind: "x"), NowUnix, out _));

        BroadcastPublishRequest unknownLocale = Request();
        unknownLocale.Title!["fr"] = "x";
        Assert.AreEqual("title", BroadcastValidator.TryBuild(unknownLocale, NowUnix, out _));

        BroadcastPublishRequest longBody = Request();
        longBody.Body!["en"] = new string('a', BroadcastValidator.MaxBodyLength + 1);
        Assert.AreEqual("body", BroadcastValidator.TryBuild(longBody, NowUnix, out _));

        BroadcastPublishRequest noTitle = Request();
        noTitle.Title = new() { ["en"] = "  " };
        Assert.AreEqual("title", BroadcastValidator.TryBuild(noTitle, NowUnix, out _));

        BroadcastPublishRequest reversed = Request();
        reversed.EndsAt = reversed.StartsAt - 1;
        Assert.AreEqual("time", BroadcastValidator.TryBuild(reversed, NowUnix, out _));

        BroadcastPublishRequest alreadyEnded = Request();
        alreadyEnded.StartsAt = NowUnix - 7200;
        alreadyEnded.EndsAt = NowUnix - 3600;
        Assert.AreEqual("time", BroadcastValidator.TryBuild(alreadyEnded, NowUnix, out _));
    }

    [TestMethod]
    public async Task Publish_PrunesExpiredAndEnforcesActiveLimit()
    {
        var store = new MemoryStore();
        store.Items["old"] = new BroadcastItem { Id = "old", EndsAt = NowUnix - 1 };
        for (int i = 0; i < BroadcastValidator.MaxActiveItems; i++)
        {
            store.Items[$"k{i}"] = new BroadcastItem { Id = $"k{i}", EndsAt = 0 };
        }
        var service = new BroadcastService(store, new FixedTime());

        var full = await service.PublishAsync(Request("new"), CancellationToken.None);
        Assert.AreEqual(BroadcastOutcome.LimitReached, full.Outcome);
        Assert.IsFalse(store.Items.ContainsKey("old"), "過期項目應順帶清除");

        var replace = await service.PublishAsync(Request("k0"), CancellationToken.None);
        Assert.AreEqual(BroadcastOutcome.Ok, replace.Outcome, "覆寫同 ID 不佔新名額");
    }

    [TestMethod]
    public async Task Remove_ReportsMissingAndInvalidIds()
    {
        var service = new BroadcastService(new MemoryStore(), new FixedTime());

        Assert.AreEqual(BroadcastOutcome.NotFound, (await service.RemoveAsync("nope", CancellationToken.None)).Outcome);
        Assert.AreEqual(BroadcastOutcome.Invalid, (await service.RemoveAsync("../x", CancellationToken.None)).Outcome);
    }

    [TestMethod]
    public async Task Controller_RejectsMissingWrongOrUnconfiguredAdminKey()
    {
        Assert.AreEqual(401, Status(await Controller(new MemoryStore(), "secret", null).List(CancellationToken.None)));
        Assert.AreEqual(401, Status(await Controller(new MemoryStore(), "secret", "wrong").List(CancellationToken.None)));
        Assert.AreEqual(401, Status(await Controller(new MemoryStore(), "", "").List(CancellationToken.None)),
            "未設定 AdminKey 時連空字串都不能通過");
    }

    [TestMethod]
    public async Task Controller_PublishListRemoveRoundTripAndStoreFailure()
    {
        var store = new MemoryStore();
        BroadcastController admin = Controller(store, "secret", "secret");

        Assert.AreEqual(200, Status(await admin.Publish(Request("m1"), CancellationToken.None)));
        Assert.IsTrue(store.Items.ContainsKey("m1"));
        Assert.AreEqual(400, Status(await admin.Publish(Request("bad id"), CancellationToken.None)));
        Assert.AreEqual(200, Status(await admin.Remove(new BroadcastRemoveRequest { Id = "m1" }, CancellationToken.None)));
        Assert.AreEqual(404, Status(await admin.Remove(new BroadcastRemoveRequest { Id = "m1" }, CancellationToken.None)));

        store.Unavailable = true;
        Assert.AreEqual(503, Status(await admin.List(CancellationToken.None)));
    }

    [TestMethod]
    public void Item_SerializesToClientContractNames()
    {
        BroadcastValidator.TryBuild(Request("m1"), NowUnix, out BroadcastItem item);
        string json = System.Text.Json.JsonSerializer.Serialize(item);

        foreach (string name in new[] { "\"id\"", "\"kind\"", "\"title\"", "\"body\"", "\"starts_at\"", "\"ends_at\"", "\"published_at\"" })
        {
            StringAssert.Contains(json, name);
        }
    }
}
