using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services
{
    /// <summary>單一領域的批次讀取：SQL 片段與依序解析各結果集的函式，需共用參數 @CreateUserId。</summary>
    public sealed record SnapshotRead<T>(string Sql, Func<ManagedGridReader, Task<T>> Read);

    public sealed class BatchResult<T>
    {
        public T Value { get; internal set; } = default!;
    }

    /// <summary>把多個領域的讀取合併成一次 round-trip，在呼叫端既有的交易內執行。</summary>
    public sealed class SnapshotReadBatch
    {
        private readonly List<string> _sql = [];
        private readonly List<Func<ManagedGridReader, Task>> _readers = [];

        public BatchResult<T> Add<T>(SnapshotRead<T> read)
        {
            var result = new BatchResult<T>();
            _sql.Add(read.Sql);
            _readers.Add(async reader => result.Value = await read.Read(reader));
            return result;
        }

        public string Sql => string.Join("\n", _sql);

        /// <summary>依加入順序從已執行的多結果 reader 解析各片段，並確認全部結果集皆已讀取。</summary>
        public async Task ReadAsync(ManagedGridReader reader)
        {
            foreach (var read in _readers) { await read(reader); }
            if (!reader.IsConsumed) { throw new InvalidOperationException("Snapshot batch did not consume every result."); }
        }

        public async Task ExecuteAsync(IDapperRepository repository, Guid? playerId)
        {
            await using var reader = await repository.QueryMultipleAsync(Sql, new { CreateUserId = playerId });
            await ReadAsync(reader);
        }
    }
}
