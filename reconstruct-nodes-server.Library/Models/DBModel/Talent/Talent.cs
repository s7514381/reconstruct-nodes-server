using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    /// 每位玩家僅一筆；由服務層以交易保證唯一性。
    [Table(nameof(Talent))]
    public class Talent : ModelBase
    {
        public int Version { get => Get<int>(); set => Set(value); }

        public long TalentPoints { get => Get<long>(); set => Set(value); }

        public long TotalSpentPoints { get => Get<long>(); set => Set(value); }

        public int PointsRecalculationVersion { get => Get<int>(); set => Set(value); }
    }
}
