using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Broadcast;

public static class BroadcastKinds
{
    public const string Announcement = "announcement";
    public const string Maintenance = "maintenance";

    public static bool IsValid(string? kind) => kind is Announcement or Maintenance;
}

/// <summary>寫入 RTDB 的單則廣播；欄位名稱即前端讀取契約（snake_case）。時間為 UTC Unix 秒，0 代表不限。</summary>
public sealed class BroadcastItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; set; } = BroadcastKinds.Announcement;
    /// <summary>語系碼（zh_TW／en）對應文字。</summary>
    [JsonPropertyName("title")] public Dictionary<string, string> Title { get; set; } = new();
    [JsonPropertyName("body")] public Dictionary<string, string> Body { get; set; } = new();
    [JsonPropertyName("starts_at")] public long StartsAt { get; set; }
    [JsonPropertyName("ends_at")] public long EndsAt { get; set; }
    [JsonPropertyName("published_at")] public long PublishedAt { get; set; }
}

public sealed class BroadcastPublishRequest
{
    public string? Id { get; set; }
    public string? Kind { get; set; }
    public Dictionary<string, string>? Title { get; set; }
    public Dictionary<string, string>? Body { get; set; }
    public long StartsAt { get; set; }
    public long EndsAt { get; set; }
}

public sealed class BroadcastRemoveRequest
{
    public string? Id { get; set; }
}

/// <summary>Firebase 憑證或網址未設定／無法連線。</summary>
public sealed class BroadcastUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
