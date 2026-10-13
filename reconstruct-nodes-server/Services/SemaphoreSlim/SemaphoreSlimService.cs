namespace ReconstructNodesServer.Services
{
    public abstract class SemaphoreSlimService(int initialCount, int maxCount)
        : SemaphoreSlim(initialCount, maxCount)
    {
        public SemaphoreSlimLease Acquire(CancellationToken cancellationToken = default)
        {
            Wait(cancellationToken);
            return new SemaphoreSlimLease(this);
        }

        public async Task<SemaphoreSlimLease> AcquireAsync(CancellationToken cancellationToken = default)
        {
            await WaitAsync(cancellationToken).ConfigureAwait(false);
            return new SemaphoreSlimLease(this);
        }

        public sealed class SemaphoreSlimLease : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private int _disposed;

            internal SemaphoreSlimLease(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _semaphore.Release();
                }
            }
        }
    }
}
