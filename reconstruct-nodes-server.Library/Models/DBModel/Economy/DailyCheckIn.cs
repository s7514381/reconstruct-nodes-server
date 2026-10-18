using System;
using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    /// <summary>
    /// 每日簽到紀錄。DateKey（yyyy-MM-dd，伺服器本地日期）在同一玩家內唯一：同一天只能簽到一次，
    /// 並行請求由唯一索引保證只有一筆成功。月份計數取 DateKey 前七碼相同的列。
    /// </summary>
    [Table(nameof(DailyCheckIn))]
    public class DailyCheckIn : ModelBase
    {
        public string DateKey { get => Get<string>(); set => Set(value); }
        /// <summary>該月第幾次簽到（1 起算），決定獎勵。</summary>
        public int Ordinal { get => Get<int>(); set => Set(value); }
        public Guid RequestId { get => Get<Guid>(); set => Set(value); }
        public string ItemKey { get => Get<string>(); set => Set(value); }
        public int Amount { get => Get<int>(); set => Set(value); }
    }
}
