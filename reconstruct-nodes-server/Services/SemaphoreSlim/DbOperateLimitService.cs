namespace ReconstructNodesServer.Services
{
    public sealed class DbOperateLimitService(int initialCount, int maxCount)
        : SemaphoreSlimService(initialCount, maxCount);
}
