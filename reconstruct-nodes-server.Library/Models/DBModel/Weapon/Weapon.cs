using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Weapon))]
    public class Weapon : ModelBase
    {
        public string EntryKey { get => Get<string>(); set => Set(value); }

        public string WeaponKey { get => Get<string>(); set => Set(value); }

        public string WeaponFamilyKey { get => Get<string>(); set => Set(value); }

        public string Rarity { get => Get<string>(); set => Set(value); }

        public string UniqueKey { get => Get<string>(); set => Set(value); }
    }
}
