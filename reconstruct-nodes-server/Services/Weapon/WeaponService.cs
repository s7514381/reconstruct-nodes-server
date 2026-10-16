using System.Globalization;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    public partial class WeaponService(BaseServiceArgument argument, WeaponAffixService affixService,
        WeaponAffixValueService affixValueService, WeaponUniqueValueService uniqueValueService)
        : _SnapshotService<Weapon, WeaponInstanceModel>(argument)
    {
        protected override string? NaturalKeyField => "EntryKey";

        public virtual async Task<IEnumerable<WeaponInstanceModel>> GetInstanceList()
        {
            List<Weapon> weapons = await QueryOwnedRowsAsync(orderBy: row => row.CreateDate);
            if (weapons.Count == 0) { return []; }

            List<Guid> weaponIds = weapons.Select(w => w.Id).ToList();

            List<WeaponAffix> affixes = await affixService.QueryChildrenAsync<Weapon>(weaponIds);

            List<WeaponAffixValue> affixValues = [];
            List<Guid> affixIds = affixes.Select(a => a.Id).ToList();
            if (affixIds.Count > 0)
            {
                affixValues = await affixValueService.QueryChildrenAsync<WeaponAffix>(affixIds, activeParentsOnly: false);
            }

            List<WeaponUniqueValue> uniqueValues = await uniqueValueService.QueryChildrenAsync<Weapon>(weaponIds);

            return BuildSnapshots(weapons, affixes, affixValues, uniqueValues, await Revisions.Read(nameof(WeaponInstanceModel)));
        }

        public Task<IEnumerable<WeaponInstanceModel>> GetInstanceListCommand() =>
            ReadSnapshotCommandAsync(GetInstanceListRead);

        public SnapshotRead<IEnumerable<WeaponInstanceModel>> GetInstanceListRead =>
            new($"""
                {OwnedSql.Select<Weapon>(orderBy: "CreateDate")}
                {OwnedSql.Children<WeaponAffix, Weapon>()}
                {OwnedSql.Descendants<WeaponAffixValue, WeaponAffix, Weapon>()}
                {OwnedSql.Children<WeaponUniqueValue, Weapon>()}
                {RevisionCommand.SelectValueSql(nameof(WeaponInstanceModel))}
                """, async reader =>
            {
                var weapons = (await reader.ReadAsync<Weapon>()).ToList();
                var affixes = await reader.ReadAsync<WeaponAffix>();
                var values = await reader.ReadAsync<WeaponAffixValue>();
                var unique = await reader.ReadAsync<WeaponUniqueValue>();
                string? revision = await reader.ReadSingleOrDefaultAsync<string>();
                return BuildSnapshots(weapons, affixes, values, unique,
                    revision == null ? 0 : long.Parse(revision, CultureInfo.InvariantCulture));
            });

        internal static IEnumerable<WeaponInstanceModel> BuildSnapshots(IEnumerable<Weapon> weapons,
            IEnumerable<WeaponAffix> affixes, IEnumerable<WeaponAffixValue> affixValues,
            IEnumerable<WeaponUniqueValue> uniqueValues, long revision)
        {
            ILookup<Guid?, WeaponAffix> affixesByWeapon = affixes.ToLookup(a => a.WeaponId);
            ILookup<Guid?, WeaponAffixValue> valuesByAffix = affixValues.ToLookup(v => v.WeaponAffixId);
            ILookup<Guid?, WeaponUniqueValue> uniqueByWeapon = uniqueValues.ToLookup(u => u.WeaponId);

            return weapons.Select(weapon =>
            {
                List<WeaponAffixSnapshot> affixSnapshots = affixesByWeapon[weapon.Id]
                    .Select(a => new WeaponAffixSnapshot(
                        a.AffixKey,
                        a.Rolled,
                        valuesByAffix[a.Id].Select(v => WeaponSnapshotCodec.ParseValue(v.Value)).ToList()))
                    .ToList();

                WeaponUniqueSnapshot? unique = string.IsNullOrEmpty(weapon.UniqueKey)
                    ? null
                    : new WeaponUniqueSnapshot(
                        weapon.UniqueKey,
                        uniqueByWeapon[weapon.Id].Select(u => WeaponSnapshotCodec.ParseValue(u.Value)).ToList());

                return new WeaponInstanceModel
                {
                    Revision = revision,
                    Id = weapon.EntryKey,
                    WeaponId = weapon.WeaponKey,
                    WeaponFamilyId = weapon.WeaponFamilyKey,
                    Rarity = weapon.Rarity,
                    Affixes = WeaponSnapshotCodec.WriteAffixes(affixSnapshots),
                    Unique = WeaponSnapshotCodec.WriteUnique(unique),
                };
            }).ToList();
        }

        /// <summary>
        /// 單把武器 upsert：以 EntryKey 定位，子表整批刪除後重插。需在交易內呼叫。
        /// </summary>
        public override Task Save(WeaponInstanceModel model) => base.Save(model);

        // Only explicit commands may update existing instances. No client revision fence:
        // the command ledger and player lock already protect this transaction.
        internal async Task SaveForCommand(WeaponInstanceModel model, bool acquire = false)
        {
            ValidateSnapshot(model);
            if (acquire)
            {
                int count = await QueryFirstAsync<int>(
                    "SELECT COUNT(1) FROM Weapon WITH (UPDLOCK, HOLDLOCK) WHERE CreateUserId=@CreateUserId AND EntryKey=@EntryKey",
                    new { CreateUserId = _playerId, EntryKey = model.Id });
                // Include tombstones: a late acquire must never resurrect a disassembled entry.
                if (count != 0) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
            }
            await SaveSnapshot(model);
            await Revisions.Advance(nameof(WeaponInstanceModel), [model.Id]);
        }

        protected override void ValidateSnapshot(WeaponInstanceModel model)
            => ValidateWeapon(model);

        internal static void ValidateWeapon(WeaponInstanceModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Id) || model.Id.Length > 450 ||
                string.IsNullOrWhiteSpace(model.WeaponId) || model.WeaponId.Length > 450 ||
                model.WeaponFamilyId?.Length > 450)
            {
                throw new BusinessArgumentException(ApiMessageKeys.WeaponKeysRequired);
            }
            WeaponSnapshotCodec.ReadAffixes(model.Affixes ?? new());
            WeaponSnapshotCodec.ReadUnique(model.Unique);
        }

        protected override async Task SaveSnapshot(WeaponInstanceModel model)
        {
            // 先完整解析再寫入，格式錯誤時不會留下部分資料
            List<WeaponAffixSnapshot> affixes = WeaponSnapshotCodec.ReadAffixes(model.Affixes ?? new());
            WeaponUniqueSnapshot? unique = WeaponSnapshotCodec.ReadUnique(model.Unique);

            (Weapon weapon, bool isNew) = await UpsertSnapshotParentAsync(row =>
            {
                row.EntryKey = model.Id;
                row.WeaponKey = model.WeaponId;
                row.WeaponFamilyKey = model.WeaponFamilyId;
                row.Rarity = model.Rarity;
                row.UniqueKey = unique?.UniqueKey;
            }, model.Id, row => UpdateSnapshotFieldsAsync(row,
                w => w.WeaponKey, w => w.WeaponFamilyKey, w => w.Rarity, w => w.UniqueKey));

            if (!isNew)
            {
                await DeleteChildrenAsync(weapon.Id);
            }

            await InsertChildrenAsync(weapon.Id, affixes, unique);
        }

        /// <summary>
        /// 刪除前先鎖玩家再檢查角色裝備，避免裝備寫入與刪除交錯。子表保留，會因父表 Status 被過濾
        /// </summary>
        public override async Task<int> DeleteByNaturalKey(string naturalKey)
        {
            await Revisions.Check(nameof(WeaponInstanceModel), null);
            return await DeleteForCommand(naturalKey);
        }

        internal async Task<int> DeleteForCommand(string naturalKey)
        {
            await EnsureNotEquippedAsync(naturalKey);
            return await base.DeleteByNaturalKey(naturalKey);
        }

        /// <summary>
        /// 部署使用角色目前裝備，只需檢查有效角色的引用。
        /// </summary>
        private async Task EnsureNotEquippedAsync(string entryKey)
        {
            string sql = $@"SELECT COUNT(1) FROM {nameof(Character)}
                            WHERE CreateUserId = @CreateUserId
                              AND Status != -1 AND WeaponEntryKey = @EntryKey";
            int count = await QueryFirstAsync<int>(sql, new { CreateUserId = _playerId, EntryKey = entryKey });
            if (count > 0) { throw new BusinessArgumentException(ApiMessageKeys.WeaponInUse); }
        }

        private async Task DeleteChildrenAsync(Guid weaponId)
        {
            await affixValueService.DeleteDescendantsAsync<WeaponAffix, Weapon>(weaponId);
            await affixService.DeleteChildrenAsync<Weapon>(weaponId);
            await uniqueValueService.DeleteChildrenAsync<Weapon>(weaponId);
        }

        private async Task InsertChildrenAsync(Guid weaponId, IReadOnlyList<WeaponAffixSnapshot> affixes, WeaponUniqueSnapshot? unique)
        {
            await affixService.InsertOwnedRowsAsync(affixes, (snapshot, _) => new WeaponAffix
                {
                    WeaponId = weaponId,
                    AffixKey = snapshot.AffixKey,
                    Rolled = snapshot.Rolled,
                }, async (snapshot, affix) =>
                {
                    await affixValueService.InsertOwnedRowsAsync(snapshot.Values, (value, j) => new WeaponAffixValue
                    {
                        WeaponAffixId = affix.Id,
                        ValueKey = j.ToString(CultureInfo.InvariantCulture),
                        Value = WeaponSnapshotCodec.FormatValue(value),
                    });
                });

            if (unique == null) { return; }

            await uniqueValueService.InsertOwnedRowsAsync(unique.Values, (value, j) => new WeaponUniqueValue
                {
                    WeaponId = weaponId,
                    ValueKey = j.ToString(CultureInfo.InvariantCulture),
                    Value = WeaponSnapshotCodec.FormatValue(value),
                });
        }
    }
}
