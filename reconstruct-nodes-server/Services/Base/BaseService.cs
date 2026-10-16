using System.Runtime.CompilerServices;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 基礎 Service 類別
    /// </summary>
    public class BaseService(BaseServiceArgument argument)
    {
        protected IDapperRepository _repository { get; private set; } = argument.Repository;

        protected LazyServiceArgument _lazy { get; set; } = argument.LazyServiceArgument;
        protected MailSender _mailSender { get; set; } = argument.LazyServiceArgument.MailSender.Value;

        public Guid? _playerId => _player?.Id;
        public Player? _player => _lazy.PlayerService.Value.Player;

        protected async Task<TResult> ExecuteCommandAsync<TContext, TResult>(
            Func<Task<TContext>> read, Func<TContext, Task<TResult>> execute)
        {
            using var transaction = _repository.Transaction == null ? _repository.OpenTransaction() : null;
            try
            {
                TContext context = await read();
                TResult result = await execute(context);
                transaction?.Commit();
                return result;
            }
            catch
            {
                transaction?.Rollback();
                throw;
            }
        }

        protected async Task<TResult> ReadBatchAsync<TResult>(
            string sql, object? parameters, Func<ManagedGridReader, Task<TResult>> read)
        {
            using var reader = await _repository.QueryMultipleAsync(sql, parameters);
            return await read(reader);
        }

        protected Task<TResult> ReadSnapshotCommandAsync<TResult>(SnapshotRead<TResult> read) =>
            ReadSnapshotCommandAsync(read.Sql, read.Read);

        protected async Task<TResult> ReadSnapshotCommandAsync<TResult>(
            string sql, Func<ManagedGridReader, Task<TResult>> read)
        {
            if (_repository.Transaction != null)
            {
                throw new InvalidOperationException("Snapshot command cannot own a nested transaction.");
            }
            await using var reader = await _repository.QueryMultipleAsync("""
                BEGIN TRY
                    SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
                    BEGIN TRAN;
                """ + sql + """
                    COMMIT;
                    SELECT CAST(1 AS int);
                END TRY
                BEGIN CATCH
                    IF XACT_STATE() <> 0 ROLLBACK;
                    THROW;
                END CATCH;
                """, new { CreateUserId = _playerId });
            TResult result = await read(reader);
            if (await reader.ReadSingleAsync<int>() != 1 || !reader.IsConsumed)
            {
                throw new InvalidOperationException("Snapshot command did not finish its transaction.");
            }
            return result;
        }

        public Task<int> ExecuteAsync(string sql, object? para = null, [CallerMemberName] string? propertyName = null) =>
            _repository.ExecuteAsync(sql, para);

        public Task<IEnumerable<T>> QueryAsync<T>(string sql, object? para = null, [CallerMemberName] string? propertyName = null) =>
            _repository.QueryAsync<T>(sql, para);

        public Task<T> QueryFirstAsync<T>(string sql, object? para = null, [CallerMemberName] string? propertyName = null) =>
            _repository.QueryFirstOrDefaultAsync<T>(sql, para);

        /// <summary>
        /// 自動覆寫 Create 與 Update 參數 (Date 與 UserId)，並新增至資料庫
        /// </summary>
        public virtual Task<int> InsertAsync<T>(T model, [CallerMemberName] string? propertyName = null) where T : class =>
            _repository.InsertAsync(model);

        /// <summary>
        /// 自動覆寫 Update 參數 (Date 與 UserId)，並更新至資料庫
        /// </summary>
        public virtual Task<bool> UpdateAsync<T>(T model, [CallerMemberName] string? propertyName = null) where T : class =>
            _repository.UpdateAsync(model);

        public virtual async Task<(bool, Exception? ex)> TransactionFunc(Func<Task> func, Func<Task>? exceptionFunc = null)
        {
            using var trans = _repository.OpenTransaction();
            try
            {
                await func();
                trans.Commit();
                return (true, null);
            }
            catch (Exception ex)
            {
                trans.Rollback();
                if (exceptionFunc != null) { await exceptionFunc(); }
                return (false, ex);
            }
        }
    }
}

namespace ReconstructNodesServer.Argument
{
    /// <summary>
    /// BaseService 的依賴聚合類別，
    /// 以解決新增 BaseService 依賴項時需要修改每個子類別建構子的問題。
    /// </summary>
    public class BaseServiceArgument
    {
        public IDapperRepository Repository { get; set; }
        public IConfiguration Configuration { get; set; }
        public LazyServiceArgument LazyServiceArgument { get; set; }

        public BaseServiceArgument(
            LazyServiceArgument lazyServiceArgument,
            IDapperRepository repository,
            IConfiguration configuration)
        {
            Repository = repository;
            Configuration = configuration;
            LazyServiceArgument = lazyServiceArgument;
        }
    }
}
