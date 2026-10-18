namespace ReconstructNodesServer.Library.Models;

/// <summary>
/// 角色技能連線數（一條技能鏈的節點上限，含主動技能）規則的唯一後端來源；
/// 前端 SkillLinkRules 必須同步初始值、上限、物品 ID 與升級費用。
/// </summary>
public static class SkillLinkRules
{
    public const int InitialCount = 2;
    public const int MaxCount = 6;
    public const string UpgradeItemKey = "energy_extractor";

    /// <summary>從 currentCount 升到下一級需要的能量萃取器數量；已達上限回傳 null。</summary>
    public static int? UpgradeCost(int currentCount) => currentCount switch
    {
        2 => 1,
        3 => 5,
        4 => 25,
        5 => 100,
        _ => null,
    };
}
