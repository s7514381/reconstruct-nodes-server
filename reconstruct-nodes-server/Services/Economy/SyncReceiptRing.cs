using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>
/// BackgroundSync 最近幾筆批次回執，存在單一 PlayerParams 列（economy / sync_receipts），
/// 取代「每秒新增一列 background.sync EconomyRequest」。客戶端同時只有一批在途，
/// 未知結果重試的一定是最近的批次，所以只需保留最近 <see cref="Window"/> 筆。
/// </summary>
internal sealed class SyncReceiptRing
{
    public const string Group = "economy";
    public const string Name = "sync_receipts";
    public const int Window = 2;

    public sealed record Entry(
        [property: JsonPropertyName("i")] string RequestId,
        [property: JsonPropertyName("h")] string PayloadHash,
        [property: JsonPropertyName("r")] string Result,
        // 回執不存庫存快照（會隨庫存變大）；true 表示重放時要補上目前在庫物品。
        [property: JsonPropertyName("s")] bool Stock = false);

    private readonly List<Entry> _entries;

    private SyncReceiptRing(List<Entry> entries) => _entries = entries;

    public static SyncReceiptRing Parse(string? value) => new(string.IsNullOrEmpty(value)
        ? [] : JsonSerializer.Deserialize<List<Entry>>(value) ?? []);

    public static string Hash(string payload) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    /// <summary>找到同 request_id 的回執；意圖不同回 409 衝突，找不到回 null。</summary>
    public Entry? Replay(Guid requestId, string payload)
    {
        Entry? entry = _entries.FirstOrDefault(e => Guid.TryParse(e.RequestId, out var id) && id == requestId);
        if (entry == null) { return null; }
        if (entry.PayloadHash != Hash(payload))
        { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
        return entry;
    }

    /// <summary>新回執加入前要保留的舊項目（JSON 陣列）；新項目由 SQL 在 @Result 算出後附加。</summary>
    public string Retained(Guid requestId) => JsonSerializer.Serialize(_entries
        .Where(e => !(Guid.TryParse(e.RequestId, out var id) && id == requestId))
        .TakeLast(Window - 1).ToList());
}
