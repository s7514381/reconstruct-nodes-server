using System;
using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    /// <summary>
    /// 看廣告領鑽石的紀錄。AdMob 伺服器端驗證（SSV）回呼先建立「已驗證、未領取」的列（Claimed=false），
    /// 玩家以 RequestId 領取時才計入當日次數並發放。TransactionId 全域唯一：同一次廣告觀看只能發放一次；
    /// (玩家, DateKey, Slot) 唯一：同一天的第 n 次領取只會成功一次，並行請求由唯一索引擋下。
    /// </summary>
    [Table(nameof(AdRewardGrant))]
    public class AdRewardGrant : ModelBase
    {
        /// <summary>AdMob 回呼的 transaction_id（Google 簽章保證）。</summary>
        public string TransactionId { get => Get<string>(); set => Set(value); }
        /// <summary>用戶端以 SSV custom_data 帶入、領取時沿用的請求 ID。</summary>
        public Guid RequestId { get => Get<Guid>(); set => Set(value); }
        public bool Claimed { get => Get<bool>(); set => Set(value); }
        /// <summary>領取當天的伺服器本地日期（yyyy-MM-dd）；未領取為 null。</summary>
        public string? DateKey { get => Get<string?>(); set => Set(value); }
        /// <summary>當天第幾次領取（1 起算）；未領取為 null。</summary>
        public int? Slot { get => Get<int?>(); set => Set(value); }
        public int Amount { get => Get<int>(); set => Set(value); }
    }
}
