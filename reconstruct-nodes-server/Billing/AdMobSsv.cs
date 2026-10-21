using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReconstructNodesServer.Billing;

public sealed class AdMobSsvOptions
{
    public const string SectionName = "AdMob";
    /// <summary>獎勵廣告單元 ID（ca-app-pub-…/…）。設定後回呼的 ad_unit 必須相符；空字串表示不檢查。</summary>
    public string AdUnitId { get; set; } = "";
}

/// <summary>取不到 Google 驗證金鑰：結果未知，AdMob 會以 5xx 重試回呼。</summary>
public sealed class AdMobKeysUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface IAdMobSsvKeySource
{
    /// <summary>以 key_id 取得 Google 公開金鑰（SubjectPublicKeyInfo DER）；金鑰不存在回 null。</summary>
    Task<byte[]?> GetPublicKeyAsync(string keyId, CancellationToken cancellationToken);
}

/// <summary>
/// 從 Google 公佈的驗證金鑰清單取得 AdMob SSV 公開金鑰。金鑰會輪替，因此遇到未知 key_id 時
/// 在冷卻時間外重新抓取一次；清單本身不含祕密，快取在記憶體。
/// </summary>
public sealed class AdMobSsvKeySource(HttpClient http, TimeProvider clock) : IAdMobSsvKeySource
{
    internal const string KeysUrl = "https://www.gstatic.com/admob/reward/verifier-keys.json";
    private static readonly TimeSpan RefreshCooldown = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, byte[]> _keys = [];
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public async Task<byte[]?> GetPublicKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        if (_keys.TryGetValue(keyId, out byte[]? known)) { return known; }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_keys.TryGetValue(keyId, out known)) { return known; }
            if (clock.GetUtcNow() - _fetchedAt < RefreshCooldown) { return null; }
            _keys = await Fetch(cancellationToken);
            _fetchedAt = clock.GetUtcNow();
            return _keys.GetValueOrDefault(keyId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, byte[]>> Fetch(CancellationToken cancellationToken)
    {
        try
        {
            string json = await http.GetStringAsync(KeysUrl, cancellationToken);
            return ParseKeys(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException
            && !cancellationToken.IsCancellationRequested)
        {
            throw new AdMobKeysUnavailableException("AdMob verifier keys are unavailable.", ex);
        }
    }

    internal static Dictionary<string, byte[]> ParseKeys(string json)
    {
        KeyList list = JsonSerializer.Deserialize<KeyList>(json) ?? throw new JsonException("Empty key list.");
        return list.Keys.ToDictionary(key => key.KeyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            key => Convert.FromBase64String(key.Base64));
    }

    private sealed class KeyList
    {
        [JsonPropertyName("keys")] public List<Key> Keys { get; set; } = [];
    }

    private sealed class Key
    {
        [JsonPropertyName("keyId")] public long KeyId { get; set; }
        [JsonPropertyName("base64")] public string Base64 { get; set; } = "";
    }
}

/// <summary>
/// AdMob SSV 回呼的簽章檢查。簽章涵蓋查詢字串中 <c>&amp;signature=</c> 之前的原始內容（不解碼、不重排），
/// 簽章為 web-safe base64 的 ECDSA(SHA-256) DER。
/// </summary>
public static class AdMobSsvSignature
{
    private const string SignatureMarker = "&signature=";
    private const string KeyIdMarker = "&key_id=";

    public sealed record Signed(string Content, byte[] Signature, string KeyId);

    /// <summary>從原始查詢字串切出被簽署的內容、簽章與 key_id；格式不符回 null。</summary>
    public static Signed? Parse(string rawQuery)
    {
        string query = rawQuery.TrimStart('?');
        int signatureAt = query.IndexOf(SignatureMarker, StringComparison.Ordinal);
        int keyAt = query.IndexOf(KeyIdMarker, StringComparison.Ordinal);
        if (signatureAt < 0 || keyAt < signatureAt) { return null; }
        string signaturePart = query[(signatureAt + SignatureMarker.Length)..keyAt];
        string keyId = query[(keyAt + KeyIdMarker.Length)..];
        if (keyId.Contains('&') || signaturePart.Length == 0 || keyId.Length == 0) { return null; }
        byte[]? signature = DecodeWebSafeBase64(signaturePart);
        return signature == null ? null : new Signed(query[..signatureAt], signature, keyId);
    }

    public static bool Verify(byte[] publicKey, Signed signed)
    {
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out _);
            return key.VerifyData(System.Text.Encoding.UTF8.GetBytes(signed.Content), signed.Signature,
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static byte[]? DecodeWebSafeBase64(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(padded); }
        catch (FormatException) { return null; }
    }
}
