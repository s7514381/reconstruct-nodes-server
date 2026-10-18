using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ReconstructNodesServer.Library.DBModel;

namespace ReconstructNodesServer.Library.Models;

/// <summary>Google Play 內購商品目錄：商品 ID 與發放數量的唯一來源。</summary>
public static class PurchaseCatalog
{
    public const string Diamond100 = "diamond_100";
    public const string DiamondItemKey = "diamond";

    private static readonly Dictionary<string, int> DiamondsByProduct = new() { [Diamond100] = 100 };

    public static bool TryGetDiamonds(string productId, out int diamonds) =>
        DiamondsByProduct.TryGetValue(productId, out diamonds);
}

/// <summary>用戶端完成 Google Play 購買後送交驗證；request_id 由用戶端 journal 固定，重試沿用。</summary>
public sealed class GooglePlayPurchaseRequest : CommandRequest
{
    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = "";
    [JsonPropertyName("purchase_token")]
    public string PurchaseToken { get; set; } = "";
}

/// <summary>發放回執：帶回發放後的在庫物品，用戶端據此重設鑽石基準，不必再讀 Item 清單。</summary>
public sealed class GooglePlayPurchaseReceipt
{
    [JsonPropertyName("request_id")]
    public Guid RequestId { get; set; }
    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = "";
    [JsonPropertyName("diamonds")]
    public int Diamonds { get; set; }
    [JsonPropertyName("items")]
    public List<Item> Items { get; set; } = [];
    /// <summary>Google 端 consume 是否完成；false 時用戶端保留 journal，稍後以原請求重送。</summary>
    [JsonPropertyName("consumed")]
    public bool Consumed { get; set; }
}
