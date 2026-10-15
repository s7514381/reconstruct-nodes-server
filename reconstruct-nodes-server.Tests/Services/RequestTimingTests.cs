using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Tests.Services;

[TestClass]
public class RequestTimingTests
{
    [TestMethod]
    public void Disabled_IsNoOp_AndNestedScopesRestoreParent()
    {
        Assert.IsNull(RequestTiming.Current);
        Assert.IsNull(RequestTiming.Measure("Sql"));
        using (var parent = new RequestTiming())
        {
            using (var child = new RequestTiming())
            {
                var measure = RequestTiming.Measure("Sql")!;
                measure.Dispose();
                measure.Dispose();
                Assert.AreEqual(1L, child.Snapshot()["Sql"].Count);
                Assert.IsGreaterThanOrEqualTo(0.0, child.Snapshot()["Sql"].Milliseconds);
            }
            Assert.AreSame(parent, RequestTiming.Current);
            Assert.IsEmpty(parent.Snapshot());
        }
        Assert.IsNull(RequestTiming.Current);
    }

    [TestMethod]
    public async Task ParallelRequests_DoNotMixCounters()
    {
        await Task.WhenAll(Enumerable.Range(1, 8).Select(async count =>
        {
            using var timing = new RequestTiming();
            await Task.Yield();
            for (int i = 0; i < count; i++)
            {
                using var measure = RequestTiming.Measure("Sql");
            }
            Assert.AreEqual((long)count, timing.Snapshot()["Sql"].Count);
        }));
        Assert.IsNull(RequestTiming.Current);
    }
}
