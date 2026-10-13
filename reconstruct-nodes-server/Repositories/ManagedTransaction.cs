namespace ReconstructNodesServer.Repositories
{
    public sealed class ManagedTransaction : IDisposable
    {
        private readonly Action _commit;
        private readonly Action _rollback;
        private readonly Action _dispose;
        private int _disposed;

        internal ManagedTransaction(Action commit, Action rollback, Action dispose)
        {
            _commit = commit;
            _rollback = rollback;
            _dispose = dispose;
        }

        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            _commit();
        }

        public void Rollback()
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            _rollback();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) { _dispose(); }
        }
    }
}
