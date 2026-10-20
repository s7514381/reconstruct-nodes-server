using System;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 角色性格（25 種，順序與 52poke 性格表一致）：與客戶端 scripts/character_traits.gd 的 Personality 同值（兩邊各有契約測試）。
    /// 每次取得角色時由伺服器隨機擲出；線上格式與資料庫皆為整數。數值效果只存在客戶端（戰鬥由客戶端結算），後端只保存身分。
    /// 新增項目只能接在最後，不重排。
    /// </summary>
    public enum CharacterPersonality
    {
        Hardy = 0, Lonely = 1, Adamant = 2, Naughty = 3, Brave = 4,
        Bold = 5, Docile = 6, Impish = 7, Lax = 8, Relaxed = 9,
        Modest = 10, Mild = 11, Bashful = 12, Rash = 13, Quiet = 14,
        Calm = 15, Gentle = 16, Careful = 17, Quirky = 18, Sassy = 19,
        Timid = 20, Hasty = 21, Jolly = 22, Naive = 23, Serious = 24,
    }

    /// <summary>
    /// 角色命格（10 種，仙俠主題）：與客戶端 scripts/character_traits.gd 的 Fate 同值。規則同 <see cref="CharacterPersonality"/>。
    /// </summary>
    public enum CharacterFate
    {
        Ziwei = 0,
        Qisha = 1,
        Pojun = 2,
        Tianji = 3,
        Taiyang = 4,
        Taiyin = 5,
        Tianliang = 6,
        Tanlang = 7,
        Lianzhen = 8,
        Tianfu = 9,
    }

    public static class CharacterTraits
    {
        public static readonly IReadOnlyList<CharacterPersonality> Personalities = System.Enum.GetValues<CharacterPersonality>();
        public static readonly IReadOnlyList<CharacterFate> Fates = System.Enum.GetValues<CharacterFate>();

        /// <summary>System.Text.Json 對未定義數值也會成功轉成列舉，入口必須再驗證。</summary>
        public static bool IsDefined(CharacterPersonality personality) => System.Enum.IsDefined(personality);
        public static bool IsDefined(CharacterFate fate) => System.Enum.IsDefined(fate);
    }

    /// <summary>角色等級規則的唯一後端來源；前端 CharacterLevelRules 必須同步。</summary>
    public static class CharacterLevelRules
    {
        public const int InitialLevel = 1;
        public const int MaxLevel = 10;

        public static bool IsValid(int level) => level is >= InitialLevel and <= MaxLevel;

        /// <summary>由 level 升到 level + 1 需要的素材角色數：2^(level-1)，即 1、2、4、8…；不可再升級時回傳 0。</summary>
        public static int UpgradeMaterialCount(int level) => IsValid(level) && level < MaxLevel ? 1 << (level - 1) : 0;

        /// <summary>投入 materialCount 隻素材後的最終等級：依序扣除各級所需數量，不足下一級即停止，多餘素材照樣消耗。必須與前端 level_after_materials 同步。</summary>
        public static int LevelAfterMaterials(int level, int materialCount)
        {
            int result = level;
            int remaining = materialCount;
            while (IsValid(result))
            {
                int cost = UpgradeMaterialCount(result);
                if (cost == 0 || remaining < cost) { break; }
                remaining -= cost;
                result++;
            }
            return result;
        }
    }
}
