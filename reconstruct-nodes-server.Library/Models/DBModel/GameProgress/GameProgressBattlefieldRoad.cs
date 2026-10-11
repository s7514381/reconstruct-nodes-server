using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(GameProgressBattlefieldRoad))]
    public class GameProgressBattlefieldRoad : ModelBase
    {
        public int X { get => Get<int>(); set => Set(value); }

        public int Y { get => Get<int>(); set => Set(value); }

        public bool Corner { get => Get<bool>(); set => Set(value); }

        public int Rotation { get => Get<int>(); set => Set(value); }
    }
}
