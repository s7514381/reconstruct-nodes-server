using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ReconstructNodesServer.Library.DBModel;

namespace ReconstructNodesServer.Library.Models;

/// <summary>每日簽到獎勵目錄：每月只需簽到 RequiredDays 天，第 n 次簽到領第 n 格獎勵。獎勵內容的唯一來源。</summary>
public static class DailyCheckInCatalog
{
    public const int RequiredDays = 20;
    public const string DiamondItemKey = "diamond";
    public const string EnergyExtractorItemKey = "energy_extractor";

    public sealed record Reward(string ItemKey, int Amount);

    private static readonly Reward[] Rewards = BuildRewards();

    public static IReadOnlyList<Reward> All => Rewards;

    public static Reward RewardFor(int ordinal) => Rewards[ordinal - 1];

    public static string MonthKey(DateTime date) => date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    public static string DateKey(DateTime date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static Reward[] BuildRewards()
    {
        var rewards = new Reward[RequiredDays];
        for (int i = 0; i < RequiredDays; i++)
        {
            int ordinal = i + 1;
            rewards[i] = ordinal switch
            {
                20 => new Reward(DiamondItemKey, 100),
                10 => new Reward(DiamondItemKey, 50),
                5 or 15 => new Reward(EnergyExtractorItemKey, 2),
                _ => new Reward(DiamondItemKey, 5 + ordinal / 5 * 5),
            };
        }
        return rewards;
    }
}

public sealed class DailyCheckInRewardModel
{
    [JsonPropertyName("ordinal")]
    public int Ordinal { get; set; }
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";
    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

public sealed class DailyCheckInClaimedModel
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
    [JsonPropertyName("ordinal")]
    public int Ordinal { get; set; }
}

/// <summary>本月簽到狀態：伺服器日期、已簽到日期與整張獎勵表，用戶端不自行推算日期或獎勵。</summary>
public sealed class DailyCheckInStatus
{
    [JsonPropertyName("month")]
    public string Month { get; set; } = "";
    [JsonPropertyName("today")]
    public string Today { get; set; } = "";
    [JsonPropertyName("required_days")]
    public int RequiredDays { get; set; }
    [JsonPropertyName("claimed")]
    public List<DailyCheckInClaimedModel> Claimed { get; set; } = [];
    [JsonPropertyName("rewards")]
    public List<DailyCheckInRewardModel> Rewards { get; set; } = [];
    [JsonPropertyName("can_claim")]
    public bool CanClaim { get; set; }
}

public sealed class DailyCheckInClaimRequest : CommandRequest
{
}

/// <summary>簽到回執：帶回發放後在庫物品，用戶端據此重設物品基準。</summary>
public sealed class DailyCheckInReceipt
{
    [JsonPropertyName("request_id")]
    public Guid RequestId { get; set; }
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
    [JsonPropertyName("ordinal")]
    public int Ordinal { get; set; }
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";
    [JsonPropertyName("amount")]
    public int Amount { get; set; }
    [JsonPropertyName("items")]
    public List<Item> Items { get; set; } = [];
}
