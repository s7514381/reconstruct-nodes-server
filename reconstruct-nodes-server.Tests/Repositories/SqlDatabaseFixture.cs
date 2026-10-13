using Dapper;
using System.Data;
using System.Data.SqlClient;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests.Repositories;

internal sealed class SqlDatabaseFixture : IAsyncDisposable
{
    private readonly List<DapperRepository> _repositories = [];
    private readonly SqlConnection _observer;
    private readonly string _connectionString;
    private readonly object _metricsGate = new();
    private int _openConnections;
    private int _peakConnections;
    private bool _tableCreationAttempted;

    public string ApplicationName { get; } = $"DbConcurrencyTests_{Guid.NewGuid():N}";
    public string TableName { get; } = $"dbo.DbConcurrencyTests_{Guid.NewGuid():N}";
    public DbConnectionLimitService Connections { get; }
    public DbOperateLimitService Operations { get; }
    public int PeakConnections { get { lock (_metricsGate) { return _peakConnections; } } }
    public int OpenConnections { get { lock (_metricsGate) { return _openConnections; } } }

    private SqlDatabaseFixture(string connectionString, int connections, int operations, bool mars)
    {
        Connections = new(connections, connections);
        Operations = new(operations, operations);
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = ApplicationName,
            MultipleActiveResultSets = mars,
            ConnectTimeout = 5,
            MaxPoolSize = 16
        };
        _connectionString = builder.ConnectionString;
        builder.ApplicationName += "_Observer";
        _observer = new SqlConnection(builder.ConnectionString);
    }

    public static async Task<SqlDatabaseFixture> CreateAsync(
        int connections = 2, int operations = 2, bool mars = false)
    {
        string? connectionString = Environment.GetEnvironmentVariable("DB_CONCURRENCY_SQL_CONNECTION");
        Assert.IsFalse(string.IsNullOrWhiteSpace(connectionString),
            "啟用 SQL 測試時必須設定 DB_CONCURRENCY_SQL_CONNECTION。");
        var fixture = new SqlDatabaseFixture(connectionString!, connections, operations, mars);
        try
        {
            await fixture._observer.OpenAsync();
            fixture._tableCreationAttempted = true;
            await fixture._observer.ExecuteAsync(
                $"CREATE TABLE {fixture.TableName} (Id int IDENTITY PRIMARY KEY, Value int NOT NULL)",
                commandTimeout: 5);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public DapperRepository CreateRepository()
    {
        var repository = new DapperRepository(new DapperDBContext(_connectionString), Connections, Operations);
        ((SqlConnection)repository.Connection).StateChange += (_, args) =>
        {
            lock (_metricsGate)
            {
                if (args.OriginalState == ConnectionState.Open) { _openConnections--; }
                if (args.CurrentState == ConnectionState.Open) { _openConnections++; }
                _peakConnections = Math.Max(_peakConnections, _openConnections);
            }
        };
        _repositories.Add(repository);
        return repository;
    }

    public Task<int> ActiveRequestsAsync() => _observer.QuerySingleAsync<int>(
        """
        SELECT COUNT(*) FROM sys.dm_exec_requests r
        INNER JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id
        WHERE s.program_name = @ApplicationName
        """, new { ApplicationName }, commandTimeout: 5);

    public Task<int> CountRowsAsync(int value) => _observer.QuerySingleAsync<int>(
        $"SELECT COUNT(*) FROM {TableName} WHERE Value = @value", new { value }, commandTimeout: 5);

    public async ValueTask DisposeAsync()
    {
        List<Exception> errors = [];
        try
        {
            foreach (var repository in _repositories)
            {
                try { repository.Dispose(); }
                catch (Exception ex) { errors.Add(ex); }
            }
        }
        finally
        {
            try
            {
                if (_tableCreationAttempted)
                {
                    if (_observer.State != ConnectionState.Open)
                    {
                        _observer.Close();
                        await _observer.OpenAsync();
                    }
                    await _observer.ExecuteAsync($"DROP TABLE IF EXISTS {TableName}", commandTimeout: 5);
                    Assert.AreEqual(0, await _observer.QuerySingleAsync<int>(
                        "SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(@TableName)",
                        new { TableName }, commandTimeout: 5), "專用 SQL 測試表清理失敗。");
                }
            }
            catch (Exception ex) { errors.Add(ex); }
            finally
            {
                using var poolKey = new SqlConnection(_connectionString);
                SqlConnection.ClearPool(poolKey);
                SqlConnection.ClearPool(_observer);
                await _observer.DisposeAsync();
                Connections.Dispose();
                Operations.Dispose();
            }
        }
        if (errors.Count != 0)
        {
            throw new AggregateException("SQL 測試資源清理失敗。", errors);
        }
    }
}
