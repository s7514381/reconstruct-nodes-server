using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 戰場空間：玩家等級決定總空間，角色種類決定部署占用。數值與客戶端 scripts/battlefield_space_rules.gd 必須同步。
/// </summary>
public static class BattlefieldSpaceRules
{
    /// <summary>1 等的總空間。</summary>
    public const int BaseSpace = 20;
    /// <summary>之後每升一等增加的空間。</summary>
    public const int SpacePerLevel = 1;
    public const int MaxSpace = 60;
    /// <summary>未在 KindCosts 另外設定的角色種類占用。</summary>
    public const int DefaultCost = 10;

    /// <summary>角色種類 → 占用空間；沒有列出的種類使用 DefaultCost。</summary>
    private static readonly IReadOnlyDictionary<CharacterKind, int> KindCosts = new Dictionary<CharacterKind, int>();

    public static int Capacity(int playerLevel) =>
        Math.Min(BaseSpace + (Math.Clamp(playerLevel, 1, ProgressionRules.MaxLevel) - 1) * SpacePerLevel, MaxSpace);

    public static int CostOf(CharacterKind kind) => KindCosts.TryGetValue(kind, out int cost) ? cost : DefaultCost;

    /// <summary>部署角色占用的空間總和不得超過玩家等級對應的總空間；characters 為本人可部署角色（含種類）。</summary>
    public static void Validate(IEnumerable<GameProgressDeploymentModel> deployments, IEnumerable<Character> characters, int playerLevel)
    {
        Dictionary<Guid, CharacterKind> kinds = characters.ToDictionary(row => row.Id, row => row.Kind);
        long used = deployments.Sum(row => (long)CostOf(kinds.GetValueOrDefault(row.CharacterId)));
        if (used > Capacity(playerLevel)) { throw new BusinessArgumentException("battlefield.space_insufficient"); }
    }
}
