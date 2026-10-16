using ReconstructNodesServer.Library.Models;
using System.Text.Json;
using System.Globalization;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services
{
    public class GoldCommand(BaseServiceArgument argument) : BaseService(argument)
    {
        public const long StartingGold = 1200;
        internal sealed class GoldBalance(long balance)
        {
            public long Balance { get; set; } = balance;
        }

        internal static readonly string GoldRowSql =
            OwnedSql.Select<PlayerParams>("[Group] = 'game_progress' AND Name = 'gold'");

        /// <summary>金幣加成天賦的節點條件，供 SELECT * FROM TalentAcquired 共用。</summary>
        internal const string GoldTalentsCondition = "TreeId = 'utility' AND NodeId IN ('gold_1', 'gold_2', 'gold_3')";

        private async Task<PlayerParams?> GetGoldRow()
        {
            var rows = (await QueryAsync<PlayerParams>(GoldRowSql, new { CreateUserId = _playerId })).ToList();
            if (rows.Count > 1) { throw new InvalidOperationException("玩家金幣參數重複"); }
            return rows.SingleOrDefault();
        }

        internal static long ReadBalance(PlayerParams row)
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

        internal static void Spend(GoldBalance account, long cost)
        {
            if (cost <= 0) { throw new ArgumentOutOfRangeException(nameof(cost)); }
            if (account.Balance < cost) { throw new BusinessOperationException(new ApiMessage(ApiMessageKeys.EconomyInsufficientGold, new Dictionary<string, string> { ["cost"] = cost.ToString(System.Globalization.CultureInfo.InvariantCulture) })); }
            account.Balance -= cost;
        }

        internal async Task<TResult> ExecuteCommand<TContext, TResult>(
            Guid requestId, string operation, object payload, string contextSql, object contextParameters,
            Func<ManagedGridReader, Task<TContext>> readContext, Func<GoldBalance, TContext, TResult> action)
            where TContext : class where TResult : class
        {
            if (requestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
            string json = JsonSerializer.Serialize(payload);
            var parameters = new Dapper.DynamicParameters(contextParameters);
            parameters.Add("CreateUserId", _playerId);
            parameters.Add("RequestId", requestId);
            parameters.Add("Payload", json);
            return await ExecuteCommandAsync(
                () => ReadBatchAsync(OwnedSql.RequireOwner("Economy")
                    + "\n" + OwnedSql.Select<EconomyRequest>("RequestId = @RequestId", activeOnly: false) + "\n" + GoldRowSql + "\n" + contextSql, parameters, async reader =>
                {
                    var existing = await reader.ReadSingleOrDefaultAsync<EconomyRequest>();
                    if (existing != null)
                    {
                        return (Receipt: existing, Row: (PlayerParams?)null, Context: (TContext?)null);
                    }
                    var row = await reader.ReadSingleOrDefaultAsync<PlayerParams>();
                    var context = await readContext(reader);
                    return (Receipt: (EconomyRequest?)null, Row: row, Context: (TContext?)context);
                }),
                async state =>
            {
                if (state.Receipt != null)
                {
                    if (state.Receipt.Operation != operation || state.Receipt.Payload != json)
                    {
                        throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict);
                    }
                    return JsonSerializer.Deserialize<TResult>(state.Receipt.Result)
                        ?? throw new InvalidOperationException("交易結果紀錄無效");
                }
                PlayerParams? row = state.Row;
                GoldBalance account = new(row == null ? StartingGold : ReadBalance(row));
                TResult result = action(account, state.Context ?? throw new InvalidOperationException("交易資料不可為空"))
                    ?? throw new InvalidOperationException("交易結果不可為空");
                var write = new Dapper.DynamicParameters();
                write.Add("CreateUserId", _playerId);
                write.Add("RequestId", requestId);
                write.Add("Operation", operation);
                write.Add("Payload", json);
                write.Add("GoldId", row?.Id);
                write.Add("Balance", account.Balance);
                write.Add("Result", JsonSerializer.Serialize(result));
                write.Add("Now", DateTime.Now);
                write.Add("Enabled", (int)ReconstructNodesServer.Library.Models.Enum.EStatus.啟用);
                await ExecuteAsync(PersistOperationSql, write);
                return result;
            });
        }

        private const string PersistOperationSql = """
            IF @Balance < 0 OR @Result IS NULL THROW 51004, 'Invalid economy result', 1;
            IF @GoldId IS NULL
                INSERT INTO PlayerParams (Id, CreateDate, CreateUserId, Status, [Group], Name, Value)
                VALUES (NEWID(), @Now, @CreateUserId, @Enabled, 'game_progress', 'gold', CONVERT(nvarchar(30), @Balance));
            ELSE
                UPDATE PlayerParams SET Value = CONVERT(nvarchar(30), @Balance)
                WHERE Id = @GoldId AND CreateUserId = @CreateUserId;
            INSERT INTO EconomyRequest (Id, CreateDate, CreateUserId, Status, RequestId, Operation, Payload, Result)
            VALUES (NEWID(), @Now, @CreateUserId, @Enabled, @RequestId, @Operation, @Payload, @Result);
            """;

        internal async Task<T> ExecuteBatch<T>(Guid requestId, string operation, object payload,
            string actionSql, object actionParameters) where T : class
        {
            if (requestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
            var parameters = new Dapper.DynamicParameters(actionParameters);
            parameters.Add("CreateUserId", _playerId);
            parameters.Add("RequestId", requestId);
            parameters.Add("Operation", operation);
            parameters.Add("Payload", JsonSerializer.Serialize(payload));
            parameters.Add("StartingGold", StartingGold);
            parameters.Add("Now", DateTime.Now);
            parameters.Add("Enabled", (int)ReconstructNodesServer.Library.Models.Enum.EStatus.啟用);
            parameters.Add("OwnsTransaction", _repository.Transaction == null);
            try
            {
                string json = await QueryFirstAsync<string>("""
                    BEGIN TRY
                        IF @OwnsTransaction = 1
                        BEGIN
                            SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
                            BEGIN TRAN;
                        END
                        DECLARE @Owner uniqueidentifier, @Result nvarchar(max), @PriorOperation nvarchar(max),
                            @PriorPayload nvarchar(max), @GoldId uniqueidentifier, @Balance bigint;
                        SELECT @Owner = Id FROM Player WHERE Id = @CreateUserId;
                        IF @Owner IS NULL THROW 51002, 'Economy owner is unavailable', 1;
                        SELECT @Result = Result, @PriorOperation = Operation, @PriorPayload = Payload
                        FROM EconomyRequest WHERE CreateUserId = @CreateUserId AND RequestId = @RequestId;
                        IF @PriorOperation IS NOT NULL
                        BEGIN
                            IF @PriorOperation COLLATE Latin1_General_100_BIN2 <> @Operation COLLATE Latin1_General_100_BIN2
                                OR DATALENGTH(@PriorOperation) <> DATALENGTH(@Operation)
                                OR @PriorPayload COLLATE Latin1_General_100_BIN2 <> @Payload COLLATE Latin1_General_100_BIN2
                                OR DATALENGTH(@PriorPayload) <> DATALENGTH(@Payload)
                                THROW 51003, 'Economy request conflict', 1;
                        END
                        ELSE
                        BEGIN
                            SELECT @GoldId = Id, @Balance = TRY_CONVERT(bigint, Value) FROM PlayerParams
                            WHERE CreateUserId = @CreateUserId AND [Group] = 'game_progress' AND Name = 'gold' AND Status != -1;
                            IF @@ROWCOUNT > 1 THROW 51004, 'Duplicate gold parameter', 1;
                            IF @GoldId IS NULL SET @Balance = @StartingGold;
                            IF @Balance IS NULL OR @Balance < 0 THROW 51004, 'Invalid gold parameter', 1;
                    """ + actionSql + "\n" + PersistOperationSql + "\n" + """
                        END
                        IF @OwnsTransaction = 1 COMMIT;
                        SELECT @Result;
                    END TRY
                    BEGIN CATCH
                        IF @OwnsTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK;
                        THROW;
                    END CATCH;
                    """, parameters);
                return JsonSerializer.Deserialize<T>(json)
                    ?? throw new InvalidOperationException("交易結果紀錄無效");
            }
            catch (System.Data.SqlClient.SqlException error) when (error.Number == 51003)
            {
                throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict);
            }
        }
    }
}
