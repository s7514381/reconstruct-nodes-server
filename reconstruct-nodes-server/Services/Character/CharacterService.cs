using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;

namespace ReconstructNodesServer.Services
{
    public class CharacterService(BaseServiceArgument argument) : _SnapshotService<Character, CharacterModel>(argument)
    {
        public const int CoreKind = 3;
        // BulletData.FIREBALL -> ActiveSkill.FIREBALL（遊戲端穩定種類值）。
        public const int CoreDefaultSkillKind = 1;

        protected override string? NaturalKeyField => nameof(Character.Kind);

        public virtual async Task<List<CharacterModel>> GetList()
        {
            List<Character> characters = await QuerySnapshotRowsAsync<Character>(orderBy: row => row.Kind);
            // 唯一角色鍵保護首次初始化，Snapshot transaction 保持快照一致。
            // 只初始化不存在的核心；已保存的空技能與裝備都是有效配置。
            if (!characters.Any(row => row.Kind == CoreKind))
            {
                Character core = new() { Kind = CoreKind };
                await InsertSnapshotRowAsync(core);
                await InsertSnapshotRowAsync(new CharacterSkillTree
                {
                    CharacterId = core.Id, Kind = CoreDefaultSkillKind, Sequence = 0,
                });
                characters.Add(core);
            }

            Guid[] ids = characters.Select(row => row.Id).ToArray();
            var trees = await QuerySnapshotChildrenAsync<CharacterSkillTree, Character>(ids, activeChildrenOnly: true);
            var nodes = trees.Count == 0 ? new List<CharacterSkillNode>() :
                await QuerySnapshotChildrenAsync<CharacterSkillNode, CharacterSkillTree>(
                    trees.Select(tree => tree.Id).ToArray(), activeChildrenOnly: true);
            var weapons = (await _lazy.WeaponService.Value.GetInstanceList()).ToDictionary(weapon => weapon.Id);
            return BuildSnapshots(characters, trees, nodes, weapons, await Revisions.Read(nameof(CharacterModel)));
        }

        public Task<List<CharacterModel>> GetListCommand() => ReadSnapshotCommandAsync(GetListRead);

        public SnapshotRead<List<CharacterModel>> GetListRead => new($"""
            IF NOT EXISTS (SELECT 1 FROM Character WITH (UPDLOCK, HOLDLOCK) WHERE CreateUserId = @CreateUserId AND Status != -1 AND Kind = {CoreKind})
            BEGIN
                DECLARE @Core uniqueidentifier = NEWID();
                INSERT INTO Character (Id, CreateDate, CreateUserId, Status, Kind)
                VALUES (@Core, GETDATE(), @CreateUserId, {(int)EStatus.啟用}, {CoreKind});
                INSERT INTO CharacterSkillTree (Id, CreateDate, CreateUserId, Status, CharacterId, Kind, Sequence)
                VALUES (NEWID(), GETDATE(), @CreateUserId, {(int)EStatus.啟用}, @Core, {CoreDefaultSkillKind}, 0);
            END
            SELECT * FROM Character WHERE CreateUserId = @CreateUserId AND Status != -1 ORDER BY Kind;
            SELECT c.* FROM CharacterSkillTree c INNER JOIN Character p ON p.Id = c.CharacterId
            WHERE p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId
                AND p.Status != -1 AND c.Status != -1 ORDER BY c.Sequence;
            SELECT c.* FROM CharacterSkillNode c INNER JOIN CharacterSkillTree p ON p.Id = c.CharacterSkillTreeId
                INNER JOIN Character a ON a.Id = p.CharacterId
            WHERE p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId AND a.CreateUserId = @CreateUserId
                AND p.Status != -1 AND c.Status != -1 AND a.Status != -1 ORDER BY c.Sequence;
            SELECT w.* INTO #EquippedWeapons FROM Weapon w
            WHERE w.CreateUserId = @CreateUserId AND w.Status != -1
                AND EXISTS (SELECT 1 FROM Character c WHERE c.CreateUserId = @CreateUserId
                    AND c.Status != -1 AND c.WeaponEntryKey = w.EntryKey);
            SELECT * FROM #EquippedWeapons;
            SELECT c.* FROM WeaponAffix c INNER JOIN #EquippedWeapons p ON p.Id = c.WeaponId
            WHERE c.CreateUserId = @CreateUserId ORDER BY c.Sequence;
            SELECT c.* FROM WeaponAffixValue c INNER JOIN WeaponAffix p ON p.Id = c.WeaponAffixId
                INNER JOIN #EquippedWeapons w ON w.Id = p.WeaponId
            WHERE p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId ORDER BY c.Sequence;
            SELECT c.* FROM WeaponUniqueValue c INNER JOIN #EquippedWeapons p ON p.Id = c.WeaponId
            WHERE c.CreateUserId = @CreateUserId ORDER BY c.Sequence;
            DROP TABLE #EquippedWeapons;
            SELECT Value FROM PlayerParams WHERE CreateUserId = @CreateUserId
                AND [Group] = 'server.revisions' AND Name = 'CharacterModel' AND Status != -1;
            """, async reader =>
        {
            var characters = (await reader.ReadAsync<Character>()).ToList();
            var trees = await reader.ReadAsync<CharacterSkillTree>();
            var nodes = await reader.ReadAsync<CharacterSkillNode>();
            var weapons = await reader.ReadAsync<Weapon>();
            var affixes = await reader.ReadAsync<WeaponAffix>();
            var values = await reader.ReadAsync<WeaponAffixValue>();
            var unique = await reader.ReadAsync<WeaponUniqueValue>();
            string? revision = await reader.ReadSingleOrDefaultAsync<string>();
            return BuildSnapshots(characters, trees, nodes,
                WeaponService.BuildSnapshots(weapons, affixes, values, unique, 0).ToDictionary(weapon => weapon.Id),
                revision == null ? 0 : long.Parse(revision, System.Globalization.CultureInfo.InvariantCulture));
        });

        private static List<CharacterModel> BuildSnapshots(IEnumerable<Character> characters,
            IEnumerable<CharacterSkillTree> trees, IEnumerable<CharacterSkillNode> nodes,
            Dictionary<string, WeaponInstanceModel> weapons, long revision)
        {
            var treesByCharacter = trees.ToLookup(tree => tree.CharacterId);
            var nodesByTree = nodes.ToLookup(node => node.CharacterSkillTreeId);
            return characters.Select(row =>
            {
                CharacterModel model = new()
                {
                    Revision = revision,
                    Id = row.Id,
                    Kind = row.Kind,
                    WeaponQualityVersion = row.WeaponQualityVersion,
                    SkillTrees = treesByCharacter[row.Id].Select(tree => new GameProgressSkillLoadoutModel
                    {
                        Kind = tree.Kind,
                        AuxiliaryNodes = nodesByTree[tree.Id].Select(node => new GameProgressSkillNodeModel
                        {
                            Type = node.NodeType, LootId = node.LootId, Kind = node.Kind,
                        }).ToList(),
                    }).ToList(),
                };
                if (row.WeaponEntryKey != null)
                {
                    if (!weapons.TryGetValue(row.WeaponEntryKey, out WeaponInstanceModel? weapon))
                    {
                        throw new InvalidOperationException($"角色 {row.Id} 的裝備武器不存在");
                    }
                    model.WeaponEntryId = weapon.Id;
                    model.WeaponId = weapon.WeaponId;
                    model.WeaponRarity = weapon.Rarity;
                    model.WeaponAffixes = weapon.Affixes;
                    model.WeaponUnique = weapon.Unique;
                }
                return model;
            }).ToList();
        }

        public virtual async Task<CharacterModel?> Get(Guid id) =>
            (await GetList()).SingleOrDefault(row => row.Id == id);

        public override Task Save(CharacterModel model) => base.Save(model);

        public Task<CommandReceipt<CharacterModel>> EquipWeapon(WeaponEquipRequest request) =>
            Revisions.ExecuteIntent(request, "character.equip_weapon", nameof(CharacterModel), request, async () =>
            {
                CharacterModel model = await Get(request.CharacterId)
                    ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned);
                model.WeaponEntryId = request.WeaponEntryId ?? "";
                ValidateWeaponKey(model.WeaponEntryId);
                string? weaponKey = string.IsNullOrEmpty(model.WeaponEntryId) ? null : model.WeaponEntryId;
                await ValidateWeaponAsync(weaponKey, model.Kind);
                if (weaponKey != null)
                {
                    int owned = await ExecuteAsync("""
                        UPDATE Weapon SET Rarity = Rarity
                        WHERE CreateUserId = @CreateUserId AND EntryKey = @EntryKey AND Status != -1;
                        """, new { CreateUserId = _playerId, EntryKey = weaponKey });
                    if (owned != 1) { throw new BusinessArgumentException(ApiMessageKeys.WeaponNotOwned); }
                }
                // Update only equipment. Skill trees are never deleted/reinserted by this action.
                await ExecuteAsync(
                    "UPDATE Character SET WeaponEntryKey=@WeaponEntryKey, WeaponQualityVersion=@Quality WHERE Id=@Id AND CreateUserId=@CreateUserId AND Status != -1",
                    new { model.Id, CreateUserId = _playerId, WeaponEntryKey = weaponKey, Quality = weaponKey == null ? (int?)null : 2 });
                await Revisions.Advance(nameof(CharacterModel), [request.CharacterId.ToString("D")]);
                return (await Get(request.CharacterId))!;
            });

        private static void ValidateWeaponKey(string? key)
        {
            if (key is { Length: > 450 } || (!string.IsNullOrEmpty(key) && string.IsNullOrWhiteSpace(key)))
            { throw new BusinessArgumentException(ApiMessageKeys.WeaponIdInvalid); }
        }

        private async Task ValidateWeaponAsync(string? weaponKey, int kind)
        {
            if (weaponKey == null) { return; }
            var weapons = await QueryAsync<Guid>(
                "SELECT Id FROM Weapon WHERE CreateUserId=@CreateUserId AND Status != -1 AND EntryKey=@EntryKey",
                new { CreateUserId = _playerId, EntryKey = weaponKey });
            if (!weapons.Any()) { throw new BusinessArgumentException(ApiMessageKeys.WeaponNotOwned); }
            var equipped = await QueryAsync<int>(
                "SELECT Kind FROM Character WHERE CreateUserId=@CreateUserId AND Status != -1 AND WeaponEntryKey=@EntryKey AND Kind != @Kind",
                new { CreateUserId = _playerId, EntryKey = weaponKey, Kind = kind });
            if (equipped.Any()) { throw new BusinessArgumentException(ApiMessageKeys.WeaponAlreadyEquipped); }
        }

        protected override void ValidateSnapshot(CharacterModel model)
        {
            if (model.Kind is < 0 or > CoreKind) { throw new BusinessArgumentException(ApiMessageKeys.CharacterKindInvalid); }
            if (!model.Id.HasValue || model.Id == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned); }
            ValidateWeaponKey(model.WeaponEntryId);
            foreach (GameProgressSkillLoadoutModel? tree in model.SkillTrees ?? new())
            {
                if (tree == null) { throw new BusinessArgumentException(ApiMessageKeys.SkillTreeRequired); }
                foreach (GameProgressSkillNodeModel? node in tree.AuxiliaryNodes ?? new())
                {
                    if (node == null) { throw new BusinessArgumentException(ApiMessageKeys.SkillNodeRequired); }
                    if (node.Type == GameProgressSkillNodeModel.AuxiliaryType)
                    {
                        if (string.IsNullOrEmpty(node.LootId)) { throw new BusinessArgumentException(ApiMessageKeys.SkillLootRequired); }
                    }
                    else if (node.Type == GameProgressSkillNodeModel.ActiveType)
                    {
                        if (node.Kind == null) { throw new BusinessArgumentException(ApiMessageKeys.SkillKindRequired); }
                    }
                    else { throw new BusinessArgumentException(ApiMessageKeys.SkillNodeTypeInvalid); }
                }
            }
        }

        protected override async Task SaveSnapshot(CharacterModel model)
        {
            if (model.Id.HasValue)
            {
                var owned = (await QuerySnapshotRowsAsync<Character>(ids: [model.Id.Value])).SingleOrDefault();
                if (owned == null) { throw new BusinessArgumentException(ApiMessageKeys.CharacterNotFound); }
                if (owned.Kind != model.Kind) { throw new BusinessArgumentException(ApiMessageKeys.CharacterKindImmutable); }
                // Legacy character snapshots still own skill configuration, never equipment.
                model.WeaponEntryId = owned.WeaponEntryKey ?? "";
                model.WeaponQualityVersion = owned.WeaponQualityVersion;
            }
            await ValidateSkillAccessAsync(model);

            string? weaponKey = string.IsNullOrEmpty(model.WeaponEntryId) ? null : model.WeaponEntryId;
            await ValidateWeaponAsync(weaponKey, model.Kind);

            (Character row, bool isNew) = await UpsertSnapshotParentAsync(character =>
            {
                if (model.Id.HasValue && model.Id != character.Id) { throw new BusinessArgumentException(ApiMessageKeys.CharacterKindMismatch); }
                character.Kind = model.Kind;
                character.WeaponEntryKey = weaponKey;
                character.WeaponQualityVersion = weaponKey == null ? null : model.WeaponQualityVersion;
            }, model.Kind.ToString(CultureInfo.InvariantCulture));
            List<CharacterSkillTree> trees = [];
            List<CharacterSkillNode> nodes = [];
            foreach (GameProgressSkillLoadoutModel input in model.SkillTrees ?? [])
            {
                CharacterSkillTree tree = new()
                {
                    CharacterId = row.Id, Kind = input.Kind, Sequence = trees.Count,
                };
                trees.Add(tree);
                int sequence = 0;
                foreach (GameProgressSkillNodeModel node in input.AuxiliaryNodes ?? [])
                {
                    nodes.Add(new CharacterSkillNode
                    {
                        CharacterSkillTreeId = tree.Id, NodeType = node.Type,
                        LootId = string.IsNullOrEmpty(node.LootId) ? null : node.LootId,
                        Kind = node.Kind, Sequence = sequence++,
                    });
                }
            }
            await ExecuteAsync("""
                IF @ReplaceChildren = 1
                BEGIN
                    DELETE FROM CharacterSkillNode WHERE CreateUserId = @CreateUserId
                        AND CharacterSkillTreeId IN (
                            SELECT Id FROM CharacterSkillTree
                            WHERE CharacterId = @CharacterId AND CreateUserId = @CreateUserId);
                    DELETE FROM CharacterSkillTree WHERE CharacterId = @CharacterId AND CreateUserId = @CreateUserId;
                END;

                INSERT INTO CharacterSkillTree (Id, Sequence, CreateDate, CreateUserId, Status, CharacterId, Kind)
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, CharacterId, Kind
                FROM OPENJSON(@Trees) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    CharacterId uniqueidentifier, Kind int);

                INSERT INTO CharacterSkillNode (Id, Sequence, CreateDate, CreateUserId, Status, CharacterSkillTreeId, NodeType, LootId, Kind)
                SELECT Id, Sequence, CreateDate, @CreateUserId, Status, CharacterSkillTreeId, NodeType, LootId, Kind
                FROM OPENJSON(@Nodes) WITH (
                    Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                    CharacterSkillTreeId uniqueidentifier, NodeType nvarchar(max), LootId nvarchar(max), Kind int);
                """, new
            {
                CreateUserId = _playerId, CharacterId = row.Id, ReplaceChildren = !isNew,
                Trees = JsonSerializer.Serialize(trees), Nodes = JsonSerializer.Serialize(nodes),
            });
            model.Id = row.Id;
        }

        private async Task ValidateSkillAccessAsync(CharacterModel model)
        {
            if (model.SkillTrees == null || model.SkillTrees.Count == 0) { return; }
            int defaultSkill = model.Kind switch
            {
                0 => 5, 1 => 0, 2 => 3, CoreKind => CoreDefaultSkillKind,
                _ => throw new BusinessArgumentException(ApiMessageKeys.CharacterKindInvalid),
            };
            var rows = await QueryAsync<PlayerParams>(
                @"SELECT * FROM PlayerParams WHERE CreateUserId=@CreateUserId AND Status != -1
                  AND [Group] IN ('game_progress.unlocked_bullet_kinds','game_progress.unlocked_auxiliary_kinds')",
                new { CreateUserId = _playerId });
            var unlocked = rows.ToDictionary(row => (row.Group, row.Name), row => System.Text.Json.JsonSerializer.Deserialize<bool>(row.Value));
            bool ActiveAllowed(int kind) => kind is >= 0 and <= 6 &&
                (kind == 0 || kind == defaultSkill ||
                 unlocked.GetValueOrDefault(("game_progress.unlocked_bullet_kinds", kind.ToString(CultureInfo.InvariantCulture))));
            foreach (var tree in model.SkillTrees)
            {
                if (!ActiveAllowed(tree.Kind)) { throw new BusinessArgumentException(ApiMessageKeys.SkillLocked); }
                foreach (var node in tree.AuxiliaryNodes ?? new())
                {
                    if (node.Type == "active" && !ActiveAllowed(node.Kind!.Value))
                    {
                        throw new BusinessArgumentException(ApiMessageKeys.SkillActiveNodeLocked);
                    }
                    if (node.Type == "auxiliary" &&
                        !unlocked.GetValueOrDefault(("game_progress.unlocked_auxiliary_kinds", node.LootId)))
                    {
                        throw new BusinessArgumentException(ApiMessageKeys.SkillSupportNodeLocked);
                    }
                }
            }
        }
    }
}
