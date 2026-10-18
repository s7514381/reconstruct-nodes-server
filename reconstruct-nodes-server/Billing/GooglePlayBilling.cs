using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth.OAuth2;

namespace ReconstructNodesServer.Billing;

public sealed class GooglePlayBillingOptions
{
    public const string SectionName = "GooglePlay";
    public string PackageName { get; set; } = "org.reconstructnodes.game";
    /// <summary>相對於 ContentRoot 的服務帳戶金鑰檔；位於 Secret 資料夾，不進版控。</summary>
    public string CredentialFile { get; set; } = Path.Combine("Secret", "google-play.json");
}

/// <summary>Google Play Developer API purchases.products 的回應；欄位語意依官方文件。</summary>
public sealed class GooglePlayProductPurchase
{
    /// <summary>0 已購買、1 已取消、2 待處理。</summary>
    [JsonPropertyName("purchaseState")] public int PurchaseState { get; set; }
    /// <summary>0 尚未 consume、1 已 consume。</summary>
    [JsonPropertyName("consumptionState")] public int ConsumptionState { get; set; }
    [JsonPropertyName("orderId")] public string OrderId { get; set; } = "";
}

/// <summary>憑證缺漏、網路失敗或 Google 回非預期狀態：結果未知，呼叫端以原請求重試。</summary>
public sealed class GooglePlayUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public interface IGooglePlayBilling
{
    /// <summary>查詢購買；Google 回 400/404（token 不存在或不屬於此商品）時回 null。</summary>
    Task<GooglePlayProductPurchase?> GetProductPurchaseAsync(string productId, string purchaseToken, CancellationToken cancellationToken);

    /// <summary>consume 同時完成 acknowledge；已 consume 的購買重送視為成功。</summary>
    Task ConsumeAsync(string productId, string purchaseToken, CancellationToken cancellationToken);
}

/// <summary>
/// 以服務帳戶經 REST 驗證並 consume 內購。憑證延遲載入：缺檔不影響伺服器啟動，只讓購買端點回報不可用。
/// </summary>
public sealed class GooglePlayBilling(HttpClient http, GooglePlayBillingOptions options, IHostEnvironment environment)
    : IGooglePlayBilling
{
    private const string Scope = "https://www.googleapis.com/auth/androidpublisher";
    private const string Root = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications";

    private readonly string _credentialPath = Path.Combine(environment.ContentRootPath, options.CredentialFile);
    private readonly object _credentialLock = new();
    private GoogleCredential? _credential;

    public async Task<GooglePlayProductPurchase?> GetProductPurchaseAsync(
        string productId, string purchaseToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = await CreateAsync(HttpMethod.Get, productId, purchaseToken, "", cancellationToken);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound) { return null; }
        EnsureSuccess(response);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<GooglePlayProductPurchase>(json)
            ?? throw new GooglePlayUnavailableException("Google Play returned an empty purchase.");
    }

    public async Task ConsumeAsync(string productId, string purchaseToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = await CreateAsync(HttpMethod.Post, productId, purchaseToken, ":consume", cancellationToken);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        // 已被 consume 的購買再次 consume 會回 400，等同目標狀態。
        if (response.StatusCode == HttpStatusCode.BadRequest) { return; }
        EnsureSuccess(response);
    }

    private async Task<HttpRequestMessage> CreateAsync(HttpMethod method, string productId, string purchaseToken,
        string suffix, CancellationToken cancellationToken)
    {
        string accessToken;
        try
        {
            accessToken = await GetCredential().UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new GooglePlayUnavailableException("Google Play credential is unavailable.", ex);
        }
        string url = $"{Root}/{Uri.EscapeDataString(options.PackageName)}/purchases/products/" +
            $"{Uri.EscapeDataString(productId)}/tokens/{Uri.EscapeDataString(purchaseToken)}{suffix}";
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private GoogleCredential GetCredential()
    {
        lock (_credentialLock)
        {
            return _credential ??= CredentialFactory.FromFile<ServiceAccountCredential>(_credentialPath)
                .ToGoogleCredential().CreateScoped(Scope);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new GooglePlayUnavailableException("Google Play request failed.", ex);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new GooglePlayUnavailableException($"Google Play returned HTTP {(int)response.StatusCode}.");
        }
    }
}
