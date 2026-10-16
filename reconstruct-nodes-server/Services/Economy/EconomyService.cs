using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    public class EconomyService(BaseServiceArgument argument, GoldService gold) : _ModelService<PlayerParams, PlayerParams>(argument)
    {
        private static readonly HashSet<string> AuxiliarySkills =
        [
            "auxiliary_gem", "explosion_gem", "multi_projectile_gem", "increased_area_gem",
            "increased_angle_gem", "fast_projectile_gem", "multi_strike_gem",
            "critical_trigger_gem", "hit_trigger_gem", "tracking_gem", "aura_support",
        ];

        public Task<WaveStartResult> StartWave(WaveStartRequest request)
        {
            int count = WaveEnemyCount(request);
            return gold.ExecuteBatch<WaveStartResult>(request.RequestId, "wave_start",
                new { request.Wave, request.Difficulty }, """
                    DECLARE @ActiveId uniqueidentifier;
                    SELECT @ActiveId = Id FROM PlayerParams WHERE CreateUserId = @CreateUserId
                        AND Status != -1 AND [Group] = 'economy' AND Name = 'active_wave';
                    IF @@ROWCOUNT > 1 THROW 51004, 'Duplicate wave parameter', 1;
                    IF @ActiveId IS NULL
                        INSERT INTO PlayerParams (Id, CreateDate, CreateUserId, Status, [Group], Name, Value)
                        VALUES (NEWID(), @Now, @CreateUserId, @Enabled, 'economy', 'active_wave', CONVERT(nvarchar(36), @RequestId));
                    ELSE UPDATE PlayerParams SET Value = CONVERT(nvarchar(36), @RequestId)
                        WHERE Id = @ActiveId AND CreateUserId = @CreateUserId;
                    SET @Result = (SELECT @RequestId AS session_id, @Wave AS wave, @EnemyCount AS enemy_count,
                        @Difficulty AS difficulty, @InfiniteLevel AS infinite_level,
                        @Balance AS gold FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
                    """, new { request.Wave, request.Difficulty, request.InfiniteLevel, EnemyCount = count });
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

        public Task<GoldOperationResult> ReportKill(EnemyKillRequest request)
        {
            if (request.SessionId == Guid.Empty || request.EnemyIndex < 0) { throw new BusinessArgumentException(ApiMessageKeys.EconomyKillInvalid); }
            var payload = new { request.SessionId, request.EnemyIndex };
            return gold.ExecuteCommand(request.RequestId, "enemy_kill", payload, """
                    SELECT Result FROM EconomyRequest WHERE CreateUserId = @CreateUserId
                        AND RequestId = @SessionId AND Operation = 'wave_start';
                    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM EconomyRequest WHERE CreateUserId = @CreateUserId
                        AND Operation = 'enemy_kill' AND Payload = @Payload) THEN 1 ELSE 0 END AS bit);
                    SELECT * FROM TalentAcquired WHERE CreateUserId = @CreateUserId AND Status != -1
                        AND TreeId = 'utility' AND NodeId IN ('gold_1', 'gold_2', 'gold_3');
                    """, new { request.SessionId }, ReadKillContext, (account, context) =>
            {
                long reward = KillRewardRules.CalculateClaim(context.Session, context.Claimed, request.EnemyIndex, context.Talents);
                account.Balance = checked(account.Balance + reward);
                return new GoldOperationResult { Gold = account.Balance };
            });
        }

        private sealed record KillContext(string? Session, bool Claimed, List<TalentAcquired> Talents);

        private static async Task<KillContext> ReadKillContext(Repositories.ManagedGridReader reader)
        {
            string? session = await reader.ReadSingleOrDefaultAsync<string>();
            bool claimed = await reader.ReadSingleAsync<bool>();
            var talents = (await reader.ReadAsync<TalentAcquired>()).ToList();
            return new KillContext(session, claimed, talents);
        }

        public Task<GoldOperationResult> PurchaseSkill(SkillPurchaseRequest request)
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
            return gold.Execute(request.RequestId, "skill_purchase", new { request.Type, request.Kind, request.LootId }, async account =>
            {
                var existing = await ReadParameter(group, name);
                if (existing != null && JsonSerializer.Deserialize<bool>(existing.Value))
                {
                    throw new BusinessArgumentException(ApiMessageKeys.SkillUnlocked);
                }
                GoldService.Spend(account, 100);
                await WriteParameter(group, name, "true");
                return new GoldOperationResult { Gold = account.Balance };
            });
        }

        private async Task<PlayerParams?> ReadParameter(string group, string name)
        {
            var rows = (await QueryAsync<PlayerParams>(
                "SELECT * FROM PlayerParams WHERE CreateUserId=@CreateUserId AND Status != -1 AND [Group]=@Group AND Name=@Name",
                new { CreateUserId = _playerId, Group = group, Name = name })).ToList();
            if (rows.Count > 1) { throw new InvalidOperationException("玩家參數重複"); }
            return rows.SingleOrDefault();
        }

        private async Task WriteParameter(string group, string name, string value)
        {
            var row = await ReadParameter(group, name);
            if (row == null)
            {
                await InsertAsync(new PlayerParams { Group = group, Name = name, Value = value, CreateUserId = _playerId });
            }
            else
            {
                row.Value = value;
                await UpdateAsync(row);
            }
        }
    }
}
