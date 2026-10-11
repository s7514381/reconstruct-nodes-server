using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Map))]
    public class Map : _MapSetting 
    {
        [Relation(nameof(MapSetting))]
        public Guid? MapSettingId { get => Get<Guid?>(); set => Set(value); }
    }

    public enum MapType
    {
        荒野 = 0,
        平原 = 1,
        森林 = 2,
        火山 = 3,
        冰原 = 4
    }
}
