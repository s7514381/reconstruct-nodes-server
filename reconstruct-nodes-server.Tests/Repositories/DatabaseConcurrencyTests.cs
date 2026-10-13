using Dapper.Contrib.Extensions;
using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests.Repositories;

[TestClass]
public class DatabaseConcurrencyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task Lease_CancellationAndRepeatedDispose_DoNotLeakOrOverRelease()
    {
        using var limiter = new DbConnectionLimitService(1, 1);
        var lease = limiter.Acquire();
        using var cancellation = new CancellationTokenSource();
        var waiting = limiter.AcquireAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await waiting);
        Assert.AreEqual(0, limiter.CurrentCount);
        lease.Dispose();
        lease.Dispose();
        using (await limiter.AcquireAsync()) { Assert.AreEqual(0, limiter.CurrentCount); }
        Assert.AreEqual(1, limiter.CurrentCount);
    }

    [TestMethod]
    public void Registration_DefaultsAreSingletonAcrossScopes()
    {
        var services = new ServiceCollection();
        services.AddDatabaseConcurrency(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var connection = first.ServiceProvider.GetRequiredService<DbConnectionLimitService>();
        var operation = first.ServiceProvider.GetRequiredService<DbOperateLimitService>();
        Assert.AreSame(connection, second.ServiceProvider.GetRequiredService<DbConnectionLimitService>());
        Assert.AreSame(operation, second.ServiceProvider.GetRequiredService<DbOperateLimitService>());
        Assert.AreEqual(10, connection.CurrentCount);
        Assert.AreEqual(10, operation.CurrentCount);
    }

    [TestMethod]
    [DataRow(0, 10)]
    [DataRow(10, 0)]
    [DataRow(-1, 10)]
    public void Registration_RejectsInvalidLimits(int connections, int operations)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseConcurrency:MaxConnections"] = connections.ToString(),
            ["DatabaseConcurrency:MaxOperations"] = operations.ToString()
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddDatabaseConcurrency(configuration));
    }

    [TestMethod]
    [DataRow(2, 4, false)]
    [DataRow(4, 2, false)]
    [DataRow(2, 4, true)]
    [DataRow(4, 2, true)]
    public async Task Repository_EnforcesIndependentLimits(int connections, int operations, bool sync)
    {
        using var connectionLimit = new DbConnectionLimitService(connections, connections);
        using var operationLimit = new DbOperateLimitService(operations, operations);
        var database = new FakeDatabase
        {
            ExpectedOperations = Math.Min(connections, operations),
            BlockCommands = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var repositories = Enumerable.Range(0, 12)
            .Select(_ => new DapperRepository(new FakeContext(database), connectionLimit, operationLimit)).ToArray();
        Assert.AreEqual(0, database.Opens);
        var tasks = repositories.Select(repository => sync
            ? Task.Run(() => repository.Execute("update"))
            : repository.ExecuteAsync("update")).ToArray();
        try
        {
            await database.ReachedOperations.Task.WaitAsync(Timeout);
            Assert.AreEqual(Math.Min(connections, operations), database.PeakOperations);
            Assert.IsLessThanOrEqualTo(connections, database.PeakConnections);
        }
        finally { database.BlockCommands.SetResult(); }
        await Task.WhenAll(tasks).WaitAsync(Timeout);
        Assert.IsLessThanOrEqualTo(connections, database.PeakConnections);
        Assert.IsLessThanOrEqualTo(operations, database.PeakOperations);
        Assert.AreEqual(0, database.OpenConnections);
        Assert.AreEqual(connections, connectionLimit.CurrentCount);
        Assert.AreEqual(operations, operationLimit.CurrentCount);
        foreach (var repository in repositories) { repository.Dispose(); }
    }

    [TestMethod]
    public async Task SameRepository_ParallelCommandsAreSerialized()
    {
        using var connections = new DbConnectionLimitService(3, 3);
        using var operations = new DbOperateLimitService(3, 3);
        var database = new FakeDatabase { BlockCommands = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var repository = new DapperRepository(new FakeContext(database), connections, operations);
        var first = repository.ExecuteAsync("first");
        await database.ReachedOperations.Task.WaitAsync(Timeout);
        var second = repository.ExecuteAsync("second");
        Assert.IsFalse(second.IsCompleted);
        database.BlockCommands.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);
        Assert.AreEqual(1, database.PeakOperations);
        Assert.AreEqual(1, database.PeakConnections);
        Assert.AreEqual(2, database.Opens);
    }

    [TestMethod]
    public async Task AllRepositoryMethods_UseAndReleaseLimits()
    {
        using var connections = new DbConnectionLimitService(1, 1);
        using var operations = new DbOperateLimitService(1, 1);
        var database = new FakeDatabase();
        using var repository = new DapperRepository(new FakeContext(database), connections, operations);
        Assert.AreEqual(1, repository.Execute("execute"));
        Assert.AreEqual(1, await repository.ExecuteAsync("execute"));
        Assert.AreEqual(1, repository.Query<int>("query").Single());
        Assert.AreEqual(1, (await repository.QueryAsync<int>("query")).Single());
        Assert.AreEqual(1, repository.QueryFirst<int>("query"));
        Assert.AreEqual(1, await repository.QueryFirstAsync<int>("query"));
        Assert.AreEqual(1, repository.QueryFirstOrDefault<int>("query"));
        Assert.AreEqual(1, await repository.QueryFirstOrDefaultAsync<int>("query"));
        Assert.AreEqual(1L, repository.Insert(new Entity { Value = 2 }));
        Assert.AreEqual(1, await repository.InsertAsync(new Entity { Value = 2 }));
        Assert.IsTrue(await repository.UpdateAsync(new Entity { Id = 1, Value = 2 }));
        Assert.AreEqual(11, database.Opens);
        Assert.AreEqual(0, database.OpenConnections);
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(1, operations.CurrentCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failure_ReleasesConnectionAndOperation(bool failOpen)
    {
        using var connections = new DbConnectionLimitService(1, 1);
        using var operations = new DbOperateLimitService(1, 1);
        var database = new FakeDatabase { FailOpen = failOpen, FailCommand = !failOpen };
        using var repository = new DapperRepository(new FakeContext(database), connections, operations);
        Assert.Throws<InvalidOperationException>(() => repository.Execute("failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ExecuteAsync("failure"));
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(1, operations.CurrentCount);
        Assert.AreEqual(0, database.OpenConnections);
        database.FailOpen = database.FailCommand = false;
        Assert.AreEqual(1, await repository.ExecuteAsync("recovery").WaitAsync(Timeout));
    }

    [TestMethod]
    public async Task Transaction_ReusesConnection_AndDoesNotStarveWhenOthersWaitForConnections()
    {
        using var connections = new DbConnectionLimitService(1, 1);
        using var operations = new DbOperateLimitService(2, 2);
        var database = new FakeDatabase();
        using var first = new DapperRepository(new FakeContext(database), connections, operations);
        using var second = new DapperRepository(new FakeContext(database), connections, operations);
        var transaction = first.OpenTransaction();
        var rawTransaction = first.Transaction;
        Assert.AreEqual(0, connections.CurrentCount);
        Assert.AreEqual(2, operations.CurrentCount);
        var waiting = second.ExecuteAsync("waiting");
        Assert.IsFalse(waiting.IsCompleted);
        Assert.AreEqual(2, operations.CurrentCount);
        await first.ExecuteAsync("in transaction").WaitAsync(Timeout);
        await first.QueryAsync<int>("in transaction").WaitAsync(Timeout);
        Assert.AreEqual(1, database.Opens);
        Assert.AreSame(rawTransaction, database.CommandTransactions[0]);
        Assert.AreSame(rawTransaction, database.CommandTransactions[1]);
        transaction.Commit();
        transaction.Dispose();
        transaction.Dispose();
        await waiting.WaitAsync(Timeout);
        Assert.AreEqual(1, database.Commits);
        Assert.IsNull(first.Transaction);
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(2, operations.CurrentCount);
    }

    [TestMethod]
    public void Transaction_BeginFailureRollbackAndDisposeFailureReleaseResources()
    {
        using var connections = new DbConnectionLimitService(1, 1);
        using var operations = new DbOperateLimitService(1, 1);
        var database = new FakeDatabase { FailBegin = true };
        using var repository = new DapperRepository(new FakeContext(database), connections, operations);
        Assert.Throws<InvalidOperationException>(() => repository.OpenTransaction());
        Assert.AreEqual(1, connections.CurrentCount);
        database.FailBegin = false;
        var transaction = repository.OpenTransaction();
        database.FailCommit = true;
        Assert.Throws<InvalidOperationException>(transaction.Commit);
        transaction.Rollback();
        Assert.AreEqual(1, database.Rollbacks);
        database.FailTransactionDispose = true;
        Assert.Throws<InvalidOperationException>(transaction.Dispose);
        Assert.IsNull(repository.Transaction);
        Assert.AreEqual(0, database.OpenConnections);
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(1, operations.CurrentCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MultipleResults_HoldLimitsUntilReaderDisposal(bool async)
    {
        using var connections = new DbConnectionLimitService(1, 1);
        using var operations = new DbOperateLimitService(1, 1);
        var database = new FakeDatabase();
        using var repository = new DapperRepository(new FakeContext(database), connections, operations);
        using var other = new DapperRepository(new FakeContext(database), connections, operations);
        var reader = async ? await repository.QueryMultipleAsync("multiple") : repository.QueryMultiple("multiple");
        var waiting = other.ExecuteAsync("waiting");
        Assert.IsFalse(waiting.IsCompleted);
        Assert.AreEqual(0, connections.CurrentCount);
        Assert.AreEqual(0, operations.CurrentCount);
        Assert.AreEqual(1, reader.Read<int>().Single());
        Assert.AreEqual(1, (await reader.ReadAsync<int>()).Single());
        Assert.AreEqual(0, operations.CurrentCount);
        if (async) { await reader.DisposeAsync(); } else { reader.Dispose(); }
        reader.Dispose();
        await waiting.WaitAsync(Timeout);
        Assert.AreEqual(0, database.OpenConnections);
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(1, operations.CurrentCount);
    }

    [TestMethod]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("Query")]
    [DataRow("QueryAsync")]
    [DataRow("QueryFirst")]
    [DataRow("QueryFirstAsync")]
    [DataRow("QueryFirstOrDefault")]
    [DataRow("QueryFirstOrDefaultAsync")]
    [DataRow("Insert")]
    [DataRow("InsertAsync")]
    [DataRow("UpdateAsync")]
    [DataRow("QueryMultiple")]
    [DataRow("QueryMultipleAsync")]
    public async Task EveryDatabaseEntryPoint_HoldsBothPermits(string method)
    {
        using var connections = new DbConnectionLimitService(1, 1);
        using var operations = new DbOperateLimitService(1, 1);
        var database = new FakeDatabase { BlockCommands = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var repository = new DapperRepository(new FakeContext(database), connections, operations);
        var running = Task.Run(async () =>
        {
            switch (method)
            {
                case "Execute": repository.Execute("command"); break;
                case "ExecuteAsync": await repository.ExecuteAsync("command"); break;
                case "Query": repository.Query<int>("command").ToArray(); break;
                case "QueryAsync": await repository.QueryAsync<int>("command"); break;
                case "QueryFirst": repository.QueryFirst<int>("command"); break;
                case "QueryFirstAsync": await repository.QueryFirstAsync<int>("command"); break;
                case "QueryFirstOrDefault": repository.QueryFirstOrDefault<int>("command"); break;
                case "QueryFirstOrDefaultAsync": await repository.QueryFirstOrDefaultAsync<int>("command"); break;
                case "Insert": repository.Insert(new Entity { Value = 2 }); break;
                case "InsertAsync": await repository.InsertAsync(new Entity { Value = 2 }); break;
                case "UpdateAsync": await repository.UpdateAsync(new Entity { Id = 1, Value = 2 }); break;
                case "QueryMultiple": using (repository.QueryMultiple("command")) { } break;
                case "QueryMultipleAsync": await using (await repository.QueryMultipleAsync("command")) { } break;
                default: throw new InvalidOperationException(method);
            }
        });
        try
        {
            await database.ReachedOperations.Task.WaitAsync(Timeout);
            Assert.AreEqual(0, connections.CurrentCount, method);
            Assert.AreEqual(0, operations.CurrentCount, method);
        }
        finally { database.BlockCommands.SetResult(); }
        await running.WaitAsync(Timeout);
        Assert.AreEqual(1, connections.CurrentCount, method);
        Assert.AreEqual(1, operations.CurrentCount, method);
        Assert.AreEqual(0, database.OpenConnections, method);
    }

    [TestMethod]
    public void TransactionActions_HoldOperationPermit_AndReaderBlocksCommitExplicitly()
    {
        using var connections = new DbConnectionLimitService(1, 1);
        using var operations = new DbOperateLimitService(2, 2);
        var database = new FakeDatabase
        {
            OnTransactionAction = () =>
            {
                Assert.AreEqual(0, connections.CurrentCount);
                Assert.AreEqual(1, operations.CurrentCount);
            }
        };
        using var repository = new DapperRepository(new FakeContext(database), connections, operations);
        var transaction = repository.OpenTransaction();
        using (var reader = repository.QueryMultiple("reader"))
        {
            Assert.Throws<InvalidOperationException>(transaction.Commit);
            Assert.Throws<InvalidOperationException>(transaction.Rollback);
        }
        transaction.Commit();
        transaction.Rollback();
        transaction.Dispose();
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(2, operations.CurrentCount);
    }

    [TestMethod]
    public async Task ReaderCreationAndAsyncReadFailure_ReleaseResources()
    {
        using var connections = new DbConnectionLimitService(1, 1);
        using var operations = new DbOperateLimitService(1, 1);
        var database = new FakeDatabase { FailCommand = true };
        using var repository = new DapperRepository(new FakeContext(database), connections, operations);
        Assert.Throws<InvalidOperationException>(() => repository.QueryMultiple("failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.QueryMultipleAsync("failure"));
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(1, operations.CurrentCount);
        database.FailCommand = false;
        await using var reader = await repository.QueryMultipleAsync("reader");
        await Assert.ThrowsAsync<InvalidCastException>(() => reader.ReadAsync<Guid>());
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(1, operations.CurrentCount);
    }

    [TestMethod]
    public void BrokenTransaction_IsNotReopened_AndDisposalStillReleasesConnection()
    {
        using var connections = new DbConnectionLimitService(1, 1);
        using var operations = new DbOperateLimitService(1, 1);
        var database = new FakeDatabase();
        using var repository = new DapperRepository(new FakeContext(database), connections, operations);
        using var transaction = repository.OpenTransaction();
        repository.Connection.Close();
        Assert.Throws<InvalidOperationException>(() => repository.Execute("broken"));
        Assert.AreEqual(1, database.Opens);
        transaction.Dispose();
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(1, operations.CurrentCount);
    }

    [TestMethod]
    public void ReaderFailure_AndRepositoryDisposal_ReleaseResources()
    {
        using var connections = new DbConnectionLimitService(1, 1);
        using var operations = new DbOperateLimitService(1, 1);
        var database = new FakeDatabase();
        var repository = new DapperRepository(new FakeContext(database), connections, operations);
        var reader = repository.QueryMultiple("multiple");
        Assert.Throws<InvalidCastException>(() => reader.Read<Guid>().ToArray());
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(1, operations.CurrentCount);
        using var transaction = repository.OpenTransaction();
        var pendingReader = repository.QueryMultiple("multiple");
        repository.Dispose();
        repository.Dispose();
        pendingReader.Dispose();
        Assert.IsNull(repository.Transaction);
        Assert.AreEqual(0, database.OpenConnections);
        Assert.AreEqual(1, connections.CurrentCount);
        Assert.AreEqual(1, operations.CurrentCount);
        Assert.Throws<ObjectDisposedException>(() => repository.Execute("disposed"));
    }

    [Table("Entities")]
    public sealed class Entity
    {
        [Key]
        public int Id { get; set; }
        public int Value { get; set; }
    }
}
