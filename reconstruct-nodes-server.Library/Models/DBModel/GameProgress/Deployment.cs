using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Deployment))]
    public class Deployment : ModelBase
    {
        public int SpotIndex { get => Get<int>(); set => Set(value); }

        public int? CellX { get => Get<int?>(); set => Set(value); }

        public int? CellY { get => Get<int?>(); set => Set(value); }

        public string CharacterName { get => Get<string>(); set => Set(value); }

        public int Wave { get => Get<int>(); set => Set(value); }

        public int Kind { get => Get<int>(); set => Set(value); }

        /// <summary>
        /// 部署武器的 EntryKey，參照 Weapon 列；沒有裝備武器時為 null
        /// </summary>
        public string WeaponEntryKey { get => Get<string>(); set => Set(value); }

        public int? WeaponQualityVersion { get => Get<int?>(); set => Set(value); }
    }
}
