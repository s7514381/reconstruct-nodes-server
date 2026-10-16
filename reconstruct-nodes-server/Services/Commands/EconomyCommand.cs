using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services
{
    public class EconomyCommand(BaseServiceArgument argument, GoldCommand gold) : BaseService(argument)
    {
        private static readonly HashSet<string> AuxiliarySkills =
        [
            "auxiliary_gem", "explosion_gem", "multi_projectile_gem", "increased_area_gem",
            "increased_angle_gem", "fast_projectile_gem", "multi_strike_gem",
            "critical_trigger_gem", "hit_trigger_gem", "tracking_gem", "aura_support",
        ];

        /// <summary>
        /// 自動開波的批次寫入：active_wave 指向 sessionId，並寫入 wave_start 回執供 WaveComplete 驗證。
        /// 金幣不變動，只把目前餘額放進回傳值。
        /// </summary>
        internal static WaveStartResult QueueWaveStart(SqlWriteBatch writes, IEnumerable<PlayerParams> parameters,
            Guid sessionId, int wave, int difficulty, int infiniteLevel, long gold)
        {
            var result = new WaveStartResult
            {
                SessionId = sessionId, Wave = wave, Difficulty = difficulty, InfiniteLevel = infiniteLevel,
                EnemyCount = WaveEnemyCount(new WaveStartRequest { Wave = wave, Difficulty = difficulty }), Gold = gold,
            };
            // 與舊版 CONVERT(nvarchar(36), uniqueidentifier) 的大寫格式一致。
            string session = sessionId.ToString("D").ToUpperInvariant();
            PlayerParams? active = parameters.SingleOrDefault(row => row.Group == "economy" && row.Name == "active_wave");
            if (active == null) { writes.Insert([new PlayerParams { Group = "economy", Name = "active_wave", Value = session }]); }
            else { active.Value = session; writes.Update([active]); }
            writes.Insert([new EconomyRequest
            {
                RequestId = sessionId, Operation = "wave_start",
                Payload = JsonSerializer.Serialize(new { Wave = wave, Difficulty = difficulty }),
                Result = JsonSerializer.Serialize(result),
            }]);
            return result;
        }

        internal static int WaveEnemyCount(WaveStartRequest request)
        {
            if (request.Difficulty is < 0 or > 3 || request.Wave < 1 ||
                (request.Difficulty != 3 && request.Wave > 100))
            {
                throw new BusinessArgumentException(ApiMessageKeys.EconomyWaveInvalid);
            }
            return checked(6 + request.Wave * 3 + (request.Wave % 5 == 0 ? 1 : 0));
        }

        public async Task<GoldOperationResult> PurchaseSkill(SkillPurchaseRequest request)
        {
            string group;
            string name;
            if (request.Type == "active" && request.Kind is >= 0 and <= 6)
            {
                group = "game_progress.unlocked_bullet_kinds";
                name = request.Kind.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (request.Kind == 0) { throw new BusinessArgumentException(ApiMessageKeys.SkillBaseUnlocked); }
            }
            else if (request.Type == "auxiliary" && AuxiliarySkills.Contains(request.LootId))
            {
                group = "game_progress.unlocked_auxiliary_kinds";
                name = request.LootId;
            }
            else { throw new BusinessArgumentException(ApiMessageKeys.SkillMissing); }
            try
            {
                return await gold.ExecuteBatch<GoldOperationResult>(request.RequestId, "skill_purchase",
                    new { request.Type, request.Kind, request.LootId }, SkillPurchaseSql,
                    new { SkillGroup = group, SkillName = name, Cost = SkillCost });
            }
            catch (System.Data.SqlClient.SqlException error) when (error.Number == SkillUnlockedError)
            {
                throw new BusinessArgumentException(ApiMessageKeys.SkillUnlocked);
            }
            catch (System.Data.SqlClient.SqlException error) when (error.Number == InsufficientGoldError)
            {
                throw new BusinessOperationException(new ApiMessage(ApiMessageKeys.EconomyInsufficientGold,
                    new Dictionary<string, string> { ["cost"] = SkillCost.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            }
        }

        private const long SkillCost = 100;
        private const int InsufficientGoldError = 51005;
        private const int SkillUnlockedError = 51007;

        // 已解鎖先於金幣檢查；旗標列不存在時新增，值為 false 時改寫為 true。
        private const string SkillPurchaseSql = """
            DECLARE @SkillId uniqueidentifier, @SkillValue nvarchar(max);
            SELECT @SkillId = Id, @SkillValue = Value FROM PlayerParams
            WHERE CreateUserId = @CreateUserId AND Status != -1 AND [Group] = @SkillGroup AND Name = @SkillName;
            IF @@ROWCOUNT > 1 THROW 51004, 'Duplicate skill parameter', 1;
            IF @SkillValue IS NOT NULL AND LTRIM(RTRIM(@SkillValue)) NOT IN (N'true', N'false')
                THROW 51004, 'Invalid skill parameter', 1;
            IF LTRIM(RTRIM(@SkillValue)) = N'true' THROW 51007, 'Skill already unlocked', 1;
            IF @Balance < @Cost THROW 51005, 'Insufficient gold', 1;
            SET @Balance = @Balance - @Cost;
            IF @SkillId IS NULL
                INSERT INTO PlayerParams (Id, CreateDate, CreateUserId, Status, [Group], Name, Value)
                VALUES (NEWID(), @Now, @CreateUserId, @Enabled, @SkillGroup, @SkillName, N'true');
            ELSE UPDATE PlayerParams SET Value = N'true' WHERE Id = @SkillId AND CreateUserId = @CreateUserId;
            SET @Result = (SELECT @Balance AS gold FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
            """;
    }
}
