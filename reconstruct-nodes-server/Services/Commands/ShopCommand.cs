using System.Security.Cryptography;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    public class ShopCommand : BaseService
    {
        private readonly GoldCommand gold;

        public ShopCommand(BaseServiceArgument argument, GoldCommand gold, ItemService items) : base(argument)
        {
            // Preserve the existing construction contract; adjustments now embed ItemService's shared SQL.
            this.gold = gold;
        }

        public record CharacterOffer(CharacterKind Kind, string LootId, string WeaponId);
        public record ShopCatalog(int SingleCost, int TenCost, IReadOnlyList<CharacterOffer> Characters);

        public static ShopCatalog Catalog { get; } = new(100, 900,
        [
            new(CharacterKind.Warrior, "warrior", "sword"),
            new(CharacterKind.Archer, "archer", "bow"),
            new(CharacterKind.Mage, "mage", "staff"),
            new(CharacterKind.Crusader, "crusader", "sword"),
            new(CharacterKind.ShadowMage, "shadow_mage", "staff"),
            new(CharacterKind.IceMage, "ice_mage", "staff"),
            new(CharacterKind.FireMage, "fire_mage", "staff"),
            new(CharacterKind.ShadowAssassin, "shadow_assassin", "sword"),
            new(CharacterKind.FlameSwordsman, "flame_swordsman", "sword"),
            new(CharacterKind.WindHunter, "wind_hunter", "bow"),
            new(CharacterKind.Ranger, "ranger", "bow"),
            new(CharacterKind.DesertRanger, "desert_ranger", "bow"),
        ]);

        public static readonly IReadOnlyList<CharacterKind> StarterKinds = [CharacterKind.Warrior, CharacterKind.Archer, CharacterKind.Mage];

        protected virtual int DrawIndex() => RandomNumberGenerator.GetInt32(Catalog.Characters.Count);
        protected virtual CharacterPersonality DrawPersonality() => RollPersonality();
        protected virtual CharacterFate DrawFate() => RollFate();
        public static CharacterPersonality RollPersonality() =>
            CharacterTraits.Personalities[RandomNumberGenerator.GetInt32(CharacterTraits.Personalities.Count)];
        public static CharacterFate RollFate() =>
            CharacterTraits.Fates[RandomNumberGenerator.GetInt32(CharacterTraits.Fates.Count)];

        private sealed record DrawCandidate(CharacterKind Kind, CharacterModel Character, WeaponInstanceModel Weapon);

        public virtual async Task<ShopDrawResult> Draw(ShopDrawRequest request)
        {
            if (request.Count is not (1 or 10)) { throw new BusinessArgumentException(ApiMessageKeys.ShopCountInvalid); }
            if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
            if (request.StarterKind is { } requested && (request.Count != 1 || !StarterKinds.Contains(requested)))
            {
                throw new BusinessArgumentException(ApiMessageKeys.ShopStarterInvalid);
            }
            var candidates = new List<DrawCandidate>(request.Count);
            for (int index = 0; index < request.Count; index++)
            {
                CharacterOffer offer = request.StarterKind is { } starter
                    ? Catalog.Characters.First(o => o.Kind == starter)
                    : Catalog.Characters[DrawIndex()];
                string weaponKey = $"shop_{Guid.NewGuid():N}";
                int skillKind = offer.Kind switch { CharacterKind.Warrior => 5, CharacterKind.Archer => 0, CharacterKind.Mage => 3,
                    CharacterKind.Crusader => 5,
                    CharacterKind.ShadowMage => 2,
                    CharacterKind.IceMage => 2,
                    CharacterKind.FireMage => 1,
                    CharacterKind.ShadowAssassin => 5,
                    CharacterKind.FlameSwordsman => 5,
                    CharacterKind.WindHunter => 0,
                    CharacterKind.Ranger => 0,
                    CharacterKind.DesertRanger => 0,
                    _ => throw new InvalidOperationException("Unsupported shop character kind") };
                candidates.Add(new(offer.Kind,
                    new CharacterModel
                    {
                        Id = Guid.NewGuid(), Kind = offer.Kind, WeaponEntryId = weaponKey,
                        Level = CharacterLevelRules.InitialLevel, Personality = DrawPersonality(), Fate = DrawFate(),
                        WeaponId = offer.WeaponId, WeaponRarity = "common", WeaponQualityVersion = 2,
                        SkillTrees = [new GameProgressSkillLoadoutModel { Kind = skillKind }],
                    },
                    new WeaponInstanceModel
                    {
                        Id = weaponKey, WeaponId = offer.WeaponId, WeaponFamilyId = offer.WeaponId, Rarity = "common",
                    }));
            }
            bool isStarter = request.StarterKind.HasValue;
            int cost = isStarter ? 0 : request.Count == 1 ? Catalog.SingleCost : Catalog.TenCost;
            // 一般抽取沿用原冪等 payload；新手選角另含指定種類，避免同 request_id 換種類被當成重送。
            object payload = isStarter ? new { request.Count, StarterKind = (int)request.StarterKind!.Value } : new { request.Count };
            try
            {
                // Rolls are candidates, not part of the idempotency payload. ExecuteBatch skips this
                // entire action on replay and returns the durable original result, including legacy receipts.
                return await gold.ExecuteBatch<ShopDrawResult>(request.RequestId, "shop_draw", payload,
                    """
                    IF @Starter = 1 AND EXISTS (SELECT 1 FROM Character
                        WHERE CreateUserId = @CreateUserId AND Status = @Enabled AND Kind <> @CoreKind)
                        THROW 51006, 'Starter pick unavailable', 1;
                    IF @Balance < @Cost THROW 51005, 'Insufficient gold', 1;
                    SET @Balance = @Balance - @Cost;
                    SET @Result = JSON_MODIFY(@EmptyResult, '$.gold', @Balance);
                    DECLARE @Rolls TABLE (Ordinal int PRIMARY KEY, Kind int, CharacterJson nvarchar(max), WeaponJson nvarchar(max));
                    INSERT INTO @Rolls
                    SELECT CONVERT(int, j.[key]), r.Kind, r.CharacterJson, r.WeaponJson
                    FROM OPENJSON(@Candidates) j
                    CROSS APPLY OPENJSON(j.value) WITH
                        (Kind int, CharacterJson nvarchar(max) '$.Character' AS JSON,
                         WeaponJson nvarchar(max) '$.Weapon' AS JSON) r;
                    DECLARE @Roll int = 0, @Kind int, @CharacterId uniqueidentifier,
                        @CharacterJson nvarchar(max), @WeaponJson nvarchar(max), @WeaponKey nvarchar(100),
                        @Reward nvarchar(max);
                    WHILE @Roll < @Count
                    BEGIN
                        SELECT @Kind = Kind, @CharacterJson = CharacterJson, @WeaponJson = WeaponJson
                        FROM @Rolls WHERE Ordinal = @Roll;
                        -- Every roll is a new, independent character instance; owning the same kind twice is valid.
                        SET @CharacterId = CONVERT(uniqueidentifier, JSON_VALUE(@CharacterJson, '$.id'));
                        SET @WeaponKey = JSON_VALUE(@WeaponJson, '$.id');
                        INSERT INTO Weapon (Id, CreateDate, CreateUserId, Status, EntryKey, WeaponKey, WeaponFamilyKey, Rarity)
                        VALUES (NEWID(), @Now, @CreateUserId, @Enabled, @WeaponKey,
                            JSON_VALUE(@WeaponJson, '$.weapon_id'), JSON_VALUE(@WeaponJson, '$.weapon_family_id'),
                            JSON_VALUE(@WeaponJson, '$.rarity'));
                        INSERT INTO Character (Id, CreateDate, CreateUserId, Status, Kind, WeaponEntryKey, WeaponQualityVersion,
                            Level, Personality, Fate)
                        VALUES (@CharacterId, @Now, @CreateUserId, @Enabled, @Kind, @WeaponKey,
                            CONVERT(int, JSON_VALUE(@CharacterJson, '$.weapon_quality_version')),
                            CONVERT(int, JSON_VALUE(@CharacterJson, '$.level')),
                            CONVERT(int, JSON_VALUE(@CharacterJson, '$.personality')),
                            CONVERT(int, JSON_VALUE(@CharacterJson, '$.fate')));
                        INSERT INTO CharacterSkillTree (Id, CreateDate, CreateUserId, Status, CharacterId, Kind, Sequence)
                        VALUES (NEWID(), @Now, @CreateUserId, @Enabled, @CharacterId,
                            CONVERT(int, JSON_VALUE(@CharacterJson, '$.skill_trees[0].kind')), 0);
                        SET @Result = JSON_MODIFY(@Result, 'append $.state.characters', JSON_QUERY(@CharacterJson));
                        SET @Result = JSON_MODIFY(@Result, 'append $.state.weapons', JSON_QUERY(@WeaponJson));
                        SET @Reward = (SELECT @CharacterId AS character_id, @Kind AS character_kind,
                            @WeaponKey AS weapon_entry_id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
                        SET @Result = JSON_MODIFY(@Result, 'append $.results', JSON_QUERY(@Reward));
                        SET @Roll = @Roll + 1;
                    END;
                    """,
                    new
                    {
                        Cost = cost, request.Count, Starter = isStarter ? 1 : 0, CoreKind = (int)CharacterKind.Core, Candidates = JsonSerializer.Serialize(candidates),
                        EmptyResult = JsonSerializer.Serialize(new ShopDrawResult { RequestId = request.RequestId, State = new() }),
                    });
            }
            catch (System.Data.SqlClient.SqlException error) when (error.Number == 51006)
            {
                throw new BusinessOperationException(new ApiMessage(ApiMessageKeys.ShopStarterUnavailable));
            }
            catch (System.Data.SqlClient.SqlException error) when (error.Number == 51005)
            {
                throw new BusinessOperationException(new ApiMessage(ApiMessageKeys.EconomyInsufficientGold,
                    new Dictionary<string, string> { ["cost"] = cost.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            }
        }
    }
}
