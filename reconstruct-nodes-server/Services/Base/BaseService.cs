using Azure.Data.Tables;
using Dapper.Contrib.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
        protected readonly IConfiguration _configuration = argument.Configuration;

        protected LazyServiceArgument _lazy { get; set; } = argument.LazyServiceArgument;
        protected MailSender _mailSender { get; set; } = argument.LazyServiceArgument.MailSender.Value;

        private static int _currentMaxCount = 10;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(_currentMaxCount, _currentMaxCount);
        private static readonly object _lockObject = new object();
        private static readonly int _adjustThreshold = 20;
        private static ConcurrentBag<long> _records = new();
        private static bool _semaphoreChecking = false;

        public async Task<int> ExecuteAsync(string sql, object? para = null, [CallerMemberName] string? propertyName = null)
        {
            int result = 0;

            await SemaphoreExecute(async () => {
                result = await _repository.ExecuteAsync(sql, para);
            }, propertyName, true);
            return result;
        }

        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? para = null, [CallerMemberName] string? propertyName = null)
        {
            IEnumerable<T> result = Enumerable.Empty<T>();

            await SemaphoreExecute(async () => {
                result = await _repository.QueryAsync<T>(sql, para);
            }, propertyName, true);
            return result;
        }

        public async Task<T> QueryFirstAsync<T>(string sql, object? para = null, [CallerMemberName] string? propertyName = null)
        {
            // QueryFirstOrDefault 查無資料時會回傳 default，維持既有呼叫端對非 null 結果的約定
            T result = default!;

            await SemaphoreExecute(async () => {
                result = await _repository.QueryFirstOrDefaultAsync<T>(sql, para);
            }, propertyName, true);
            return result;
        }

        /// <summary>
        /// 自動覆寫 Create 與 Update 參數 (Date 與 UserId)，並新增至資料庫
        /// </summary>
        public virtual async Task<int> InsertAsync<T>(T model, [CallerMemberName] string? propertyName = null) where T : class
        {
            int result = default;

            await SemaphoreExecute(async () => {
                result = await _repository.InsertAsync(model);
            }, propertyName, true);
            return result;
        }

        /// <summary>
        /// 自動覆寫 Update 參數 (Date 與 UserId)，並更新至資料庫
        /// </summary>
        public virtual async Task<bool> UpdateAsync<T>(T model, [CallerMemberName] string? propertyName = null) where T : class
        {
            bool result = default;

            await SemaphoreExecute(async () => {
                result = await _repository.UpdateAsync(model);
            }, propertyName, true);
            return result;
        }

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

        public static async Task SemaphoreExecute(Func<Task> func, [CallerMemberName] string? propertyName = null, bool isCalResponseTime = false)
        {
            Stopwatch stopwatch = new Stopwatch();
            SemaphoreSlim semaphore;
            lock (_lockObject) { semaphore = _semaphore; } //確保_semaphore沒有正在替換

            await semaphore.WaitAsync();
            try
            {
                stopwatch.Start();
                await func();
            }
            finally
            {
                semaphore.Release();

                stopwatch.Stop();
                long elapsedMs = stopwatch.ElapsedMilliseconds;
                Console.WriteLine($@"{propertyName}: {elapsedMs} 毫秒");

                if (isCalResponseTime) { OptimalDBConnection(elapsedMs); }
            }
        }

        private static int CalculateOptimalMaxCount(long t)
        {
            if (t >= 300) { return 2; }
            else if (t >= 150) { return 5; }
            else if (t >= 100) { return 10; }
            else if (t >= 70) { return 15; }
            else if (t >= 50) { return 25; }
            else if (t >= 20) { return 40; }
            else { return 50; }
        }

        public static void OptimalDBConnection(long elapsedMs) 
        {
            _records.Add(elapsedMs);

            if (_semaphore.CanReset(_currentMaxCount)
                && _records.Count > _adjustThreshold)
            {
                _semaphoreChecking = true;
                lock (_lockObject)
                {
                    if (!_semaphoreChecking) { goto LockEnd; }

                    long average = (long)_records.Average();
                    _records.Clear();

                    Console.WriteLine($"平均響應時間: {average} 毫秒");

                    //計算是否需要調整同時連線數
                    int newMaxCount = CalculateOptimalMaxCount(average);

                    if (newMaxCount != _currentMaxCount
                        && _semaphore.CanReset(_currentMaxCount))
                    {
                        Console.WriteLine($"目前:{_currentMaxCount} 調整成:{newMaxCount}");

                        // 創建一個新的semaphore替換舊的
                        SemaphoreSlim oldSemaphore = _semaphore;
                        var newSemaphore = new SemaphoreSlim(newMaxCount, newMaxCount);
                        _semaphore = newSemaphore;
                        oldSemaphore.Dispose();

                        _currentMaxCount = newMaxCount;
                    }

                LockEnd:
                    _semaphoreChecking = false;
                }
            }
        }

    }

    public static class SemaphoreSlimExtension 
    {
        public static bool CanReset(this SemaphoreSlim semaphore, int maxCount) {
            return semaphore.CurrentCount == maxCount
                   && semaphore.AvailableWaitHandle.WaitOne(0);
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
