using System.Text.RegularExpressions;

namespace ReconstructNodesServer.Broadcast;

/// <summary>廣播請求驗證與正規化；回傳 null 的錯誤代表通過。</summary>
public static partial class BroadcastValidator
{
    public const int MaxTitleLength = 100;
    public const int MaxBodyLength = 1000;
    public const int MaxActiveItems = 20;
    public static readonly string[] Locales = ["zh_TW", "en"];

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string? id) => id != null && IdPattern().IsMatch(id);

    public static string? TryBuild(BroadcastPublishRequest request, long nowUnix, out BroadcastItem item)
    {
        item = new BroadcastItem();
        string id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id.Trim();
        if (!IsValidId(id)) { return "id"; }
        if (!BroadcastKinds.IsValid(request.Kind)) { return "kind"; }

        Dictionary<string, string>? title = Clean(request.Title, MaxTitleLength);
        Dictionary<string, string>? body = Clean(request.Body, MaxBodyLength);
        if (title == null || title.Count == 0) { return "title"; }
        if (body == null) { return "body"; }
        if (request.StartsAt < 0 || request.EndsAt < 0) { return "time"; }
        if (request.EndsAt != 0 && request.EndsAt <= request.StartsAt) { return "time"; }
        if (request.EndsAt != 0 && request.EndsAt <= nowUnix) { return "time"; }

        item = new BroadcastItem
        {
            Id = id,
            Kind = request.Kind!,
            Title = title,
            Body = body,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            PublishedAt = nowUnix,
        };
        return null;
    }

    public static bool IsExpired(BroadcastItem item, long nowUnix) => item.EndsAt != 0 && item.EndsAt <= nowUnix;

    /// <summary>只保留已知語系的非空文字；超長或未知語系回傳 null（整體拒絕）。</summary>
    private static Dictionary<string, string>? Clean(Dictionary<string, string>? source, int maxLength)
    {
        var result = new Dictionary<string, string>();
        if (source == null) { return result; }
        foreach ((string locale, string text) in source)
        {
            if (!Locales.Contains(locale)) { return null; }
            string trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length > maxLength) { return null; }
            if (trimmed.Length > 0) { result[locale] = trimmed; }
        }
        return result;
    }
}
