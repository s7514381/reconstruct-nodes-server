using Dapper;
using Dapper.Contrib.Extensions;
using System.Data;
using System.Data.Common;
using ReconstructNodesServer.Utility;
using ReconstructNodesServer.Services;
using Lease = ReconstructNodesServer.Services.SemaphoreSlimService.SemaphoreSlimLease;

namespace ReconstructNodesServer.Repositories
{
    public class DapperRepository : IDapperRepository
    {
        private readonly DbConnection _connection;
        private readonly DbConnectionLimitService _connectionLimit;
        private readonly DbOperateLimitService _operationLimit;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Lease? _connectionLease;
        private ManagedGridReader? _reader;
        private ManagedTransaction? _managedTransaction;
        private bool _disposed;
        private IDisposable? _transactionTiming;

        public IDbConnection Connection => _connection;
        public IDbTransaction? Transaction { get; private set; }

        public DapperRepository(DapperDBContext dbContext,
            DbConnectionLimitService connectionLimit, DbOperateLimitService operationLimit)
        {
            _connection = dbContext.CreateConnection();
            _connectionLimit = connectionLimit;
            _operationLimit = operationLimit;
        }

        public void Dispose()
        {
            try { _reader?.Dispose(); }
            finally
            {
                try { _managedTransaction?.Dispose(); }
                finally { DisposeConnection(); }
            }
        }

        private void DisposeConnection()
        {
            _gate.Wait();
            try
            {
                if (_disposed) { return; }
                _disposed = true;
                try { CloseConnection(); }
                finally { _connection.Dispose(); }
            }
            finally { _gate.Release(); }
        }

        private void CloseConnection()
        {
            try { _connection.Close(); }
            finally
            {
                _connectionLease?.Dispose();
                _connectionLease = null;
            }
        }

        private OperationScope Acquire()
        {
            using (RequestTiming.Measure("GateWait")) { _gate.Wait(); }
            var scope = new OperationScope(this);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_connectionLease == null)
                {
                    using (RequestTiming.Measure("ConnectionLimitWait")) { _connectionLease = _connectionLimit.Acquire(); }
                }
                using (RequestTiming.Measure("OperationLimitWait")) { scope.OperationLease = _operationLimit.Acquire(); }
                if (_connection.State != ConnectionState.Open)
                {
                    EnsureNoBrokenTransaction();
                    using (RequestTiming.Measure("ConnectionOpen")) { _connection.Open(); }
                }
                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        private async Task<OperationScope> AcquireAsync()
        {
            using (RequestTiming.Measure("GateWait")) { await _gate.WaitAsync().ConfigureAwait(false); }
            var scope = new OperationScope(this);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_connectionLease == null)
                {
                    using (RequestTiming.Measure("ConnectionLimitWait"))
                    { _connectionLease = await _connectionLimit.AcquireAsync().ConfigureAwait(false); }
                }
                using (RequestTiming.Measure("OperationLimitWait"))
                { scope.OperationLease = await _operationLimit.AcquireAsync().ConfigureAwait(false); }
                if (_connection.State != ConnectionState.Open)
                {
                    EnsureNoBrokenTransaction();
                    using (RequestTiming.Measure("ConnectionOpen"))
                    { await _connection.OpenAsync().ConfigureAwait(false); }
                }
                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        private void EnsureNoBrokenTransaction()
        {
            if (Transaction != null)
            {
                throw new InvalidOperationException("交易連線已中斷，不能重新開啟連線繼續交易。");
            }
        }

        private T Run<T>(Func<T> action, string? sql = null, bool command = true)
        {
            using var scope = Acquire();
            using var timing = command ? RequestTiming.Measure("Sql") : null;
            using var playerLock = IsPlayerLock(sql) ? RequestTiming.Measure("PlayerLockCommand") : null;
            return action();
        }

        private async Task<T> RunAsync<T>(Func<Task<T>> action, string? sql = null)
        {
            using var scope = await AcquireAsync().ConfigureAwait(false);
            using var timing = RequestTiming.Measure("Sql");
            using var playerLock = IsPlayerLock(sql) ? RequestTiming.Measure("PlayerLockCommand") : null;
            return await action().ConfigureAwait(false);
        }

        private static bool IsPlayerLock(string? sql) => RequestTiming.Current != null && sql != null &&
            System.Text.RegularExpressions.Regex.IsMatch(sql,
                @"\bFROM\s+\[?Player\]?\s+WITH\s*\(\s*UPDLOCK\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private sealed class OperationScope(DapperRepository repository) : IDisposable
        {
            public Lease? OperationLease { get; set; }
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
                try
                {
                    if (repository.Transaction == null) { repository.CloseConnection(); }
                }
                finally
                {
                    OperationLease?.Dispose();
                    repository._gate.Release();
                }
            }
        }

        public ManagedTransaction OpenTransaction() => Run(() =>
        {
            if (Transaction != null)
            {
                throw new InvalidOperationException("Repository 已有進行中的交易。");
            }
            Transaction = _connection.BeginTransaction();
            _transactionTiming = RequestTiming.Measure("TransactionLifetime");
            _managedTransaction = new ManagedTransaction(
                () => RunTransactionAction(transaction => transaction.Commit()),
                () => RunTransactionAction(transaction => transaction.Rollback()),
                EndTransaction);
            return _managedTransaction;
        }, command: false);

        private void RunTransactionAction(Action<IDbTransaction> action)
        {
            if (_reader != null)
            {
                throw new InvalidOperationException("提交或回滾前必須先釋放目前的多結果 reader。");
            }
            Run(() =>
            {
                action(Transaction ?? throw new InvalidOperationException("Repository 沒有進行中的交易。"));
                return true;
            }, command: false);
        }

        private void EndTransaction()
        {
            try { _reader?.Dispose(); }
            finally
            {
                // Cleanup must not reopen a broken transaction connection.
                _gate.Wait();
                try
                {
                    using var lease = _operationLimit.Acquire();
                    try { Transaction?.Dispose(); }
                    finally
                    {
                        Transaction = null;
                        _managedTransaction = null;
                        try { CloseConnection(); }
                        finally
                        {
                            _transactionTiming?.Dispose();
                            _transactionTiming = null;
                        }
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

        public IEnumerable<T> Query<T>(string sql, object? param = null) =>
            Run(() => _connection.Query<T>(sql, param, Transaction), sql);

        public Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null) =>
            RunAsync(() => _connection.QueryAsync<T>(sql, param, Transaction), sql);

        public T QueryFirst<T>(string sql, object? param = null) =>
            Run(() => _connection.QueryFirst<T>(sql, param, Transaction), sql);

        public Task<T> QueryFirstAsync<T>(string sql, object? param = null) =>
            RunAsync(() => _connection.QueryFirstAsync<T>(sql, param, Transaction), sql);

        public T QueryFirstOrDefault<T>(string sql, object? param = null) =>
            Run(() => _connection.QueryFirstOrDefault<T>(sql, param, Transaction), sql)!;

        public Task<T> QueryFirstOrDefaultAsync<T>(string sql, object? param = null) =>
            RunAsync(() => _connection.QueryFirstOrDefaultAsync<T>(sql, param, Transaction), sql)!;

        public int Execute(string sql, object? param = null) =>
            Run(() => _connection.Execute(sql, param, Transaction), sql);

        public Task<int> ExecuteAsync(string sql, object? param = null) =>
            RunAsync(() => _connection.ExecuteAsync(sql, param, Transaction), sql);

        public long Insert<T>(T entityToInsert) where T : class =>
            Run(() => _connection.Insert(entityToInsert, Transaction));

        public Task<int> InsertAsync<T>(T entityToInsert) where T : class =>
            RunAsync(() => _connection.InsertAsync(entityToInsert, Transaction));

        public Task<bool> UpdateAsync<T>(T entity) where T : class =>
            RunAsync(() => _connection.UpdateAsync(entity, Transaction));

        public ManagedGridReader QueryMultiple(string sql, object? param = null)
        {
            var scope = Acquire();
            using var timing = RequestTiming.Measure("Sql");
            using var playerLock = IsPlayerLock(sql) ? RequestTiming.Measure("PlayerLockCommand") : null;
            try
            {
                _reader = new ManagedGridReader(_connection.QueryMultiple(sql, param, Transaction),
                    () => ReleaseReader(scope));
                return _reader;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        public async Task<ManagedGridReader> QueryMultipleAsync(string sql, object? param = null)
        {
            var scope = await AcquireAsync().ConfigureAwait(false);
            using var timing = RequestTiming.Measure("Sql");
            using var playerLock = IsPlayerLock(sql) ? RequestTiming.Measure("PlayerLockCommand") : null;
            try
            {
                _reader = new ManagedGridReader(
                    await _connection.QueryMultipleAsync(sql, param, Transaction).ConfigureAwait(false),
                    () => ReleaseReader(scope));
                return _reader;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        private void ReleaseReader(OperationScope scope)
        {
            _reader = null;
            scope.Dispose();
        }
    }
}
