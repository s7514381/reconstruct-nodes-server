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
            if (request.Difficulty is < 0 or > 3 || request.Wave < 1 ||
                (request.Difficulty != 3 && request.Wave > 100))
            {
                throw new BusinessArgumentException(ApiMessageKeys.EconomyWaveInvalid);
            }
            int count = checked(6 + request.Wave * 3 + (request.Wave % 5 == 0 ? 1 : 0));
            return gold.Execute(request.RequestId, "wave_start", new { request.Wave, request.Difficulty }, async account =>
            {
                await WriteParameter("economy", "active_wave", request.RequestId.ToString());
                return new WaveStartResult
                {
                    SessionId = request.RequestId, Wave = request.Wave, EnemyCount = count, Gold = account.Balance,
                };
            });
        }

        public Task<GoldOperationResult> ReportKill(EnemyKillRequest request)
        {
            if (request.SessionId == Guid.Empty || request.EnemyIndex < 0) { throw new BusinessArgumentException(ApiMessageKeys.EconomyKillInvalid); }
            var payload = new { request.SessionId, request.EnemyIndex };
            return gold.Execute(request.RequestId, "enemy_kill", payload, async account =>
            {
                // A transition does not revoke rewards from the prior owned session.
                // Ownership, bounded enemy indices and the unique kill payload still apply.
                EconomyRequest? start = await QueryFirstAsync<EconomyRequest?>(
                    "SELECT * FROM EconomyRequest WHERE CreateUserId=@CreateUserId AND RequestId=@SessionId AND Operation='wave_start'",
                    new { CreateUserId = _playerId, request.SessionId });
                if (start == null) { throw new BusinessArgumentException(ApiMessageKeys.EconomySessionNotOwned); }
                var session = JsonSerializer.Deserialize<WaveStartResult>(start.Result)
                    ?? throw new InvalidOperationException("波次紀錄格式錯誤");
                if (request.EnemyIndex >= session.EnemyCount) { throw new BusinessArgumentException(ApiMessageKeys.EconomyEnemyInvalid); }
                var claims = await QueryAsync<Guid>(
                    "SELECT Id FROM EconomyRequest WHERE CreateUserId=@CreateUserId AND Operation='enemy_kill' AND Payload=@Payload",
                    new { CreateUserId = _playerId, Payload = JsonSerializer.Serialize(payload) });
                if (claims.Any()) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRewardClaimed); }
                long reward = checked(8L + session.Wave);
                if (session.Wave % 5 == 0 && request.EnemyIndex == session.EnemyCount - 1) { reward = checked(reward * 8); }
                var talents = await QueryAsync<TalentAcquired>(
                    @"SELECT * FROM TalentAcquired WHERE CreateUserId=@CreateUserId AND Status != -1
                      AND TreeId='utility' AND NodeId IN ('gold_1','gold_2','gold_3')",
                    new { CreateUserId = _playerId });
                foreach (var talent in talents)
                {
                    var level = JsonSerializer.Deserialize<JsonElement>(talent.Value);
                    bool acquired = level.ValueKind == JsonValueKind.True ||
                        (level.ValueKind == JsonValueKind.Number && level.GetInt32() > 0);
                    if (level.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number))
                    {
                        throw new InvalidOperationException("金幣天賦資料格式錯誤");
                    }
                    if (acquired) { reward += talent.NodeId switch { "gold_1" => 1, "gold_2" => 2, "gold_3" => 3, _ => 0 }; }
                }
                account.Balance = checked(account.Balance + reward);
                return new GoldOperationResult { Gold = account.Balance };
            });
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
