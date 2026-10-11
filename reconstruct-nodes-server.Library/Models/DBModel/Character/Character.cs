using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Character))]
    public class Character : ModelBase
    {
        public CharacterCategory Category { get => Get<CharacterCategory>(); set => Set(value); }
        public float? Level { get => Get<float?>(); set => Set(value); }
        public float? CurrentExp { get => Get<float?>(); set => Set(value); }
    }

    public enum CharacterCategory
    {
        劍客 = 0, 刺客 = 1, 魔法師 = 2, 戰士 = 3, 弓箭手 = 4,
    }

}
