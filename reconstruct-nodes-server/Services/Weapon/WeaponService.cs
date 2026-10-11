using System.Globalization;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    public class WeaponService(BaseServiceArgument argument) : _ModelService<Weapon, Weapon>(argument)
    {
        private const string AffixTable = nameof(WeaponAffix);
        private const string AffixValueTable = nameof(WeaponAffixValue);
        private const string UniqueValueTable = nameof(WeaponUniqueValue);

        protected override string? NaturalKeyField => "EntryKey";

        public virtual async Task<IEnumerable<WeaponInstanceModel>> GetWeaponList()
        {
            string weaponSql = $@"SELECT *
                                    FROM {_tableName}
                                   WHERE Status != -1
                                     AND CreateUserId = @CreateUserId
                                ORDER BY CreateDate";
            List<Weapon> weapons = (await QueryAsync<Weapon>(weaponSql, new { CreateUserId = _playerId })).ToList();
            if (weapons.Count == 0) { return []; }

            List<Guid> weaponIds = weapons.Select(w => w.Id).ToList();

            string affixSql = $@"SELECT *
                                   FROM {AffixTable}
                                  WHERE WeaponId IN @WeaponIds
                                    AND CreateUserId = @CreateUserId
                               ORDER BY Sequence";
            List<WeaponAffix> affixes = (await QueryAsync<WeaponAffix>(affixSql, new { WeaponIds = weaponIds, CreateUserId = _playerId })).ToList();

            List<WeaponAffixValue> affixValues = [];
            List<Guid> affixIds = affixes.Select(a => a.Id).ToList();
            if (affixIds.Count > 0)
            {
                string valueSql = $@"SELECT *
                                       FROM {AffixValueTable}
                                      WHERE WeaponAffixId IN @AffixIds
                                        AND CreateUserId = @CreateUserId
                                   ORDER BY Sequence";
                affixValues = (await QueryAsync<WeaponAffixValue>(valueSql, new { AffixIds = affixIds, CreateUserId = _playerId })).ToList();
            }

            string uniqueSql = $@"SELECT *
                                    FROM {UniqueValueTable}
                                   WHERE WeaponId IN @WeaponIds
                                     AND CreateUserId = @CreateUserId
                                ORDER BY Sequence";
            List<WeaponUniqueValue> uniqueValues = (await QueryAsync<WeaponUniqueValue>(uniqueSql, new { WeaponIds = weaponIds, CreateUserId = _playerId })).ToList();

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
        public virtual async Task SaveWeapon(WeaponInstanceModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Id) || string.IsNullOrWhiteSpace(model.WeaponId))
            {
                throw new ArgumentException("武器實例代碼與武器代碼不可為空");
            }

            // 先完整解析再寫入，格式錯誤時不會留下部分資料
            List<WeaponAffixSnapshot> affixes = WeaponSnapshotCodec.ReadAffixes(model.Affixes ?? new());
            WeaponUniqueSnapshot? unique = WeaponSnapshotCodec.ReadUnique(model.Unique);

            // UPDLOCK + HOLDLOCK 鎖住既有列與不存在時的鍵區間，避免並行儲存重複插入同一把武器
            string lockSql = $@"SELECT TOP 1 *
                                  FROM {_tableName} WITH (UPDLOCK, HOLDLOCK)
                                 WHERE Status != -1
                                   AND CreateUserId = @CreateUserId
                                   AND EntryKey = @EntryKey";
            Weapon? existing = await QueryFirstAsync<Weapon?>(lockSql, new { CreateUserId = _playerId, EntryKey = model.Id });

            Weapon weapon = existing ?? new Weapon { EntryKey = model.Id, CreateUserId = _playerId };
            weapon.WeaponKey = model.WeaponId;
            weapon.WeaponFamilyKey = model.WeaponFamilyId;
            weapon.Rarity = model.Rarity;
            weapon.UniqueKey = unique?.UniqueKey;

            if (existing == null)
            {
                await InsertAsync(weapon);
            }
            else
            {
                string updateSql = $@"UPDATE {_tableName}
                                         SET WeaponKey = @WeaponKey,
                                             WeaponFamilyKey = @WeaponFamilyKey,
                                             Rarity = @Rarity,
                                             UniqueKey = @UniqueKey
                                       WHERE Id = @Id
                                         AND CreateUserId = @CreateUserId";
                await ExecuteAsync(updateSql, new
                {
                    weapon.Id,
                    CreateUserId = _playerId,
                    weapon.WeaponKey,
                    weapon.WeaponFamilyKey,
                    weapon.Rarity,
                    weapon.UniqueKey,
                });
                await DeleteChildrenAsync(weapon.Id);
            }

            await InsertChildrenAsync(weapon.Id, affixes, unique);
        }

        /// <summary>
        /// 刪除前先鎖玩家再檢查參照，與進度寫入序列化，避免參照與刪除交錯。子表保留，會因父表 Status 被過濾
        /// </summary>
        public override async Task<int> DeleteByNaturalKey(string naturalKey)
        {
            await LockPlayerAsync();
            await EnsureNotEquippedAsync(naturalKey);
            return await base.DeleteByNaturalKey(naturalKey);
        }

        /// <summary>
        /// 檢查武器是否仍被角色或部署裝備參照；參照表與進度讀取一致，不過濾 Status
        /// </summary>
        private async Task EnsureNotEquippedAsync(string entryKey)
        {
            string sql = $@"SELECT (SELECT COUNT(1)
                                      FROM {nameof(CharacterWeapon)}
                                     WHERE CreateUserId = @CreateUserId
                                       AND EntryKey = @EntryKey)
                                 + (SELECT COUNT(1)
                                      FROM {nameof(GameProgressDeployment)}
                                     WHERE CreateUserId = @CreateUserId
                                       AND WeaponEntryKey = @EntryKey)";
            int count = await QueryFirstAsync<int>(sql, new { CreateUserId = _playerId, EntryKey = entryKey });
            if (count > 0) { throw new ArgumentException("武器仍被角色或部署裝備中，無法刪除"); }
        }

        private async Task DeleteChildrenAsync(Guid weaponId)
        {
            string deleteValuesSql = $@"DELETE FROM {AffixValueTable}
                                         WHERE CreateUserId = @CreateUserId
                                           AND WeaponAffixId IN (SELECT Id
                                                                   FROM {AffixTable}
                                                                  WHERE WeaponId = @WeaponId
                                                                    AND CreateUserId = @CreateUserId)";
            await ExecuteAsync(deleteValuesSql, new { CreateUserId = _playerId, WeaponId = weaponId });

            await DeleteOwnedRowsAsync(AffixTable, "WeaponId", weaponId);
            await DeleteOwnedRowsAsync(UniqueValueTable, "WeaponId", weaponId);
        }

        private async Task InsertChildrenAsync(Guid weaponId, IReadOnlyList<WeaponAffixSnapshot> affixes, WeaponUniqueSnapshot? unique)
        {
            for (int i = 0; i < affixes.Count; i++)
            {
                WeaponAffixSnapshot snapshot = affixes[i];
                WeaponAffix affix = new WeaponAffix
                {
                    WeaponId = weaponId,
                    AffixKey = snapshot.AffixKey,
                    Rolled = snapshot.Rolled,
                    Sequence = i,
                    CreateUserId = _playerId,
                };
                await InsertAsync(affix);

                for (int j = 0; j < snapshot.Values.Count; j++)
                {
                    await InsertAsync(new WeaponAffixValue
                    {
                        WeaponAffixId = affix.Id,
                        ValueKey = j.ToString(CultureInfo.InvariantCulture),
                        Value = WeaponSnapshotCodec.FormatValue(snapshot.Values[j]),
                        Sequence = j,
                        CreateUserId = _playerId,
                    });
                }
            }

            if (unique == null) { return; }

            for (int j = 0; j < unique.Values.Count; j++)
            {
                await InsertAsync(new WeaponUniqueValue
                {
                    WeaponId = weaponId,
                    ValueKey = j.ToString(CultureInfo.InvariantCulture),
                    Value = WeaponSnapshotCodec.FormatValue(unique.Values[j]),
                    Sequence = j,
                    CreateUserId = _playerId,
                });
            }
        }
    }
}
