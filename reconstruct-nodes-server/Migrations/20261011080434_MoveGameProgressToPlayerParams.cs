using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class MoveGameProgressToPlayerParams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WeaponEntryKey",
                table: "GameProgressDeployment",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeaponQualityVersion",
                table: "GameProgressDeployment",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CharacterWeapon",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CharacterKind = table.Column<int>(type: "int", nullable: false),
                    EntryKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterWeapon", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterWeapon_CreateUserId",
                table: "CharacterWeapon",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterWeapon_CreateUserId_CharacterKind",
                table: "CharacterWeapon",
                columns: new[] { "CreateUserId", "CharacterKind" },
                unique: true,
                filter: "[Status] <> -1");

            // 必須先搬移資料，再刪除舊表與舊欄位
            migrationBuilder.Sql(@"
-- 有效的 GameProgress 每位玩家至多一筆（唯一索引保證），作為搬移來源
SELECT gp.*
INTO #LiveProgress
FROM GameProgress gp
WHERE gp.Status <> -1 AND gp.CreateUserId IS NOT NULL;

-- 有效部署、技能、節點、道路：父層有效且自身未刪除才搬移，其餘之後軟刪除
SELECT d.Id, d.Sequence, lp.Id AS GameProgressId, lp.CreateUserId AS OwnerId
INTO #LiveDeployment
FROM GameProgressDeployment d
JOIN #LiveProgress lp ON lp.Id = d.GameProgressId
WHERE d.Status <> -1;

SELECT sk.Id, ld.OwnerId
INTO #LiveSkill
FROM GameProgressDeploymentSkill sk
JOIN #LiveDeployment ld ON ld.Id = sk.GameProgressDeploymentId
WHERE sk.Status <> -1;

SELECT n.Id, ls.OwnerId
INTO #LiveNode
FROM GameProgressDeploymentSkillNode n
JOIN #LiveSkill ls ON ls.Id = n.GameProgressDeploymentSkillId
WHERE n.Status <> -1;

SELECT r.Id, lp.CreateUserId AS OwnerId
INTO #LiveRoad
FROM GameProgressBattlefieldRoad r
JOIN #LiveProgress lp ON lp.Id = r.GameProgressId
WHERE r.Status <> -1 AND lp.BattlefieldVersion IS NOT NULL;

-- 純量寫入 PlayerParams（Group = game_progress，Sequence 留空）
INSERT INTO PlayerParams (Id, [Group], Name, Value, Sequence, CreateDate, CreateUserId, Status)
SELECT NEWID(), 'game_progress', v.Name, v.Value, NULL, lp.CreateDate, lp.CreateUserId, 1
FROM #LiveProgress lp
CROSS APPLY (VALUES
    ('version', CAST(lp.Version AS nvarchar(max))),
    ('wave', CAST(lp.Wave AS nvarchar(max))),
    ('gold', CAST(lp.Gold AS nvarchar(max))),
    ('max_wave_reached', CAST(lp.MaxWaveReached AS nvarchar(max))),
    ('difficulty', CAST(lp.Difficulty AS nvarchar(max))),
    ('lifetime_enemies_defeated', CAST(lp.LifetimeEnemiesDefeated AS nvarchar(max))),
    ('infinite_level', CAST(lp.InfiniteLevel AS nvarchar(max))),
    ('wave_completed', CASE WHEN lp.WaveCompleted = 1 THEN N'True' ELSE N'False' END)
) v(Name, Value);

-- 戰場鍵只在有戰場時寫入；缺值的欄位不寫，讀取端會以 0 代替
INSERT INTO PlayerParams (Id, [Group], Name, Value, Sequence, CreateDate, CreateUserId, Status)
SELECT NEWID(), 'game_progress', v.Name, v.Value, NULL, lp.CreateDate, lp.CreateUserId, 1
FROM #LiveProgress lp
CROSS APPLY (VALUES
    ('battlefield_version', CAST(lp.BattlefieldVersion AS nvarchar(max))),
    ('battlefield_columns', CAST(lp.BattlefieldColumns AS nvarchar(max))),
    ('battlefield_rows', CAST(lp.BattlefieldRows AS nvarchar(max))),
    ('castle_x', CAST(lp.CastleX AS nvarchar(max))),
    ('castle_y', CAST(lp.CastleY AS nvarchar(max)))
) v(Name, Value)
WHERE lp.BattlefieldVersion IS NOT NULL AND v.Value IS NOT NULL;

-- 設定列：群組加上 game_progress. 前綴，同一玩家同群組同 key 只取一筆
INSERT INTO PlayerParams (Id, [Group], Name, Value, Sequence, CreateDate, CreateUserId, Status)
SELECT NEWID(), 'game_progress.' + s.SettingGroup, s.SettingKey,
       CASE WHEN NULLIF(LTRIM(RTRIM(s.Value)), '') IS NULL THEN 'null' ELSE s.Value END,
       s.Sequence, lp.CreateDate, lp.CreateUserId, 1
FROM (
    SELECT st.*,
           ROW_NUMBER() OVER (PARTITION BY st.GameProgressId, st.SettingGroup, st.SettingKey ORDER BY st.Sequence, st.Id) AS Rn
    FROM GameProgressSetting st
    WHERE st.Status <> -1 AND st.SettingGroup IS NOT NULL AND st.SettingKey IS NOT NULL
) s
JOIN #LiveProgress lp ON lp.Id = s.GameProgressId
WHERE s.Rn = 1;

-- 角色武器（Scope = 0）：每位玩家每個角色一筆，有值者優先，空值存為 NULL
INSERT INTO CharacterWeapon (Id, CharacterKind, EntryKey, Sequence, CreateDate, CreateUserId, Status)
SELECT NEWID(), c.ScopeKey, c.EntryKey, NULL, c.CreateDate, c.OwnerId, 1
FROM (
    SELECT w.ScopeKey, NULLIF(w.EntryKey, '') AS EntryKey, w.CreateDate, lp.CreateUserId AS OwnerId,
           ROW_NUMBER() OVER (PARTITION BY lp.CreateUserId, w.ScopeKey
                              ORDER BY CASE WHEN NULLIF(w.EntryKey, '') IS NULL THEN 1 ELSE 0 END, w.Id) AS Rn
    FROM GameProgressWeapon w
    JOIN #LiveProgress lp ON lp.Id = w.GameProgressId
    WHERE w.Scope = 0 AND w.Status <> -1
) c
WHERE c.Rn = 1;

-- 部署：歸屬改為玩家，並帶入部署武器（Scope = 1，ScopeKey 對應部署順序）
UPDATE d SET
       d.CreateUserId = ld.OwnerId,
       d.WeaponEntryKey = w.EntryKey,
       d.WeaponQualityVersion = w.QualityVersion
FROM GameProgressDeployment d
JOIN #LiveDeployment ld ON ld.Id = d.Id
LEFT JOIN (
    SELECT x.GameProgressId, x.ScopeKey, x.EntryKey, x.QualityVersion
    FROM (
        SELECT gw.GameProgressId, gw.ScopeKey, NULLIF(gw.EntryKey, '') AS EntryKey, gw.QualityVersion,
               ROW_NUMBER() OVER (PARTITION BY gw.GameProgressId, gw.ScopeKey
                                  ORDER BY CASE WHEN NULLIF(gw.EntryKey, '') IS NULL THEN 1 ELSE 0 END, gw.Id) AS Rn
        FROM GameProgressWeapon gw
        WHERE gw.Scope = 1 AND gw.Status <> -1
    ) x
    WHERE x.Rn = 1
) w ON w.GameProgressId = ld.GameProgressId AND w.ScopeKey = ld.Sequence;

UPDATE GameProgressDeployment SET Status = -1
 WHERE Status <> -1 AND Id NOT IN (SELECT Id FROM #LiveDeployment);

-- 技能、節點、道路：依父層歸屬；無法回溯到有效父層者軟刪除
UPDATE sk SET sk.CreateUserId = ls.OwnerId
FROM GameProgressDeploymentSkill sk
JOIN #LiveSkill ls ON ls.Id = sk.Id;

UPDATE GameProgressDeploymentSkill SET Status = -1
 WHERE Status <> -1 AND Id NOT IN (SELECT Id FROM #LiveSkill);

UPDATE n SET n.CreateUserId = ln.OwnerId
FROM GameProgressDeploymentSkillNode n
JOIN #LiveNode ln ON ln.Id = n.Id;

UPDATE GameProgressDeploymentSkillNode SET Status = -1
 WHERE Status <> -1 AND Id NOT IN (SELECT Id FROM #LiveNode);

UPDATE r SET r.CreateUserId = lr.OwnerId
FROM GameProgressBattlefieldRoad r
JOIN #LiveRoad lr ON lr.Id = r.Id;

UPDATE GameProgressBattlefieldRoad SET Status = -1
 WHERE Status <> -1 AND Id NOT IN (SELECT Id FROM #LiveRoad);

DROP TABLE #LiveRoad;
DROP TABLE #LiveNode;
DROP TABLE #LiveSkill;
DROP TABLE #LiveDeployment;
DROP TABLE #LiveProgress;
");

            migrationBuilder.DropIndex(
                name: "IX_GameProgressDeployment_GameProgressId",
                table: "GameProgressDeployment");

            migrationBuilder.DropIndex(
                name: "IX_GameProgressBattlefieldRoad_GameProgressId",
                table: "GameProgressBattlefieldRoad");

            migrationBuilder.DropColumn(
                name: "GameProgressId",
                table: "GameProgressDeployment");

            migrationBuilder.DropColumn(
                name: "GameProgressId",
                table: "GameProgressBattlefieldRoad");

            migrationBuilder.DropTable(
                name: "GameProgressWeaponUniqueValue");

            migrationBuilder.DropTable(
                name: "GameProgressWeaponAffixValue");

            migrationBuilder.DropTable(
                name: "GameProgressWeaponAffix");

            migrationBuilder.DropTable(
                name: "GameProgressWeapon");

            migrationBuilder.DropTable(
                name: "GameProgressSetting");

            migrationBuilder.DropTable(
                name: "GameProgress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GameProgressId",
                table: "GameProgressDeployment",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GameProgressId",
                table: "GameProgressBattlefieldRoad",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GameProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    BattlefieldColumns = table.Column<int>(type: "int", nullable: true),
                    BattlefieldRows = table.Column<int>(type: "int", nullable: true),
                    BattlefieldVersion = table.Column<int>(type: "int", nullable: true),
                    CastleX = table.Column<int>(type: "int", nullable: true),
                    CastleY = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Difficulty = table.Column<int>(type: "int", nullable: false),
                    Gold = table.Column<long>(type: "bigint", nullable: false),
                    InfiniteLevel = table.Column<int>(type: "int", nullable: false),
                    LifetimeEnemiesDefeated = table.Column<long>(type: "bigint", nullable: false),
                    MaxWaveReached = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Wave = table.Column<int>(type: "int", nullable: false),
                    WaveCompleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GameProgressId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    SettingGroup = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettingKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressSetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressWeapon",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntryKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GameProgressId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityVersion = table.Column<int>(type: "int", nullable: true),
                    Rarity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    ScopeKey = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    UniqueKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WeaponKey = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressWeapon", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressWeaponAffix",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    AffixKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GameProgressWeaponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Rolled = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressWeaponAffix", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressWeaponAffixValue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GameProgressWeaponAffixId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValueKey = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressWeaponAffixValue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressWeaponUniqueValue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GameProgressWeaponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValueKey = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressWeaponUniqueValue", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressDeployment_GameProgressId",
                table: "GameProgressDeployment",
                column: "GameProgressId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressBattlefieldRoad_GameProgressId",
                table: "GameProgressBattlefieldRoad",
                column: "GameProgressId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgress_CreateUserId",
                table: "GameProgress",
                column: "CreateUserId",
                unique: true,
                filter: "[Status] <> -1");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressSetting_CreateUserId",
                table: "GameProgressSetting",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressSetting_GameProgressId",
                table: "GameProgressSetting",
                column: "GameProgressId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeapon_CreateUserId",
                table: "GameProgressWeapon",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeapon_GameProgressId",
                table: "GameProgressWeapon",
                column: "GameProgressId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponAffix_CreateUserId",
                table: "GameProgressWeaponAffix",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponAffix_GameProgressWeaponId",
                table: "GameProgressWeaponAffix",
                column: "GameProgressWeaponId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponAffixValue_CreateUserId",
                table: "GameProgressWeaponAffixValue",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponAffixValue_GameProgressWeaponAffixId",
                table: "GameProgressWeaponAffixValue",
                column: "GameProgressWeaponAffixId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponUniqueValue_CreateUserId",
                table: "GameProgressWeaponUniqueValue",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponUniqueValue_GameProgressWeaponId",
                table: "GameProgressWeaponUniqueValue",
                column: "GameProgressWeaponId");

            // 還原前提：上方已重建舊表與 GameProgressId 欄位；此時 CharacterWeapon 與裝備欄位仍存在
            migrationBuilder.Sql(@"
-- 由 PlayerParams 的 game_progress 純量還原 GameProgress，每位玩家一筆
SELECT NEWID() AS GameProgressId, x.CreateUserId,
       ISNULL(CAST(x.[version] AS int), 0) AS Version,
       ISNULL(CAST(x.[wave] AS int), 0) AS Wave,
       ISNULL(CAST(x.[gold] AS bigint), 0) AS Gold,
       ISNULL(CAST(x.[max_wave_reached] AS int), 0) AS MaxWaveReached,
       ISNULL(CAST(x.[difficulty] AS int), 0) AS Difficulty,
       ISNULL(CAST(x.[lifetime_enemies_defeated] AS bigint), 0) AS LifetimeEnemiesDefeated,
       ISNULL(CAST(x.[infinite_level] AS int), 0) AS InfiniteLevel,
       CASE WHEN x.[wave_completed] = 'True' THEN 1 ELSE 0 END AS WaveCompleted,
       CAST(x.[battlefield_version] AS int) AS BattlefieldVersion,
       CAST(x.[battlefield_columns] AS int) AS BattlefieldColumns,
       CAST(x.[battlefield_rows] AS int) AS BattlefieldRows,
       CAST(x.[castle_x] AS int) AS CastleX,
       CAST(x.[castle_y] AS int) AS CastleY
INTO #RestoredProgress
FROM (
    SELECT CreateUserId,
           MAX(CASE WHEN Name = 'version' THEN Value END) AS [version],
           MAX(CASE WHEN Name = 'wave' THEN Value END) AS [wave],
           MAX(CASE WHEN Name = 'gold' THEN Value END) AS [gold],
           MAX(CASE WHEN Name = 'max_wave_reached' THEN Value END) AS [max_wave_reached],
           MAX(CASE WHEN Name = 'difficulty' THEN Value END) AS [difficulty],
           MAX(CASE WHEN Name = 'lifetime_enemies_defeated' THEN Value END) AS [lifetime_enemies_defeated],
           MAX(CASE WHEN Name = 'infinite_level' THEN Value END) AS [infinite_level],
           MAX(CASE WHEN Name = 'wave_completed' THEN Value END) AS [wave_completed],
           MAX(CASE WHEN Name = 'battlefield_version' THEN Value END) AS [battlefield_version],
           MAX(CASE WHEN Name = 'battlefield_columns' THEN Value END) AS [battlefield_columns],
           MAX(CASE WHEN Name = 'battlefield_rows' THEN Value END) AS [battlefield_rows],
           MAX(CASE WHEN Name = 'castle_x' THEN Value END) AS [castle_x],
           MAX(CASE WHEN Name = 'castle_y' THEN Value END) AS [castle_y]
    FROM PlayerParams
    WHERE Status <> -1 AND [Group] = 'game_progress' AND CreateUserId IS NOT NULL
    GROUP BY CreateUserId
    HAVING MAX(CASE WHEN Name = 'version' THEN 1 END) = 1
) x;

INSERT INTO GameProgress (Id, Version, Wave, Gold, MaxWaveReached, Difficulty, LifetimeEnemiesDefeated, InfiniteLevel,
                          WaveCompleted, BattlefieldVersion, BattlefieldColumns, BattlefieldRows, CastleX, CastleY,
                          CreateDate, CreateUserId, Sequence, Status)
SELECT GameProgressId, Version, Wave, Gold, MaxWaveReached, Difficulty, LifetimeEnemiesDefeated, InfiniteLevel,
       WaveCompleted, BattlefieldVersion, BattlefieldColumns, BattlefieldRows, CastleX, CastleY,
       GETDATE(), CreateUserId, NULL, 1
FROM #RestoredProgress;

-- 部署與道路依歸屬玩家回連 GameProgressId
UPDATE d SET d.GameProgressId = rp.GameProgressId
FROM GameProgressDeployment d
JOIN #RestoredProgress rp ON rp.CreateUserId = d.CreateUserId
WHERE d.Status <> -1;

UPDATE r SET r.GameProgressId = rp.GameProgressId
FROM GameProgressBattlefieldRoad r
JOIN #RestoredProgress rp ON rp.CreateUserId = r.CreateUserId
WHERE r.Status <> -1;

-- 設定群組去掉 game_progress. 前綴後還原
INSERT INTO GameProgressSetting (Id, CreateDate, CreateUserId, GameProgressId, SettingGroup, SettingKey, Value, Sequence, Status)
SELECT NEWID(), GETDATE(), p.CreateUserId, rp.GameProgressId, SUBSTRING(p.[Group], 15, 4000), p.Name, p.Value, p.Sequence, 1
FROM PlayerParams p
JOIN #RestoredProgress rp ON rp.CreateUserId = p.CreateUserId
WHERE p.Status <> -1 AND p.[Group] LIKE 'game[_]progress.%';

-- 角色武器（Scope = 0）與部署武器（Scope = 1）還原為 GameProgressWeapon
SELECT NEWID() AS SlotId, cw.CreateUserId AS OwnerId, rp.GameProgressId, 0 AS Scope, cw.CharacterKind AS ScopeKey,
       cw.EntryKey, CAST(NULL AS int) AS QualityVersion, cw.CreateDate
INTO #WeaponSlot
FROM CharacterWeapon cw
JOIN #RestoredProgress rp ON rp.CreateUserId = cw.CreateUserId
WHERE cw.Status <> -1
UNION ALL
SELECT NEWID(), d.CreateUserId, d.GameProgressId, 1, ISNULL(d.Sequence, 0), d.WeaponEntryKey, d.WeaponQualityVersion, d.CreateDate
FROM GameProgressDeployment d
WHERE d.Status <> -1 AND d.GameProgressId IS NOT NULL;

INSERT INTO GameProgressWeapon (Id, CreateDate, CreateUserId, EntryKey, GameProgressId, QualityVersion, Rarity, Scope, ScopeKey,
                                Sequence, Status, UniqueKey, WeaponKey)
SELECT ws.SlotId, ws.CreateDate, ws.OwnerId, ISNULL(ws.EntryKey, ''), ws.GameProgressId, ws.QualityVersion,
       ISNULL(wp.Rarity, ''), ws.Scope, ws.ScopeKey, NULL, 1, ISNULL(wp.UniqueKey, ''), ISNULL(wp.WeaponKey, '')
FROM #WeaponSlot ws
LEFT JOIN Weapon wp ON wp.CreateUserId = ws.OwnerId AND wp.EntryKey = ws.EntryKey AND wp.Status <> -1;

-- 詞綴依武器的 (玩家, EntryKey) 對應到每個槽位各自複製一份
SELECT NEWID() AS NewAffixId, a.Id AS OldAffixId, ws.SlotId, ws.OwnerId, a.AffixKey, a.Rolled, a.Sequence, a.CreateDate
INTO #AffixMap
FROM WeaponAffix a
JOIN Weapon wp ON wp.Id = a.WeaponId AND wp.Status <> -1
JOIN #WeaponSlot ws ON ws.OwnerId = wp.CreateUserId AND ws.EntryKey = wp.EntryKey
WHERE a.Status <> -1;

INSERT INTO GameProgressWeaponAffix (Id, AffixKey, CreateDate, CreateUserId, GameProgressWeaponId, Rolled, Sequence, Status)
SELECT am.NewAffixId, am.AffixKey, am.CreateDate, am.OwnerId, am.SlotId, am.Rolled, am.Sequence, 1
FROM #AffixMap am;

INSERT INTO GameProgressWeaponAffixValue (Id, CreateDate, CreateUserId, GameProgressWeaponAffixId, Sequence, Status, Value, ValueKey)
SELECT NEWID(), v.CreateDate, am.OwnerId, am.NewAffixId, v.Sequence, 1, v.Value, v.ValueKey
FROM WeaponAffixValue v
JOIN #AffixMap am ON am.OldAffixId = v.WeaponAffixId
WHERE v.Status <> -1;

INSERT INTO GameProgressWeaponUniqueValue (Id, CreateDate, CreateUserId, GameProgressWeaponId, Sequence, Status, Value, ValueKey)
SELECT NEWID(), u.CreateDate, ws.OwnerId, ws.SlotId, u.Sequence, 1, u.Value, u.ValueKey
FROM WeaponUniqueValue u
JOIN Weapon wp ON wp.Id = u.WeaponId AND wp.Status <> -1
JOIN #WeaponSlot ws ON ws.OwnerId = wp.CreateUserId AND ws.EntryKey = wp.EntryKey
WHERE u.Status <> -1;

-- 還原完成後移除 PlayerParams 的 game_progress 資料，與服務端的整批覆寫行為一致
DELETE FROM PlayerParams
WHERE [Group] = 'game_progress' OR [Group] LIKE 'game[_]progress.%';

DROP TABLE #AffixMap;
DROP TABLE #WeaponSlot;
DROP TABLE #RestoredProgress;
");

            migrationBuilder.DropTable(
                name: "CharacterWeapon");

            migrationBuilder.DropColumn(
                name: "WeaponEntryKey",
                table: "GameProgressDeployment");

            migrationBuilder.DropColumn(
                name: "WeaponQualityVersion",
                table: "GameProgressDeployment");
        }
    }
}
