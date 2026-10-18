using System;
using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    /// <summary>
    /// Google Play 內購的發放紀錄。PurchaseToken 全域唯一：同一筆購買不論由哪個請求、哪個帳號重送，只會發放一次。
    /// </summary>
    [Table(nameof(IapPurchase))]
    public class IapPurchase : ModelBase
    {
        public string PurchaseToken { get => Get<string>(); set => Set(value); }
        public string ProductId { get => Get<string>(); set => Set(value); }
        public string OrderId { get => Get<string>(); set => Set(value); }
        public Guid RequestId { get => Get<Guid>(); set => Set(value); }
        public int Diamonds { get => Get<int>(); set => Set(value); }
        /// <summary>Google 端是否已 consume（同時完成 acknowledge）；未完成時重送同一請求會重試。</summary>
        public bool Consumed { get => Get<bool>(); set => Set(value); }
    }
}
