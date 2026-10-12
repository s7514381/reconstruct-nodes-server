using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class RenameGameProgressDeploymentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename only. No DropTable/CreateTable, so existing rows and column values are preserved.
            // EF scaffolding cannot infer these renames from the new CLR types, so they are written by hand.
            migrationBuilder.RenameTable(
                name: "GameProgressDeployment",
                newName: "Deployment");

            migrationBuilder.RenameTable(
                name: "GameProgressDeploymentSkill",
                newName: "DeploymentSkill");

            migrationBuilder.RenameTable(
                name: "GameProgressDeploymentSkillNode",
                newName: "DeploymentSkillNode");

            migrationBuilder.RenameTable(
                name: "GameProgressBattlefieldRoad",
                newName: "BattlefieldRoad");

            migrationBuilder.RenameColumn(
                name: "GameProgressDeploymentId",
                table: "DeploymentSkill",
                newName: "DeploymentId");

            migrationBuilder.RenameColumn(
                name: "GameProgressDeploymentSkillId",
                table: "DeploymentSkillNode",
                newName: "DeploymentSkillId");

            migrationBuilder.RenameIndex(
                name: "IX_GameProgressDeployment_CreateUserId",
                table: "Deployment",
                newName: "IX_Deployment_CreateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_GameProgressDeploymentSkill_CreateUserId",
                table: "DeploymentSkill",
                newName: "IX_DeploymentSkill_CreateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_GameProgressDeploymentSkill_GameProgressDeploymentId",
                table: "DeploymentSkill",
                newName: "IX_DeploymentSkill_DeploymentId");

            migrationBuilder.RenameIndex(
                name: "IX_GameProgressDeploymentSkillNode_CreateUserId",
                table: "DeploymentSkillNode",
                newName: "IX_DeploymentSkillNode_CreateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_GameProgressDeploymentSkillNode_GameProgressDeploymentSkillId",
                table: "DeploymentSkillNode",
                newName: "IX_DeploymentSkillNode_DeploymentSkillId");

            migrationBuilder.RenameIndex(
                name: "IX_GameProgressBattlefieldRoad_CreateUserId",
                table: "BattlefieldRoad",
                newName: "IX_BattlefieldRoad_CreateUserId");

            // RenameTable/RenameIndex do not rename primary keys; sp_rename on a PK also renames its clustered index.
            migrationBuilder.Sql("EXEC sp_rename N'PK_GameProgressDeployment', N'PK_Deployment', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'PK_GameProgressDeploymentSkill', N'PK_DeploymentSkill', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'PK_GameProgressDeploymentSkillNode', N'PK_DeploymentSkillNode', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'PK_GameProgressBattlefieldRoad', N'PK_BattlefieldRoad', N'OBJECT';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse of Up. Rename only; no data is dropped or recreated.
            migrationBuilder.Sql("EXEC sp_rename N'PK_BattlefieldRoad', N'PK_GameProgressBattlefieldRoad', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'PK_DeploymentSkillNode', N'PK_GameProgressDeploymentSkillNode', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'PK_DeploymentSkill', N'PK_GameProgressDeploymentSkill', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'PK_Deployment', N'PK_GameProgressDeployment', N'OBJECT';");

            migrationBuilder.RenameIndex(
                name: "IX_BattlefieldRoad_CreateUserId",
                table: "BattlefieldRoad",
                newName: "IX_GameProgressBattlefieldRoad_CreateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_DeploymentSkillNode_DeploymentSkillId",
                table: "DeploymentSkillNode",
                newName: "IX_GameProgressDeploymentSkillNode_GameProgressDeploymentSkillId");

            migrationBuilder.RenameIndex(
                name: "IX_DeploymentSkillNode_CreateUserId",
                table: "DeploymentSkillNode",
                newName: "IX_GameProgressDeploymentSkillNode_CreateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_DeploymentSkill_DeploymentId",
                table: "DeploymentSkill",
                newName: "IX_GameProgressDeploymentSkill_GameProgressDeploymentId");

            migrationBuilder.RenameIndex(
                name: "IX_DeploymentSkill_CreateUserId",
                table: "DeploymentSkill",
                newName: "IX_GameProgressDeploymentSkill_CreateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_Deployment_CreateUserId",
                table: "Deployment",
                newName: "IX_GameProgressDeployment_CreateUserId");

            migrationBuilder.RenameColumn(
                name: "DeploymentSkillId",
                table: "DeploymentSkillNode",
                newName: "GameProgressDeploymentSkillId");

            migrationBuilder.RenameColumn(
                name: "DeploymentId",
                table: "DeploymentSkill",
                newName: "GameProgressDeploymentId");

            migrationBuilder.RenameTable(
                name: "BattlefieldRoad",
                newName: "GameProgressBattlefieldRoad");

            migrationBuilder.RenameTable(
                name: "DeploymentSkillNode",
                newName: "GameProgressDeploymentSkillNode");

            migrationBuilder.RenameTable(
                name: "DeploymentSkill",
                newName: "GameProgressDeploymentSkill");

            migrationBuilder.RenameTable(
                name: "Deployment",
                newName: "GameProgressDeployment");
        }
    }
}
