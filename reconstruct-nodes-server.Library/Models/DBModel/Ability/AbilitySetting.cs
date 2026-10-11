using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Dapper.Contrib.Extensions.Table(nameof(AbilitySetting))]
    public class AbilitySetting : _AbilitySetting { }

    public class _AbilitySetting : ModelBase
    {
        public AbilityCategory? Category { get => Get<AbilityCategory?>(); set => Set(value); }
        public float? Weights { get => Get<float?>(); set => Set(value); }
        public string Name { get => Get<string>(); set => Set(value); }
        public float? Level { get => Get<float?>(); set => Set(value); }
        public float? MaxLevel { get => Get<float?>(); set => Set(value); }
        public float? CurrentExp { get => Get<float?>(); set => Set(value); }
        public bool? IsOwned { get => Get<bool?>(); set => Set(value); }
        public float? Grade { get => Get<float?>(); set => Set(value); }
        public float? Attack { get => Get<float?>(); set => Set(value); }
        
    }

    public enum AbilityCategory 
    {
        專精 = 0,光劍 = 1, 斧頭 = 2, 火箭筒 = 3, 火球 = 4,
    }

}
