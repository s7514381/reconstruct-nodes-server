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

        public record CharacterOffer(int Kind, string LootId, string ShardId, string WeaponId);
        public record ShopCatalog(int SingleCost, int TenCost, IReadOnlyList<CharacterOffer> Characters);

        public static ShopCatalog Catalog { get; } = new(100, 900,
        [
            new(0, "warrior", "warrior_shard", "sword"),
            new(1, "archer", "archer_shard", "bow"),
            new(2, "mage", "mage_shard", "staff"),
        ]);

        protected virtual int DrawIndex() => RandomNumberGenerator.GetInt32(Catalog.Characters.Count);

        private sealed record DrawCandidate(int Kind, string ShardId, CharacterModel Character, WeaponInstanceModel Weapon);

        public virtual async Task<ShopDrawResult> Draw(ShopDrawRequest request)
        {
            if (request.Count is not (1 or 10)) { throw new BusinessArgumentException(ApiMessageKeys.ShopCountInvalid); }
            if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
            var candidates = new List<DrawCandidate>(request.Count);
            for (int index = 0; index < request.Count; index++)
            {
                CharacterOffer offer = Catalog.Characters[DrawIndex()];
                string weaponKey = $"shop_{Guid.NewGuid():N}";
                int skillKind = offer.Kind switch { 0 => 5, 1 => 0, 2 => 3,
                    _ => throw new InvalidOperationException("Unsupported shop character kind") };
                candidates.Add(new(offer.Kind, offer.ShardId,
                    new CharacterModel
                    {
                        Id = Guid.NewGuid(), Kind = offer.Kind, WeaponEntryId = weaponKey,
                        WeaponId = offer.WeaponId, WeaponRarity = "common", WeaponQualityVersion = 2,
                        SkillTrees = [new GameProgressSkillLoadoutModel { Kind = skillKind }],
                    },
                    new WeaponInstanceModel
                    {
                        Id = weaponKey, WeaponId = offer.WeaponId, WeaponFamilyId = offer.WeaponId, Rarity = "common",
                    }));
            }
            int cost = request.Count == 1 ? Catalog.SingleCost : Catalog.TenCost;
            try
            {
                // Rolls are candidates, not part of the idempotency payload. ExecuteBatch skips this
                // entire action on replay and returns the durable original result, including legacy receipts.
                return await gold.ExecuteBatch<ShopDrawResult>(request.RequestId, "shop_draw", new { request.Count },
                    """
                    IF @Balance < @Cost THROW 51005, 'Insufficient gold', 1;
                    SET @Balance = @Balance - @Cost;
                    SET @Result = JSON_MODIFY(@EmptyResult, '$.gold', @Balance);
                    IF EXISTS (SELECT Kind FROM Character WHERE CreateUserId = @CreateUserId AND Status != -1
                               GROUP BY Kind HAVING COUNT(*) > 1)
                        THROW 51004, 'Duplicate character ownership', 1;
                    DECLARE @Rolls TABLE (Ordinal int PRIMARY KEY, Kind int, ShardId nvarchar(100),
                        CharacterJson nvarchar(max), WeaponJson nvarchar(max));
                    INSERT INTO @Rolls
                    SELECT CONVERT(int, j.[key]), r.Kind, r.ShardId, r.CharacterJson, r.WeaponJson
                    FROM OPENJSON(@Candidates) j
                    CROSS APPLY OPENJSON(j.value) WITH
                        (Kind int, ShardId nvarchar(100), CharacterJson nvarchar(max) '$.Character' AS JSON,
                         WeaponJson nvarchar(max) '$.Weapon' AS JSON) r;
                    DECLARE @Roll int = 0, @Kind int, @CharacterId uniqueidentifier,
                        @CharacterJson nvarchar(max), @WeaponJson nvarchar(max), @WeaponKey nvarchar(100),
                        @Duplicate bit, @Reward nvarchar(max), @ShardPath nvarchar(200),
                        @ItemKey nvarchar(100), @Delta int = 1, @Category nvarchar(100) = N'character_shard',
                        @NewId uniqueidentifier, @CreateDate datetime = @Now, @Status int = @Enabled;
                    WHILE @Roll < @Count
                    BEGIN
                        SELECT @Kind = Kind, @ItemKey = ShardId, @CharacterJson = CharacterJson,
                            @WeaponJson = WeaponJson FROM @Rolls WHERE Ordinal = @Roll;
                        SET @CharacterId = NULL;
                        SET @WeaponKey = N'';
                        SELECT @CharacterId = Id FROM Character
                        WHERE CreateUserId = @CreateUserId AND Status != -1 AND Kind = @Kind;
                        SET @Duplicate = CASE WHEN @CharacterId IS NULL THEN 0 ELSE 1 END;
                        IF @Duplicate = 1
                        BEGIN
                            SET @NewId = NEWID();
                    """ + ItemService.AdjustmentSql + """
                            SET @ShardPath = N'$.state.shards."' + @ItemKey + N'"';
                            SET @Result = JSON_MODIFY(@Result, @ShardPath, @Amount);
                            -- The embedded adjustment variables are batch-scoped, even inside this loop.
                            SET @Id = NULL;
                            SET @Amount = NULL;
                            SET @ExistingCategory = NULL;
                        END
                        ELSE
                        BEGIN
                            SET @CharacterId = CONVERT(uniqueidentifier, JSON_VALUE(@CharacterJson, '$.id'));
                            SET @WeaponKey = JSON_VALUE(@WeaponJson, '$.id');
                            INSERT INTO Weapon (Id, CreateDate, CreateUserId, Status, EntryKey, WeaponKey, WeaponFamilyKey, Rarity)
                            VALUES (NEWID(), @Now, @CreateUserId, @Enabled, @WeaponKey,
                                JSON_VALUE(@WeaponJson, '$.weapon_id'), JSON_VALUE(@WeaponJson, '$.weapon_family_id'),
                                JSON_VALUE(@WeaponJson, '$.rarity'));
                            INSERT INTO Character (Id, CreateDate, CreateUserId, Status, Kind, WeaponEntryKey, WeaponQualityVersion)
                            VALUES (@CharacterId, @Now, @CreateUserId, @Enabled, @Kind, @WeaponKey,
                                CONVERT(int, JSON_VALUE(@CharacterJson, '$.weapon_quality_version')));
                            INSERT INTO CharacterSkillTree (Id, CreateDate, CreateUserId, Status, CharacterId, Kind, Sequence)
                            VALUES (NEWID(), @Now, @CreateUserId, @Enabled, @CharacterId,
                                CONVERT(int, JSON_VALUE(@CharacterJson, '$.skill_trees[0].kind')), 0);
                            SET @Result = JSON_MODIFY(@Result, 'append $.state.characters', JSON_QUERY(@CharacterJson));
                            SET @Result = JSON_MODIFY(@Result, 'append $.state.weapons', JSON_QUERY(@WeaponJson));
                        END;
                        SET @Reward = (SELECT @CharacterId AS character_id, @Kind AS character_kind,
                            @Duplicate AS is_duplicate, CASE WHEN @Duplicate = 1 THEN @ItemKey ELSE N'' END AS shard_id,
                            @WeaponKey AS weapon_entry_id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
                        SET @Result = JSON_MODIFY(@Result, 'append $.results', JSON_QUERY(@Reward));
                        SET @Roll = @Roll + 1;
                    END;
                    """,
                    new
                    {
                        Cost = cost, request.Count, Candidates = JsonSerializer.Serialize(candidates),
                        EmptyResult = JsonSerializer.Serialize(new ShopDrawResult { RequestId = request.RequestId, State = new() }),
                        Whitespace = ItemService.WhitespaceCharacters,
                    });
            }
            catch (System.Data.SqlClient.SqlException error) when (error.Number == 51005)
            {
                throw new BusinessOperationException(new ApiMessage(ApiMessageKeys.EconomyInsufficientGold,
                    new Dictionary<string, string> { ["cost"] = cost.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            }
            catch (System.Data.SqlClient.SqlException error) when (error.Number == 8115)
            {
                throw new OverflowException("Item amount overflowed.", error);
            }
        }
    }
}
