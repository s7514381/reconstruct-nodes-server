namespace ReconstructNodesServer.Services
{
    public sealed class DbConnectionLimitService(int initialCount, int maxCount)
        : SemaphoreSlimService(initialCount, maxCount);
}
