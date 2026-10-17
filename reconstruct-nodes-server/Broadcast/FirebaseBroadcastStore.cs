using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace ReconstructNodesServer.Broadcast;

public interface IBroadcastStore
{
    Task<IReadOnlyList<BroadcastItem>> ListAsync(CancellationToken cancellationToken);
    Task PutAsync(BroadcastItem item, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);
}

public sealed class FirebaseBroadcastOptions
{
    public const string SectionName = "Firebase";
    public string DatabaseUrl { get; set; } = string.Empty;
    /// <summary>相對於 ContentRoot 的服務帳戶金鑰檔；位於 Secret 資料夾，不進版控。</summary>
    public string CredentialFile { get; set; } = Path.Combine("Secret", "firebase-adminsdk.json");
    /// <summary>RTDB 內的廣播根節點；前端讀取 `{BroadcastPath}/items.json`。</summary>
    public string BroadcastPath { get; set; } = string.Empty;
    public string AdminKey { get; set; } = string.Empty;
}

/// <summary>
/// 以服務帳戶（Admin 權限，略過 RTDB 規則）經 REST 寫入廣播；遊戲端只讀公開節點，不持有任何金鑰。
/// 憑證延遲載入：缺金鑰不影響伺服器啟動，僅廣播端點回報不可用。
/// </summary>
public sealed class FirebaseBroadcastStore : IBroadcastStore
{
    private const string DatabaseScope = "https://www.googleapis.com/auth/firebase.database";
    private const string EmailScope = "https://www.googleapis.com/auth/userinfo.email";

    private readonly HttpClient _http;
    private readonly FirebaseBroadcastOptions _options;
    private readonly string _credentialPath;
    private readonly object _credentialLock = new();
    private GoogleCredential? _credential;

    public FirebaseBroadcastStore(HttpClient http, FirebaseBroadcastOptions options, IHostEnvironment environment)
    {
        _http = http;
        _options = options;
        _credentialPath = Path.Combine(environment.ContentRootPath, options.CredentialFile);
    }

    public async Task<IReadOnlyList<BroadcastItem>> ListAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = await CreateAsync(HttpMethod.Get, "items", cancellationToken);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null") { return []; }

        Dictionary<string, BroadcastItem>? map = JsonSerializer.Deserialize<Dictionary<string, BroadcastItem>>(json);
        return map == null ? [] : [.. map.Values.Where(item => item != null).OrderBy(item => item.PublishedAt)];
    }

    public async Task PutAsync(BroadcastItem item, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = await CreateAsync(HttpMethod.Put, $"items/{item.Id}", cancellationToken);
        request.Content = new StringContent(JsonSerializer.Serialize(item), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        // RTDB 的 DELETE 對不存在節點也回 200，先 GET 才能回報是否真的存在。
        using HttpRequestMessage probe = await CreateAsync(HttpMethod.Get, $"items/{id}", cancellationToken);
        using HttpResponseMessage probeResponse = await SendAsync(probe, cancellationToken);
        string existing = await probeResponse.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(existing) || existing.Trim() == "null") { return false; }

        using HttpRequestMessage request = await CreateAsync(HttpMethod.Delete, $"items/{id}", cancellationToken);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return true;
    }

    private async Task<HttpRequestMessage> CreateAsync(HttpMethod method, string relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DatabaseUrl) || string.IsNullOrWhiteSpace(_options.BroadcastPath))
        {
            throw new BroadcastUnavailableException("Firebase:DatabaseUrl or Firebase:BroadcastPath is not configured.");
        }

        string token;
        try
        {
            token = await GetCredential().UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new BroadcastUnavailableException("Firebase credential is unavailable.", ex);
        }

        string url = $"{_options.DatabaseUrl.TrimEnd('/')}/{_options.BroadcastPath.Trim('/')}/{relativePath}.json";
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private GoogleCredential GetCredential()
    {
        lock (_credentialLock)
        {
            return _credential ??= CredentialFactory.FromFile<ServiceAccountCredential>(_credentialPath).ToGoogleCredential().CreateScoped(DatabaseScope, EmailScope);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new BroadcastUnavailableException("Firebase request failed.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            int status = (int)response.StatusCode;
            response.Dispose();
            throw new BroadcastUnavailableException($"Firebase returned HTTP {status}.");
        }
        return response;
    }
}
