using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ReconstructNodesServer.Library.DBModel;

namespace ReconstructNodesServer.Library.Models;

/// <summary>看廣告領鑽石的規則：每次數量、每日上限與獎勵物品的唯一來源（用戶端只顯示後端回傳值）。</summary>
public static class AdRewardCatalog
{
    public const string DiamondItemKey = "diamond";
    public const int DiamondsPerAd = 10;
    public const int DailyLimit = 5;
}

/// <summary>今日看廣告進度：日期與次數只由伺服器決定。</summary>
public sealed class AdRewardStatus
{
    [JsonPropertyName("today")]
    public string Today { get; set; } = "";
    [JsonPropertyName("daily_limit")]
    public int DailyLimit { get; set; }
    [JsonPropertyName("claimed_today")]
    public int ClaimedToday { get; set; }
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";
    [JsonPropertyName("amount")]
    public int Amount { get; set; }
    [JsonPropertyName("can_watch")]
    public bool CanWatch { get; set; }
    /// <summary>播放廣告時帶入 SSV 的 user_id（玩家 ID）；SSV 回呼靠它對應到玩家，用戶端不自行推算。</summary>
    [JsonPropertyName("ssv_user_id")]
    public string SsvUserId { get; set; } = "";
    /// <summary>已通過 SSV 驗證但尚未領取的請求 ID（最早的一筆）；沒有則為空字串。用戶端據此補領被中斷的獎勵。</summary>
    [JsonPropertyName("pending_request_id")]
    public string PendingRequestId { get; set; } = "";
}

public sealed class AdRewardClaimRequest : CommandRequest
{
}

/// <summary>領取回執：帶回發放後在庫物品，用戶端據此重設鑽石基準。</summary>
public sealed class AdRewardReceipt
{
    [JsonPropertyName("request_id")]
    public Guid RequestId { get; set; }
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
    [JsonPropertyName("slot")]
    public int Slot { get; set; }
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";
    [JsonPropertyName("amount")]
    public int Amount { get; set; }
    [JsonPropertyName("items")]
    public List<Item> Items { get; set; } = [];
}
