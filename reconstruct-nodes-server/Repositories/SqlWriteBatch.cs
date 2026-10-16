using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Utility;

namespace ReconstructNodesServer.Repositories;

internal sealed class SqlWriteBatch(Guid? owner)
{
    private readonly StringBuilder sql = new();
    private readonly List<(string Sql, DynamicParameters Parameters)> completed = [];
    private int command;
    public DynamicParameters Parameters { get; private set; } = new(new { CreateUserId = owner });
    public IReadOnlyList<SqlBatchCommand> Commands =>
        completed.Append((Sql: sql.ToString(), Parameters)).Select(c => new SqlBatchCommand(c.Sql, c.Parameters)).ToArray();

    // Rename parameters and local variables together; fragments only come from server code.
    public void Add(string fragment, object? parameters = null)
    {
        // Bound compilation work as well as SQL Server's 2100-parameter limit.
        if (command >= 24 || Parameters.ParameterNames.Count() > 1500)
        {
            completed.Add((sql.ToString(), Parameters));
            sql.Clear();
            Parameters = new DynamicParameters(new { CreateUserId = owner });
            command = 0;
        }
        string suffix = "_" + command++;
        sql.AppendLine(Regex.Replace(fragment, @"(?<!@)@[A-Za-z_][A-Za-z_0-9]*",
            match => match.Value == "@CreateUserId" ? match.Value : match.Value + suffix));
        if (parameters == null) { return; }
        foreach (var property in parameters.GetType().GetProperties())
        {
            if (property.Name != "CreateUserId")
            { Parameters.Add(property.Name + suffix, property.GetValue(parameters)); }
        }
    }

    public void Insert<T>(IEnumerable<T> source) where T : ModelBase
    {
        T[] rows = source.ToArray();
        if (rows.Length == 0) { return; }
        foreach (T row in rows) { row.CreateUserId = owner; }
        var properties = Columns<T>();
        string names = string.Join(", ", properties.Select(p => $"[{p.Name}]"));
        Add($"""
            INSERT INTO [{CommonUtility.GetTableAttribute<T>()}] ({names})
            SELECT {names} FROM OPENJSON(@Rows) WITH (
                {string.Join(", ", properties.Select(p => $"[{p.Name}] {SqlType(p.PropertyType)}"))});
            """, new { Rows = JsonSerializer.Serialize(rows) });
    }

    public void Update<T>(IEnumerable<T> source) where T : ModelBase
    {
        T[] rows = source.ToArray();
        if (rows.Length == 0) { return; }
        var properties = Columns<T>();
        Add($"""
            UPDATE t SET {string.Join(", ", properties.Where(p => p.Name is not ("Id" or "CreateUserId"))
                .Select(p => $"[{p.Name}] = r.[{p.Name}]"))}
            FROM [{CommonUtility.GetTableAttribute<T>()}] t
            JOIN OPENJSON(@Rows) WITH (
                {string.Join(", ", properties.Select(p => $"[{p.Name}] {SqlType(p.PropertyType)}"))}
            ) r ON r.Id = t.Id
            WHERE t.CreateUserId = @CreateUserId;
            """, new { Rows = JsonSerializer.Serialize(rows) });
    }

    private static PropertyInfo[] Columns<T>() where T : ModelBase => typeof(T).GetProperties()
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0).ToArray();

    private static string SqlType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum || type == typeof(int)) { return "int"; }
        if (type == typeof(long)) { return "bigint"; }
        if (type == typeof(Guid)) { return "uniqueidentifier"; }
        if (type == typeof(DateTime)) { return "datetime2"; }
        if (type == typeof(bool)) { return "bit"; }
        if (type == typeof(float)) { return "real"; }
        if (type == typeof(double)) { return "float"; }
        if (type == typeof(string)) { return "nvarchar(max)"; }
        throw new NotSupportedException($"Unsupported batch column type {type.Name}.");
    }
}
