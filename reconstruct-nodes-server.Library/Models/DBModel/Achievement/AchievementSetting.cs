using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(AchievementSetting))]
    public class AchievementSetting : _AchievementSetting { }

    public class _AchievementSetting : ModelBase
    {
        public string Name { get => Get<string>(); set => Set(value); }
        public int Category { get => Get<int>(); set => Set(value); }
    }
}
