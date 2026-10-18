using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Character))]
    public class Character : ModelBase
    {
        public CharacterKind Kind { get => Get<CharacterKind>(); set => Set(value); }
        public string WeaponEntryKey { get => Get<string>(); set => Set(value); }
        public int? WeaponQualityVersion { get => Get<int?>(); set => Set(value); }
        public int Strength { get => Get<int>(); set => Set(value); }
        public int Intelligence { get => Get<int>(); set => Set(value); }
        public int Dexterity { get => Get<int>(); set => Set(value); }
        /// <summary>技能鏈節點上限（含主動技能）：初始 2，以能量萃取器升級，最多 6。</summary>
        public int SkillLinkCount { get => Get<int>(); set => Set(value); }

        public Character() { SkillLinkCount = SkillLinkRules.InitialCount; }
    }
}
