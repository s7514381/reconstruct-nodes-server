using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 角色能力值規則的唯一後端來源：升級換得的點數池由玩家等級決定，全部角色共用；
/// 前端同名常數與公式（ProgressionData）必須同步。
/// </summary>
public static class CharacterAttributeRules
{
    public const int PointsPerLevel = 6;
    public const int StartLevel = 1;

    /// <summary>等級換得的能力值總點數；等級 × 6（1 等即 6 點，100 等 600 點），等級限制在 1 ～ MaxLevel。</summary>
    public static int TotalPoints(int level) =>
        Math.Clamp(level, StartLevel, ProgressionRules.MaxLevel) * PointsPerLevel;

    /// <summary>各項不得為負，且三項合計加上其他角色已配置點數不得超過總點數。</summary>
    public static void Validate(CharacterAttributesRequest request, int level, int allocatedByOthers)
    {
        if (request.Strength < 0 || request.Intelligence < 0 || request.Dexterity < 0)
        { throw new BusinessArgumentException(ApiMessageKeys.CharacterAttributesInvalid); }
        long total = (long)request.Strength + request.Intelligence + request.Dexterity + allocatedByOthers;
        if (total > TotalPoints(level))
        { throw new BusinessArgumentException(ApiMessageKeys.CharacterAttributesExceeded); }
    }
}
