using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 移除一隻角色的唯一寫入來源（解散與當升級素材共用）：
/// 先解除裝備（武器本身是獨立資料列，解除後即回到背包可再裝備），再軟刪除技能節點、技能樹與角色。
/// 能力值點數池由玩家全部有效角色的點數加總算出，角色列被刪除即自動歸還；部署紀錄由呼叫端先確認沒有。
/// </summary>
internal static class CharacterRemoval
{
    public static void AddWrites(SqlWriteBatch writes, Character character) => AddWrites(writes, [character]);

    /// <summary>集合式移除：不論幾隻角色都只產生一個命令（三個 set-based UPDATE），避免逐隻往返與重複編譯。</summary>
    public static void AddWrites(SqlWriteBatch writes, IReadOnlyCollection<Character> characters)
    {
        if (characters.Count == 0) { return; }
        string trees = OwnedSql.Table<CharacterSkillTree>();
        string nodes = OwnedSql.Table<CharacterSkillNode>();
        string treeKey = OwnedSql.ForeignKey<CharacterSkillTree, Character>();
        string nodeKey = OwnedSql.ForeignKey<CharacterSkillNode, CharacterSkillTree>();
        string ids = "(SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON(@Ids))";
        writes.Add($"""
            UPDATE n SET n.Status = -1
            FROM {nodes} n INNER JOIN {trees} t ON t.Id = n.{nodeKey}
            WHERE n.CreateUserId = @CreateUserId AND t.CreateUserId = @CreateUserId
                AND t.{treeKey} IN {ids} AND n.Status != -1;
            UPDATE {trees} SET Status = -1
            WHERE {OwnedSql.Where($"{treeKey} IN {ids}")};
            UPDATE {OwnedSql.Table<Character>()} SET Status = -1, WeaponEntryKey = NULL, WeaponQualityVersion = NULL
            WHERE {OwnedSql.Where($"Id IN {ids}")};
            """, new { Ids = System.Text.Json.JsonSerializer.Serialize(characters.Select(c => c.Id)) });
    }
}
