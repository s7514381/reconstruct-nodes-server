using System;

namespace ReconstructNodesServer.Library.Models;

/// <summary>
/// 榮譽（解散角色換得的資源，目前沒有用途）規則的唯一後端來源；前端 CharacterHonorRules 必須同步物品 ID 與每級榮譽。
/// 榮譽以一般物品儲存（key 固定為 <see cref="ItemKey"/>），沿用物品增減 SQL 與背包鏡像。
/// </summary>
public static class CharacterHonorRules
{
    public const string ItemKey = "honor";
    public const int HonorPerLevel = 10;

    /// <summary>解散指定等級的角色可得的榮譽。</summary>
    public static int DissolveReward(int level) => CharacterLevelRules.IsValid(level)
        ? level * HonorPerLevel
        : throw new ArgumentOutOfRangeException(nameof(level), level, "Invalid character level.");
}
