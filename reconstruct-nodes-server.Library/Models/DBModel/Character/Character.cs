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

        /// <summary>角色等級（1～10），升級以其他角色為素材；抽到時為 1。</summary>
        public int Level { get => Get<int>(); set => Set(value); }
        /// <summary>性格：每次取得角色時隨機，之後不變。</summary>
        public CharacterPersonality Personality { get => Get<CharacterPersonality>(); set => Set(value); }
        /// <summary>命格：每次取得角色時隨機，之後不變。</summary>
        public CharacterFate Fate { get => Get<CharacterFate>(); set => Set(value); }
        /// <summary>玩家鎖定：鎖定的角色不能解散，也不能當升級素材。</summary>
        public bool Locked { get => Get<bool>(); set => Set(value); }

        public Character()
        {
            SkillLinkCount = SkillLinkRules.InitialCount;
            Level = CharacterLevelRules.InitialLevel;
        }
    }
}
