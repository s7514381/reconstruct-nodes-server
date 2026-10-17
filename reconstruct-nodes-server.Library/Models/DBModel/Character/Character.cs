using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Character))]
    public class Character : ModelBase
    {
        public int Kind { get => Get<int>(); set => Set(value); }
        public string WeaponEntryKey { get => Get<string>(); set => Set(value); }
        public int? WeaponQualityVersion { get => Get<int?>(); set => Set(value); }
        public int Strength { get => Get<int>(); set => Set(value); }
        public int Intelligence { get => Get<int>(); set => Set(value); }
        public int Dexterity { get => Get<int>(); set => Set(value); }
    }
}
