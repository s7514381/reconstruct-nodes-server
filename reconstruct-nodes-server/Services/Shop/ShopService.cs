using System.Security.Cryptography;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    public class ShopService(BaseServiceArgument argument, GoldService gold, ItemService items)
        : _ModelService<Character, Character>(argument)
    {
        public record CharacterOffer(int Kind, string LootId, string ShardId, string WeaponId);
        public record ShopCatalog(int SingleCost, int TenCost, IReadOnlyList<CharacterOffer> Characters);

        public static ShopCatalog Catalog { get; } = new(100, 900,
        [
            new(0, "warrior", "warrior_shard", "sword"),
            new(1, "archer", "archer_shard", "bow"),
            new(2, "mage", "mage_shard", "staff"),
        ]);

        protected virtual int DrawIndex() => RandomNumberGenerator.GetInt32(Catalog.Characters.Count);

        public virtual Task<ShopDrawResult> Draw(ShopDrawRequest request)
        {
            if (request.Count is not (1 or 10)) { throw new BusinessArgumentException(ApiMessageKeys.ShopCountInvalid); }
            return gold.Execute(request.RequestId, "shop_draw", new { request.Count }, async account =>
            {
                GoldService.Spend(account, request.Count == 1 ? Catalog.SingleCost : Catalog.TenCost);
                var owned = (await QueryAsync<Character>(
                    "SELECT * FROM Character WHERE CreateUserId = @CreateUserId AND Status != -1",
                    new { CreateUserId = _playerId })).ToDictionary(row => row.Kind);
                ShopDrawResult result = new() { RequestId = request.RequestId, Gold = account.Balance, State = new() };
                for (int index = 0; index < request.Count; index++)
                {
                    CharacterOffer offer = Catalog.Characters[DrawIndex()];
                    bool duplicate = owned.TryGetValue(offer.Kind, out Character? character);
                    string weaponKey = "";
                    if (duplicate)
                    {
                        result.State.Shards[offer.ShardId] = await items.AdjustAmount(offer.ShardId, 1, "character_shard");
                    }
                    else
                    {
                        weaponKey = $"shop_{Guid.NewGuid():N}";
                        await InsertAsync(new Weapon
                        {
                            EntryKey = weaponKey, WeaponKey = offer.WeaponId, WeaponFamilyKey = offer.WeaponId,
                            Rarity = "common", CreateUserId = _playerId,
                        });
                        character = new Character
                        {
                            Kind = offer.Kind, WeaponEntryKey = weaponKey,
                            WeaponQualityVersion = 2, CreateUserId = _playerId,
                        };
                        await InsertAsync(character);
                        int skillKind = offer.Kind switch { 0 => 5, 1 => 0, 2 => 3,
                            _ => throw new InvalidOperationException("Unsupported shop character kind") };
                        await InsertAsync(new CharacterSkillTree
                        {
                            CharacterId = character.Id, Kind = skillKind, Sequence = 0, CreateUserId = _playerId,
                        });
                        result.State.Characters.Add(new CharacterModel
                        {
                            Id = character.Id, Kind = offer.Kind, WeaponEntryId = weaponKey,
                            WeaponId = offer.WeaponId, WeaponRarity = "common", WeaponQualityVersion = 2,
                            SkillTrees = [new GameProgressSkillLoadoutModel { Kind = skillKind }],
                        });
                        result.State.Weapons.Add(new WeaponInstanceModel
                        {
                            Id = weaponKey, WeaponId = offer.WeaponId, WeaponFamilyId = offer.WeaponId, Rarity = "common",
                        });
                        owned.Add(offer.Kind, character);
                    }
                    result.Results.Add(new ShopDrawReward
                    {
                        CharacterId = character!.Id, CharacterKind = offer.Kind,
                        IsDuplicate = duplicate, ShardId = duplicate ? offer.ShardId : "", WeaponEntryId = weaponKey,
                    });
                }
                return result;
            });
        }
    }
}
