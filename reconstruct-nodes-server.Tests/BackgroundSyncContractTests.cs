using System.Text.Json;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Tests;

/// <summary>BackgroundSync 線上格式：前端送出的 snake_case JSON 與精簡後的回應欄位；不連資料庫。</summary>
[TestClass]
public class BackgroundSyncContractTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = null };

    [TestMethod]
    public void Request_BindsGroupedKillsSingleWaveStartAndSnakeCaseItems()
    {
        Guid session = Guid.NewGuid();
        string json = $$"""
            {"request_id":"{{Guid.NewGuid()}}",
             "wave_start":{"request_id":"{{session}}","wave":3,"difficulty":1},
             "kills":[{"session_id":"{{session}}","enemy_indices":[0,2,5]}],
             "items":[{"item_key":"iron_ingot","delta":-2,"category":"refine"}],
             "weapon_acquisitions":[]}
            """;
        var request = JsonSerializer.Deserialize<BackgroundSyncRequest>(json, Options)!;
        Assert.AreEqual(session, request.WaveStart!.RequestId);
        Assert.AreEqual(3, request.WaveStart.Wave);
        Assert.AreEqual(1, request.Kills.Count);
        CollectionAssert.AreEqual(new[] { 0, 2, 5 }, request.Kills[0].EnemyIndices);
        Assert.AreEqual("iron_ingot", request.Items[0].ItemKey);
        Assert.AreEqual(-2, request.Items[0].Delta);
        Assert.AreEqual("refine", request.Items[0].Category);
    }

    [TestMethod]
    public void Request_NullWaveStartAndEmptyCollectionsMeanNoWork()
    {
        string json = $$"""{"request_id":"{{Guid.NewGuid()}}","wave_start":null,"kills":[],"items":[],"weapon_acquisitions":[]}""";
        var request = JsonSerializer.Deserialize<BackgroundSyncRequest>(json, Options)!;
        Assert.IsNull(request.WaveStart);
        Assert.AreEqual(0, request.Kills.Count + request.Items.Count + request.WeaponAcquisitions.Count);
    }

    [TestMethod]
    public void Response_CarriesOnlyGoldSessionProgressTalentAndAcquisitions()
    {
        var response = new BackgroundSyncResponse { RequestId = Guid.NewGuid(), Gold = 5 };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, Options));
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        CollectionAssert.AreEqual(
            new[] { "acquisitions", "gold", "progress", "request_id", "session", "talent" }, names);
    }
}
