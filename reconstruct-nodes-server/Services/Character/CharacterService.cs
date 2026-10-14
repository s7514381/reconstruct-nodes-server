using System.Globalization;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

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
            await LockPlayerAsync();
            List<Character> characters = await QuerySnapshotRowsAsync<Character>(orderBy: row => row.Kind);
            // GetList/Get 已由 controller 包在交易內，玩家鎖讓新舊帳號的首次讀取互斥。
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
            var treesByCharacter = trees.ToLookup(tree => tree.CharacterId);
            var nodesByTree = nodes.ToLookup(node => node.CharacterSkillTreeId);
            var weapons = (await _lazy.WeaponService.Value.GetInstanceList()).ToDictionary(weapon => weapon.Id);

            return characters.Select(row =>
            {
                CharacterModel model = new()
                {
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

        protected override void ValidateSnapshot(CharacterModel model)
        {
            if (model.Kind is < 0 or > CoreKind) { throw new BusinessArgumentException(ApiMessageKeys.CharacterKindInvalid); }
            if (!model.Id.HasValue || model.Id == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned); }
            if (model.WeaponEntryId is { Length: > 450 } ||
                (!string.IsNullOrEmpty(model.WeaponEntryId) && string.IsNullOrWhiteSpace(model.WeaponEntryId)))
            {
                throw new BusinessArgumentException(ApiMessageKeys.WeaponIdInvalid);
            }
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
            await LockPlayerAsync();
            if (model.Id.HasValue)
            {
                var owned = (await QuerySnapshotRowsAsync<Character>(ids: [model.Id.Value])).SingleOrDefault();
                if (owned == null) { throw new BusinessArgumentException(ApiMessageKeys.CharacterNotFound); }
                if (owned.Kind != model.Kind) { throw new BusinessArgumentException(ApiMessageKeys.CharacterKindImmutable); }
            }
            await ValidateSkillAccessAsync(model);

            string? weaponKey = string.IsNullOrEmpty(model.WeaponEntryId) ? null : model.WeaponEntryId;
            if (weaponKey != null)
            {
                var weapons = await QueryAsync<Guid>(
                    "SELECT Id FROM Weapon WHERE CreateUserId = @CreateUserId AND Status != -1 AND EntryKey = @EntryKey",
                    new { CreateUserId = _playerId, EntryKey = weaponKey });
                if (!weapons.Any()) { throw new BusinessArgumentException(ApiMessageKeys.WeaponNotOwned); }
                var equippedKinds = await QueryAsync<int>(
                    "SELECT Kind FROM Character WHERE CreateUserId = @CreateUserId AND Status != -1 AND WeaponEntryKey = @EntryKey AND Kind != @Kind",
                    new { CreateUserId = _playerId, EntryKey = weaponKey, model.Kind });
                if (equippedKinds.Any()) { throw new BusinessArgumentException(ApiMessageKeys.WeaponAlreadyEquipped); }
            }

            (Character row, bool isNew) = await UpsertSnapshotParentAsync(character =>
            {
                if (model.Id.HasValue && model.Id != character.Id) { throw new BusinessArgumentException(ApiMessageKeys.CharacterKindMismatch); }
                character.Kind = model.Kind;
                character.WeaponEntryKey = weaponKey;
                character.WeaponQualityVersion = weaponKey == null ? null : model.WeaponQualityVersion;
            }, model.Kind.ToString(CultureInfo.InvariantCulture));
            if (!isNew)
            {
                await DeleteSnapshotDescendantsAsync<CharacterSkillNode, CharacterSkillTree, Character>(row.Id);
                await DeleteSnapshotChildrenAsync<CharacterSkillTree, Character>(row.Id);
            }
            List<GameProgressSkillLoadoutModel> trees = model.SkillTrees ?? new();
            await InsertSnapshotRowsAsync(trees, (input, _) => new CharacterSkillTree
                {
                    CharacterId = row.Id, Kind = input.Kind,
                }, async (input, tree) =>
                {
                    List<GameProgressSkillNodeModel> nodes = input.AuxiliaryNodes ?? new();
                    await InsertSnapshotRowsAsync(nodes, (node, _) => new CharacterSkillNode
                    {
                        CharacterSkillTreeId = tree.Id, NodeType = node.Type,
                        LootId = string.IsNullOrEmpty(node.LootId) ? null : node.LootId,
                        Kind = node.Kind,
                    });
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
