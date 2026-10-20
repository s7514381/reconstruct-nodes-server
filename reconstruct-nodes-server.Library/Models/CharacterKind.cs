using System;
using System.Collections.Generic;
using System.Linq;

namespace ReconstructNodesServer.Library.Models
{
    /// <summary>
    /// 角色種類：與客戶端 scripts/character_kind.gd 的 CharacterKind.Kind 同值（兩邊各有契約測試）。
    /// 線上格式與資料庫維持整數（System.Text.Json 預設數字，未使用字串列舉轉換器），既有資料與舊客戶端不需遷移。
    /// 新增角色只在這裡加一個值（接在最後，不重排）；可部署、合法性、SQL 篩選都由本檔衍生。
    /// </summary>
    public enum CharacterKind
    {
        Warrior = 0,
        Archer = 1,
        Mage = 2,
        Core = 3,
        Crusader = 4,
        ShadowMage = 5,
        IceMage = 6,
        FireMage = 7,
        ShadowAssassin = 8,
        FlameSwordsman = 9,
        WindHunter = 10,
        Ranger = 11,
        DesertRanger = 12,
    }

    public static class CharacterKinds
    {
        public static readonly IReadOnlyList<CharacterKind> All = System.Enum.GetValues<CharacterKind>();

        /// <summary>可部署／可由商店獲得的職業（排除核心）。</summary>
        public static readonly IReadOnlyList<CharacterKind> Deployable = All.Where(kind => kind != CharacterKind.Core).ToArray();

        /// <summary>供 SQL 的 IN 清單，例如 "0, 1, 2, 4, 5, 6"；值皆來自列舉，不含使用者輸入。</summary>
        public static readonly string DeployableSqlList = string.Join(", ", Deployable.Select(kind => (int)kind));

        /// <summary>System.Text.Json 對未定義數值也會成功轉成列舉，入口必須再驗證。</summary>
        public static bool IsDefined(CharacterKind kind) => System.Enum.IsDefined(kind);

        /// <summary>資料庫／日誌用的數字字串（不是列舉名稱）。</summary>
        public static string Wire(CharacterKind kind) => ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
