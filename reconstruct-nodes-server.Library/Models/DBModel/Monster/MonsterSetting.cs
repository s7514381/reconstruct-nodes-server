using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(MonsterSetting))]
    public class MonsterSetting : _MonsterSetting { }

    public class _MonsterSetting : Damageable
    {
        public MonsterCategory Category { get => Get<MonsterCategory>(); set => Set(value); }
    }

    public enum MonsterCategory
    {
        小怪 = 0, 綠怪 = 1, 藍怪 = 2, 紅怪 = 3, 金怪 = 4, 紫怪 = 5,
    }
}
