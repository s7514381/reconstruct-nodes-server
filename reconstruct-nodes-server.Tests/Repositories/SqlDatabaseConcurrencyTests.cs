using Dapper.Contrib.Extensions;
using System.Data;
using System.Data.SqlClient;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Tests.Repositories;

[TestClass]
[TestCategory("SqlIntegration")]
[DoNotParallelize]
public class SqlDatabaseConcurrencyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void RequireOptIn()
    {
        if (Environment.GetEnvironmentVariable("DB_CONCURRENCY_SQL_TESTS") != "1")
        {
            Assert.Inconclusive("設定 DB_CONCURRENCY_SQL_TESTS=1 與 DB_CONCURRENCY_SQL_CONNECTION 才執行真實 SQL 測試。");
        }
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealCrud_AllRepositoryMethods_AndContribIdentity(bool mars)
    {
        await using var fixture = await SqlDatabaseFixture.CreateAsync(mars: mars);
        var repository = fixture.CreateRepository();
        Assert.AreEqual(1, repository.Execute($"INSERT INTO {fixture.TableName} (Value) VALUES (@Value)", new { Value = 11 }));
        Assert.AreEqual(1, await repository.ExecuteAsync($"INSERT INTO {fixture.TableName} (Value) VALUES (@Value)", new { Value = 12 }));
        string select = $"SELECT Value FROM {fixture.TableName} ORDER BY Id";
        CollectionAssert.AreEqual(new[] { 11, 12 }, repository.Query<int>(select).ToArray());
        CollectionAssert.AreEqual(new[] { 11, 12 }, (await repository.QueryAsync<int>(select)).ToArray());
        Assert.AreEqual(11, repository.QueryFirst<int>(select));
        Assert.AreEqual(11, await repository.QueryFirstAsync<int>(select));
        Assert.AreEqual(11, repository.QueryFirstOrDefault<int>(select));
        Assert.AreEqual(11, await repository.QueryFirstOrDefaultAsync<int>(select));
        Assert.AreEqual(0, await repository.QueryFirstOrDefaultAsync<int>($"SELECT Value FROM {fixture.TableName} WHERE 1=0"));
        Assert.AreEqual(ConnectionState.Closed, repository.Connection.State);
        Assert.AreEqual(2, fixture.Connections.CurrentCount);
        Assert.AreEqual(2, fixture.Operations.CurrentCount);

        using (var transaction = repository.OpenTransaction())
        {
            await repository.ExecuteAsync("CREATE TABLE #DbConcurrencyContrib (Id int IDENTITY PRIMARY KEY, Value int NOT NULL)");
            var first = new ContribRow { Value = 21 };
            var second = new ContribRow { Value = 22 };
            Assert.AreEqual(1L, repository.Insert(first));
            Assert.AreEqual(1, first.Id);
            Assert.AreEqual(2, await repository.InsertAsync(second));
            Assert.AreEqual(2, second.Id);
            second.Value = 23;
            Assert.IsTrue(await repository.UpdateAsync(second));
            CollectionAssert.AreEqual(new[] { 21, 23 },
                (await repository.QueryAsync<int>("SELECT Value FROM #DbConcurrencyContrib ORDER BY Id")).ToArray());
            transaction.Commit();
        }
        Assert.AreEqual(ConnectionState.Closed, repository.Connection.State);
        Assert.AreEqual(0, fixture.OpenConnections);
        Assert.AreEqual(2, fixture.Connections.CurrentCount);
        Assert.AreEqual(2, fixture.Operations.CurrentCount);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealTransaction_CommitRollbackDispose_AndParallelCommands(bool mars)
    {
        await using var fixture = await SqlDatabaseFixture.CreateAsync(mars: mars);
        var repository = fixture.CreateRepository();
        using (var transaction = repository.OpenTransaction())
        {
            int spid = await repository.QueryFirstAsync<int>("SELECT @@SPID");
            var tasks = Enumerable.Range(1, 12).Select(async value =>
            {
                Assert.AreEqual(spid, await repository.QueryFirstAsync<int>("SELECT @@SPID"));
                await repository.ExecuteAsync($"INSERT INTO {fixture.TableName} (Value) VALUES (@value)", new { value });
            });
            await Task.WhenAll(tasks).WaitAsync(Timeout);
            Assert.AreEqual(1, fixture.PeakConnections);
            Assert.Throws<InvalidOperationException>(() => repository.OpenTransaction());
            transaction.Commit();
        }
        Assert.AreEqual(1, await fixture.CountRowsAsync(1));
        using (var transaction = repository.OpenTransaction())
        {
            await repository.ExecuteAsync($"INSERT INTO {fixture.TableName} (Value) VALUES (101)");
            transaction.Rollback();
        }
        Assert.AreEqual(0, await fixture.CountRowsAsync(101));
        using (repository.OpenTransaction())
        {
            await repository.ExecuteAsync($"INSERT INTO {fixture.TableName} (Value) VALUES (102)");
        }
        Assert.AreEqual(0, await fixture.CountRowsAsync(102));
        Assert.AreEqual(2, fixture.Connections.CurrentCount);
        Assert.AreEqual(2, fixture.Operations.CurrentCount);
        Assert.IsNull(repository.Transaction);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealReader_ResultsEarlyDisposeReadFailure_AndRepositoryCleanup(bool mars)
    {
        await using var fixture = await SqlDatabaseFixture.CreateAsync(1, 1, mars);
        var repository = fixture.CreateRepository();
        var other = fixture.CreateRepository();
        await using (var reader = await repository.QueryMultipleAsync("SELECT 1; SELECT 2"))
        {
            Assert.AreEqual(1, reader.Read<int>().Single());
            Assert.AreEqual(2, (await reader.ReadAsync<int>()).Single());
            Assert.AreEqual(0, fixture.Connections.CurrentCount);
            Assert.AreEqual(0, fixture.Operations.CurrentCount);
        }
        var early = repository.QueryMultiple("SELECT 3; SELECT 4");
        Assert.AreEqual(3, early.ReadFirst<int>());
        var waiting = other.ExecuteAsync("SELECT 1");
        Assert.IsFalse(waiting.IsCompleted);
        early.Dispose();
        early.Dispose();
        await waiting.WaitAsync(Timeout);

        await using (var failed = await repository.QueryMultipleAsync("SELECT 'not-an-int'; SELECT 2"))
        {
            await Assert.ThrowsAsync<InvalidCastException>(() => failed.ReadAsync<int>());
        }
        Assert.AreEqual(1, fixture.Connections.CurrentCount);
        Assert.AreEqual(1, fixture.Operations.CurrentCount);
        Assert.AreEqual(5, await repository.QueryFirstAsync<int>("SELECT 5"));
        using (var transaction = repository.OpenTransaction())
        {
            var reader = repository.QueryMultiple("SELECT 6; SELECT 7");
            Assert.Throws<InvalidOperationException>(transaction.Commit);
            Assert.Throws<InvalidOperationException>(transaction.Rollback);
            transaction.Dispose();
            reader.Dispose();
        }
        using (repository.OpenTransaction())
        {
            repository.QueryMultiple("SELECT 8; SELECT 9");
            repository.Dispose();
        }
        Assert.AreEqual(0, fixture.OpenConnections);
        Assert.AreEqual(1, fixture.Connections.CurrentCount);
        Assert.AreEqual(1, fixture.Operations.CurrentCount);
        Assert.AreEqual(10, await other.QueryFirstAsync<int>("SELECT 10"));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task RealSqlErrors_ReleasePermits_AndAllowRecovery()
    {
        await using var fixture = await SqlDatabaseFixture.CreateAsync(1, 1);
        var repository = fixture.CreateRepository();
        const string failure = "THROW 51000, 'Intentional concurrency integration test error', 1;";
        Assert.Throws<SqlException>(() => repository.Execute(failure));
        await Assert.ThrowsAsync<SqlException>(() => repository.ExecuteAsync(failure));
        Assert.Throws<SqlException>(() => repository.QueryMultiple(failure));
        await Assert.ThrowsAsync<SqlException>(() => repository.QueryMultipleAsync(failure));
        using (var transaction = repository.OpenTransaction())
        {
            await Assert.ThrowsAsync<SqlException>(() => repository.ExecuteAsync(failure));
            Assert.AreEqual(0, fixture.Connections.CurrentCount);
            Assert.AreEqual(1, fixture.Operations.CurrentCount);
            transaction.Rollback();
        }
        Assert.AreEqual(1, fixture.Connections.CurrentCount);
        Assert.AreEqual(1, fixture.Operations.CurrentCount);
        Assert.AreEqual(42, await repository.QueryFirstAsync<int>("SELECT 42"));
        Assert.AreEqual(0, fixture.OpenConnections);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task RealOpenFailure_ReleasesPermits_WithoutPoisoningOtherRepositories()
    {
        await using var fixture = await SqlDatabaseFixture.CreateAsync(1, 1);
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("DB_CONCURRENCY_SQL_CONNECTION"))
        {
            InitialCatalog = $"MissingConcurrencyTestDb_{Guid.NewGuid():N}",
            ApplicationName = fixture.ApplicationName + "_OpenFailure",
            ConnectTimeout = 2,
            Pooling = false
        };
        using var invalid = new DapperRepository(
            new DapperDBContext(builder.ConnectionString),
            fixture.Connections, fixture.Operations);
        Assert.Throws<SqlException>(() => invalid.Execute("SELECT 1"));
        await Assert.ThrowsAsync<SqlException>(() => invalid.ExecuteAsync("SELECT 1"));
        Assert.AreEqual(1, fixture.Connections.CurrentCount);
        Assert.AreEqual(1, fixture.Operations.CurrentCount);
        Assert.AreEqual(42, await fixture.CreateRepository().QueryFirstAsync<int>("SELECT 42"));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task TransactionHoldingOnlyConnection_DoesNotDeadlockWaitingRepositories()
    {
        await using var fixture = await SqlDatabaseFixture.CreateAsync(1, 2);
        var owner = fixture.CreateRepository();
        var waiting = fixture.CreateRepository();
        using var transaction = owner.OpenTransaction();
        var blocked = waiting.QueryFirstAsync<int>("SELECT 17");
        Assert.IsFalse(blocked.IsCompleted);
        Assert.AreEqual(2, fixture.Operations.CurrentCount);
        Assert.AreEqual(16, await owner.QueryFirstAsync<int>("SELECT 16").WaitAsync(Timeout));
        transaction.Commit();
        transaction.Dispose();
        Assert.AreEqual(17, await blocked.WaitAsync(Timeout));
        Assert.AreEqual(1, fixture.Connections.CurrentCount);
        Assert.AreEqual(2, fixture.Operations.CurrentCount);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task FourOpenTransactions_WithTwoOperationPermits_EnforceBothLimitsIndependently()
    {
        await using var fixture = await SqlDatabaseFixture.CreateAsync(4, 2);
        var owners = Enumerable.Range(0, 4).Select(_ => fixture.CreateRepository()).ToArray();
        var transactions = new List<ManagedTransaction>();
        Task<int>? waiting = null;
        try
        {
            foreach (var owner in owners) { transactions.Add(owner.OpenTransaction()); }
            Assert.AreEqual(4, fixture.OpenConnections);
            Assert.AreEqual(0, fixture.Connections.CurrentCount);
            Assert.AreEqual(2, fixture.Operations.CurrentCount);
            waiting = fixture.CreateRepository().QueryFirstAsync<int>("SELECT 99");
            Assert.IsFalse(waiting.IsCompleted);
            var all = Task.WhenAll(owners.Select(owner =>
                owner.QueryFirstAsync<int>("WAITFOR DELAY '00:00:00.250'; SELECT 1")));
            var peaks = await ObserveAsync(all, fixture, 2);
            Assert.AreEqual(2, peaks.Requests);
            Assert.AreEqual(2, peaks.Permits);
            Assert.AreEqual(4, fixture.PeakConnections);
            Assert.IsFalse(waiting.IsCompleted);
            TestContext.WriteLine("Independent limits: OpenPeak=4, SqlRequestPeak=2, OperationPermitPeak=2");
        }
        finally
        {
            foreach (var transaction in transactions) { transaction.Dispose(); }
        }
        Assert.IsNotNull(waiting);
        Assert.AreEqual(99, await waiting.WaitAsync(Timeout));
        Assert.AreEqual(0, fixture.OpenConnections);
        Assert.AreEqual(4, fixture.Connections.CurrentCount);
        Assert.AreEqual(2, fixture.Operations.CurrentCount);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(2, 4, false)]
    [DataRow(4, 2, false)]
    [DataRow(1, 1, false)]
    [DataRow(2, 4, true)]
    [DataRow(4, 2, true)]
    [DataRow(1, 1, true)]
    public async Task RealConcurrency_ObservedSqlRequestsAndConnectionsRespectLimits(
        int maxConnections, int maxOperations, bool sync)
    {
        await using var fixture = await SqlDatabaseFixture.CreateAsync(maxConnections, maxOperations);
        int expectedPeak = Math.Min(maxConnections, maxOperations);
        for (int round = 0; round < 3; round++)
        {
            var repositories = Enumerable.Range(0, 12).Select(_ => fixture.CreateRepository()).ToArray();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = repositories.Select((repository, index) => Task.Run(async () =>
            {
                await start.Task;
                string sql = "WAITFOR DELAY '00:00:00.250'; SELECT @index";
                int result = sync
                    ? repository.QueryFirst<int>(sql, new { index })
                    : await repository.QueryFirstAsync<int>(sql, new { index });
                Assert.AreEqual(index, result);
            })).ToArray();
            var all = Task.WhenAll(tasks);
            start.SetResult();
            var peaks = await ObserveAsync(all, fixture, maxOperations);
            TestContext.WriteLine(
                $"Limits=({maxConnections},{maxOperations}), sync={sync}, round={round + 1}, " +
                $"OpenPeak={fixture.PeakConnections}, SqlRequestPeak={peaks.Requests}, OperationPermitPeak={peaks.Permits}");
            Assert.IsLessThanOrEqualTo(maxConnections, fixture.PeakConnections);
            Assert.AreEqual(expectedPeak, fixture.PeakConnections);
            Assert.AreEqual(expectedPeak, peaks.Requests, "必須觀測到真正重疊的 SQL 工作，不能只證明未超過上限。");
            Assert.IsLessThanOrEqualTo(maxOperations, peaks.Permits);
            Assert.AreEqual(0, fixture.OpenConnections);
            Assert.AreEqual(maxConnections, fixture.Connections.CurrentCount);
            Assert.AreEqual(maxOperations, fixture.Operations.CurrentCount);
            foreach (var repository in repositories) { repository.Dispose(); }
        }
    }

    private async Task<(int Requests, int Permits)> ObserveAsync(
        Task work, SqlDatabaseFixture fixture, int maxOperations)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        cancellation.CancelAfter(Timeout);
        int peakRequests = 0;
        int peakPermits = 0;
        try
        {
            while (!work.IsCompleted)
            {
                peakRequests = Math.Max(peakRequests,
                    await fixture.ActiveRequestsAsync().WaitAsync(cancellation.Token));
                peakPermits = Math.Max(peakPermits, maxOperations - fixture.Operations.CurrentCount);
                await Task.Delay(10, cancellation.Token);
            }
        }
        finally
        {
            // WaitAsync times out the observer, not the underlying repository commands.
            await work.WaitAsync(Timeout);
        }
        return (peakRequests, peakPermits);
    }

    [Table("#DbConcurrencyContrib")]
    public sealed class ContribRow
    {
        [Key]
        public int Id { get; set; }
        public int Value { get; set; }
    }
}
