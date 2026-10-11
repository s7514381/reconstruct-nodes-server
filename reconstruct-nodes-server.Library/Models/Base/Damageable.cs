using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    public abstract class Damageable : ModelBase
    {
        public string Name { get => Get<string>(); set => Set(value); }
        public string SpritePath { get => Get<string>(); set => Set(value); }
        public float? Hp { get => Get<float?>(); set => Set(value); }
        public float? Movespeed { get => Get<float?>(); set => Set(value); }
        public float? Attack { get => Get<float?>(); set => Set(value); }
        public float? Recovery { get => Get<float?>(); set => Set(value); }
    }


}
