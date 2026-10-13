using ReconstructNodesServer.Library.Models;
using System.Text.Json;
using System.Globalization;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;

namespace ReconstructNodesServer.Services
{
    public class GoldService(BaseServiceArgument argument) : _ModelService<PlayerParams, PlayerParams>(argument)
    {
        public const long StartingGold = 1200;
        internal sealed class GoldBalance(long balance)
        {
            public long Balance { get; set; } = balance;
        }

        private async Task<PlayerParams?> GetGoldRow()
        {
            var rows = (await QueryAsync<PlayerParams>(
                @"SELECT * FROM PlayerParams WHERE CreateUserId = @CreateUserId
                  AND [Group] = 'game_progress' AND Name = 'gold' AND Status != -1",
                new { CreateUserId = _playerId })).ToList();
            if (rows.Count > 1) { throw new InvalidOperationException("玩家金幣參數重複"); }
            return rows.SingleOrDefault();
        }

        private static long ReadBalance(PlayerParams row)
        {
            if (!long.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long balance) || balance < 0)
            {
                throw new InvalidOperationException("玩家金幣參數無效");
            }
            return balance;
        }

        public async Task<long> GetBalance()
        {
            PlayerParams? row = await GetGoldRow();
            return row == null ? StartingGold : ReadBalance(row);
        }

        internal async Task<T> Execute<T>(Guid requestId, string operation, object payload, Func<GoldBalance, Task<T>> action)
            where T : class
        {
            if (requestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
            await LockPlayerAsync();
            string json = JsonSerializer.Serialize(payload);
            EconomyRequest? existing = await QueryFirstAsync<EconomyRequest?>(
                "SELECT * FROM EconomyRequest WHERE CreateUserId = @CreateUserId AND RequestId = @RequestId",
                new { CreateUserId = _playerId, RequestId = requestId });
            if (existing != null)
            {
                if (existing.Operation != operation || existing.Payload != json)
                {
                    throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict);
                }
                return JsonSerializer.Deserialize<T>(existing.Result)
                    ?? throw new InvalidOperationException("交易結果紀錄無效");
            }
            PlayerParams? row = await GetGoldRow();
            bool isNew = row == null;
            GoldBalance account = new(row == null ? StartingGold : ReadBalance(row));
            row ??= new PlayerParams { CreateUserId = _playerId, Group = "game_progress", Name = "gold" };
            T result = await action(account);
            if (account.Balance < 0) { throw new InvalidOperationException("金幣餘額不可為負"); }
            row.Value = account.Balance.ToString(CultureInfo.InvariantCulture);
            if (isNew) { await InsertAsync(row); }
            else { await UpdateAsync(row); }
            await InsertAsync(new EconomyRequest
            {
                RequestId = requestId, Operation = operation, Payload = json,
                Result = JsonSerializer.Serialize(result), CreateUserId = _playerId,
            });
            return result;
        }

        internal static void Spend(GoldBalance account, long cost)
        {
            if (cost <= 0) { throw new ArgumentOutOfRangeException(nameof(cost)); }
            if (account.Balance < cost) { throw new BusinessOperationException(new ApiMessage(ApiMessageKeys.EconomyInsufficientGold, new Dictionary<string, string> { ["cost"] = cost.ToString(System.Globalization.CultureInfo.InvariantCulture) })); }
            account.Balance -= cost;
        }
    }
}
