using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(MapSetting))]
    public class MapSetting : _MapSetting { }

    public class _MapSetting : ModelBase
    {
        public string Name { get => Get<string>(); set => Set(value); }
        public string SpritePath { get => Get<string>(); set => Set(value); }
    }
}
