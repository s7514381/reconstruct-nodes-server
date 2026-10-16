using System.Diagnostics;
using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using ReconstructNodesServer.Utility;

// Never print exception messages: providers may include private configuration.
try
{
    if (args.Contains("--self-test"))
    {
        Fixture.RegressionSelfTest();
        Fixture.StatisticsSelfTest();
        Fixture.ReadsSelfTest();
        Fixture.SelectedModesSelfTest();
        Fixture.ShopSelfTest();
        Fixture.RefineSelfTest();
        return 0;
    }
    string[] modes = ["--concurrent-only", "--regression-only", "--item-only", "--reads-only", "--economy-only", "--saves-only", "--shop-only", "--refine-only", "--background-only", "--skills-only", "--talent-only"];
    if (args.Skip(1).Any(option => option.StartsWith("--", StringComparison.Ordinal) &&
        option != "--run" && !modes.Contains(option)))
        throw new ArgumentException("Unknown run option");
    if (modes.Count(args.Contains) > 1 || (modes.Any(args.Contains) && !args.Contains("--run")))
        throw new InvalidOperationException("Incompatible run modes");
    string baseline = Path.GetFullPath(args[0]);
    var builder = ApplicationBuilderFactory.CreateBuilder(
        ["--contentRoot", baseline, "--environment", "Development"]);
    builder.Configuration.AddUserSecrets(typeof(APIRequest).Assembly, optional: true);
    builder.Configuration.AddEnvironmentVariables();
    string connection = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Missing connection");
    var settings = new SqlConnectionStringBuilder(connection);
    if (!settings.DataSource.Equals("tcp:timwu19950429.database.windows.net,1433",
            StringComparison.OrdinalIgnoreCase) ||
        !settings.InitialCatalog.Equals("reconstruct-nodes", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Configured target differs from explicit authorization");
    settings.ConnectTimeout = 15;
    using var sql = new SqlConnection(settings.ConnectionString);
    await sql.OpenAsync();
    using var metadata = sql.CreateCommand();
    metadata.CommandText = "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')), DB_NAME()";
    using (var reader = await metadata.ExecuteReaderAsync())
    {
        await reader.ReadAsync();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
            Kind = "ReadOnlyTargetCheck", Server = reader.GetString(0), Database = reader.GetString(1) }));
    }
    var rtt = new List<double>();
    for (int i = 0; i < 10; i++)
    {
        using var command = sql.CreateCommand();
        command.CommandText = "SELECT 1";
        var sw = Stopwatch.StartNew();
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException("Probe failed");
        rtt.Add(sw.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
        Kind = "SelectOneRtt", Samples = rtt, MeanMs = rtt.Average() }));
    using (var jsonProbe = sql.CreateCommand())
    {
        // Read-only projection of representative types used by the set-based snapshot writes.
        jsonProbe.CommandText = """
            SELECT COUNT(*) FROM OPENJSON(
                N'[{"Id":"11111111-1111-1111-1111-111111111111","Sequence":2,
                     "CreateDate":"2026-10-15T12:00:00","Status":0,"Name":"probe",
                     "Total":25.5,"IsReward":true,"RewardDate":null,"Corner":false}]')
            WITH (Id uniqueidentifier, Sequence int, CreateDate datetime2, Status int,
                  Name nvarchar(max), Total real, IsReward bit, RewardDate datetime2, Corner bit)
            WHERE Id IS NOT NULL AND Sequence=2 AND CreateDate IS NOT NULL AND Name=N'probe'
                  AND Total=25.5 AND IsReward=1 AND RewardDate IS NULL AND Corner=0;
            """;
        if (Convert.ToInt32(await jsonProbe.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException("OPENJSON typed projection unavailable");
        Console.WriteLine("Read-only OPENJSON typed projection passed; fixture seed/regressions validate actual INSERT schemas.");
    }
    if (args.Contains("--run"))
        await Fixture.Run(baseline, connection, rtt, args.Contains("--concurrent-only"),
            args.Contains("--regression-only"), args.Contains("--item-only"), args.Contains("--reads-only"),
            args.Contains("--economy-only"), args.Contains("--saves-only"), args.Contains("--shop-only"),
            args.Contains("--refine-only"), args.Contains("--background-only"), args.Contains("--skills-only"), args.Contains("--talent-only"));
    return (args.Contains("--background-only") && !Fixture.BackgroundBudgetMet) ||
        (args.Contains("--skills-only") && !Fixture.SkillBudgetMet) ||
        (args.Contains("--talent-only") && !Fixture.TalentBudgetMet) ? 1 : 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Baseline blocked ({error.GetType().Name}); private exception details suppressed.");
    return 2;
}
