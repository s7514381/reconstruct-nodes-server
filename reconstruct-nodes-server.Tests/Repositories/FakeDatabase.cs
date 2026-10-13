using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Tests.Repositories;

internal sealed class FakeDatabase
{
    private readonly object _gate = new();
    public int OpenConnections { get; private set; }
    public int PeakConnections { get; private set; }
    public int Operations { get; private set; }
    public int PeakOperations { get; private set; }
    public int Opens { get; private set; }
    public int Commits { get; set; }
    public int Rollbacks { get; set; }
    public bool FailOpen { get; set; }
    public bool FailCommand { get; set; }
    public bool FailBegin { get; set; }
    public bool FailCommit { get; set; }
    public bool FailTransactionDispose { get; set; }
    public Action? OnTransactionAction { get; set; }
    public TaskCompletionSource? BlockCommands { get; set; }
    public int ExpectedOperations { get; set; } = 1;
    public TaskCompletionSource ReachedOperations { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<DbTransaction?> CommandTransactions { get; } = [];

    public void Open()
    {
        if (FailOpen) { throw new InvalidOperationException("open failed"); }
        lock (_gate)
        {
            Opens++;
            OpenConnections++;
            PeakConnections = Math.Max(PeakConnections, OpenConnections);
        }
    }

    public void Close()
    {
        lock (_gate) { OpenConnections--; }
    }

    public async Task CommandAsync(DbTransaction? transaction)
    {
        lock (_gate)
        {
            CommandTransactions.Add(transaction);
            Operations++;
            PeakOperations = Math.Max(PeakOperations, Operations);
            if (Operations == ExpectedOperations) { ReachedOperations.TrySetResult(); }
        }
        try
        {
            if (BlockCommands != null) { await BlockCommands.Task; }
            if (FailCommand) { throw new InvalidOperationException("command failed"); }
        }
        finally
        {
            lock (_gate) { Operations--; }
        }
    }
}

internal sealed class FakeContext(FakeDatabase database) : DapperDBContext(null)
{
    public override DbConnection CreateConnection() => new FakeConnection(database);
}

internal sealed class FakeConnection(FakeDatabase database) : DbConnection
{
    private ConnectionState _state;
    [AllowNull]
    public override string ConnectionString { get; set; } = "";
    public override string Database => "fake";
    public override string DataSource => "fake";
    public override string ServerVersion => "1";
    public override ConnectionState State => _state;
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    public override void Open()
    {
        database.Open();
        _state = ConnectionState.Open;
    }
    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Open();
        return Task.CompletedTask;
    }
    public override void Close()
    {
        if (_state == ConnectionState.Open) { database.Close(); }
        _state = ConnectionState.Closed;
    }
    protected override DbCommand CreateDbCommand() => new FakeCommand(this, database);
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        database.OnTransactionAction?.Invoke();
        if (database.FailBegin) { throw new InvalidOperationException("begin failed"); }
        return new FakeTransaction(this, database);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Close(); }
        base.Dispose(disposing);
    }
}

internal sealed class FakeTransaction(DbConnection connection, FakeDatabase database) : DbTransaction
{
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    protected override DbConnection DbConnection => connection;
    public override void Commit()
    {
        database.OnTransactionAction?.Invoke();
        if (database.FailCommit) { throw new InvalidOperationException("commit failed"); }
        database.Commits++;
    }
    public override void Rollback()
    {
        database.OnTransactionAction?.Invoke();
        database.Rollbacks++;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { database.OnTransactionAction?.Invoke(); }
        if (disposing && database.FailTransactionDispose)
        {
            throw new InvalidOperationException("transaction dispose failed");
        }
        base.Dispose(disposing);
    }
}

internal sealed class FakeCommand(DbConnection connection, FakeDatabase database) : DbCommand
{
    private readonly FakeParameters _parameters = new();
    [AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; } = connection;
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection => _parameters;
    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new FakeParameter();
    public override int ExecuteNonQuery()
    {
        database.CommandAsync(DbTransaction).GetAwaiter().GetResult();
        return 1;
    }
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        await database.CommandAsync(DbTransaction);
        return 1;
    }
    public override object ExecuteScalar()
    {
        database.CommandAsync(DbTransaction).GetAwaiter().GetResult();
        return 1;
    }
    private static DbDataReader Results()
    {
        var table = new DataTable();
        table.Columns.Add("Value", typeof(int));
        table.Columns.Add("id", typeof(int));
        table.Rows.Add(1, 1);
        return new DataTableReader([table, table.Copy()]);
    }
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        database.CommandAsync(DbTransaction).GetAwaiter().GetResult();
        return Results();
    }
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior, CancellationToken cancellationToken)
    {
        await database.CommandAsync(DbTransaction);
        return Results();
    }
}

internal sealed class FakeParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; }
    [AllowNull]
    public override string ParameterName { get; set; } = "";
    [AllowNull]
    public override string SourceColumn { get; set; } = "";
    public override object? Value { get; set; }
    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }
    public override void ResetDbType() { }
}

internal sealed class FakeParameters : DbParameterCollection
{
    private readonly List<DbParameter> _items = [];
    public override int Count => _items.Count;
    public override object SyncRoot => this;
    public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
    public override void AddRange(Array values) { foreach (var value in values) { Add(value!); } }
    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains((DbParameter)value);
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
    public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _items.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
    protected override DbParameter GetParameter(int index) => _items[index];
    protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) => SetParameter(IndexOf(parameterName), value);
}
