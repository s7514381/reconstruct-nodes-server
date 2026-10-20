using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ReconstructNodesServer.Library.DBModel;

namespace ReconstructNodesServer.Library.Models;

/// <summary>單一挑戰關卡的今日狀態：每日次數與通關獎勵都由伺服器決定，用戶端不自行推算。</summary>
public sealed class ChallengeStageStatusModel
{
    /// <summary>關卡槽（StageRules 難度，5 = 挑戰1）。</summary>
    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; }
    [JsonPropertyName("daily_limit")]
    public int DailyLimit { get; set; }
    /// <summary>今天還能進入的次數；進入時扣 1，失敗不退還。</summary>
    [JsonPropertyName("remaining")]
    public int Remaining { get; set; }
    [JsonPropertyName("reward_item_id")]
    public string RewardItemId { get; set; } = "";
    [JsonPropertyName("reward_amount")]
    public int RewardAmount { get; set; }
}

public sealed class ChallengeStatus
{
    /// <summary>伺服器本地日期（yyyy-MM-dd），次數以此日換日重置。</summary>
    [JsonPropertyName("today")]
    public string Today { get; set; } = "";
    [JsonPropertyName("stages")]
    public List<ChallengeStageStatusModel> Stages { get; set; } = [];
}

/// <summary>領取目前挑戰關卡的通關獎勵；關卡由伺服器進度決定，請求只帶冪等用的 request_id。</summary>
public sealed class ChallengeClaimRequest : CommandRequest
{
}

/// <summary>挑戰獎勵回條：帶回發放後在庫物品，用戶端據此重設物品基準。</summary>
public sealed class ChallengeRewardReceipt
{
    [JsonPropertyName("request_id")]
    public Guid RequestId { get; set; }
    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; }
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";
    [JsonPropertyName("amount")]
    public int Amount { get; set; }
    [JsonPropertyName("items")]
    public List<Item> Items { get; set; } = [];
}
