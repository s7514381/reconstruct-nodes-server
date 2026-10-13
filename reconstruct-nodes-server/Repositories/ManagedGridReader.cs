using Dapper;

namespace ReconstructNodesServer.Repositories
{
    public sealed class ManagedGridReader : IDisposable, IAsyncDisposable
    {
        private readonly SqlMapper.GridReader _reader;
        private readonly Action _release;
        private int _disposed;

        internal ManagedGridReader(SqlMapper.GridReader reader, Action release)
        {
            _reader = reader;
            _release = release;
        }

        public bool IsConsumed => _reader.IsConsumed;

        public IEnumerable<T> Read<T>() => ReadResult(() => _reader.Read<T>());
        public Task<IEnumerable<T>> ReadAsync<T>() => ReadResultAsync(() => _reader.ReadAsync<T>());
        public T ReadFirst<T>() => ReadResult(() => _reader.ReadFirst<T>());
        public Task<T> ReadFirstAsync<T>() => ReadResultAsync(() => _reader.ReadFirstAsync<T>());
        public T ReadSingle<T>() => ReadResult(() => _reader.ReadSingle<T>());
        public Task<T> ReadSingleAsync<T>() => ReadResultAsync(() => _reader.ReadSingleAsync<T>());
        public T? ReadFirstOrDefault<T>() => ReadResult(() => _reader.ReadFirstOrDefault<T>());
        public async Task<T?> ReadFirstOrDefaultAsync<T>() =>
            await ReadResultAsync(() => _reader.ReadFirstOrDefaultAsync<T>()).ConfigureAwait(false);
        public T? ReadSingleOrDefault<T>() => ReadResult(() => _reader.ReadSingleOrDefault<T>());
        public async Task<T?> ReadSingleOrDefaultAsync<T>() =>
            await ReadResultAsync(() => _reader.ReadSingleOrDefaultAsync<T>()).ConfigureAwait(false);

        private T ReadResult<T>(Func<T> read)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            try { return read(); }
            catch
            {
                Dispose();
                throw;
            }
        }

        private async Task<T> ReadResultAsync<T>(Func<Task<T>> read)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            try { return await read().ConfigureAwait(false); }
            catch
            {
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
            try { _reader.Dispose(); }
            finally { _release(); }
        }

        public ValueTask DisposeAsync()
        {
            // The installed Dapper version only supports synchronous reader disposal.
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
