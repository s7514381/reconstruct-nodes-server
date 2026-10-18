using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterSkillLinkCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SkillLinkCount",
                table: "Character",
                type: "int",
                nullable: false,
                defaultValue: 2);

            // 既有角色保留已配置的鏈長，避免新上限讓舊存檔無法再儲存技能。
            migrationBuilder.Sql("""
                UPDATE c SET SkillLinkCount = m.Longest
                FROM [Character] c
                INNER JOIN (
                    SELECT t.CharacterId, MAX(t.Nodes + 1) AS Longest
                    FROM (SELECT st.CharacterId, st.Id, COUNT(n.Id) AS Nodes
                          FROM CharacterSkillTree st
                          LEFT JOIN CharacterSkillNode n ON n.CharacterSkillTreeId = st.Id AND n.Status != -1
                          WHERE st.Status != -1
                          GROUP BY st.CharacterId, st.Id) t
                    GROUP BY t.CharacterId) m ON m.CharacterId = c.Id
                WHERE m.Longest > c.SkillLinkCount;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SkillLinkCount",
                table: "Character");
        }
    }
}
