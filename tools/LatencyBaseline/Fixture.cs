using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Utility;

internal static partial class Fixture
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private sealed record Sample(double Ms, string RequestId);
    private sealed record RouteReport(string Route, Sample First, List<Sample> Samples,
        double MeanMs, bool Below100Ms);
    private sealed record ConcurrentPair(string SaveRoute, Sample Save, Sample Kill);
    private static readonly ConcurrentDictionary<string, JsonNode> Breakdown = new();
    private static string Stage = "initial";
    private sealed class FixtureAssertion(string rule) : Exception
    {
        public string Rule { get; } = rule;
    }
    private static void Require(bool condition, string rule)
    {
        if (!condition) throw new FixtureAssertion(rule);
    }

    public static async Task Run(string baseline, string connection, List<double> rtt,
        bool concurrentOnly = false, bool regressionOnly = false)
    {
        string clientId = $"core-http-fixture-{Guid.NewGuid():N}";
        string reportDirectory = Path.Combine(Path.GetDirectoryName(baseline)!,
            $"latency-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(reportDirectory);
        Process? backend = null, emulator = null;
        Guid? playerId = null;
        List<RouteReport> reports = [];
        List<ConcurrentPair> concurrentSamples = [];
        List<string> regressionChecks = [];
        bool cleaned = false;
        try
        {
            using var repository = Repository(connection);
            Require(await repository.QueryFirstAsync<int>("SELECT COUNT(*) FROM Player WHERE ClientId=@ClientId",
                new { ClientId = clientId }) == 0, "new random fixture");
            string script = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm", "node_modules", "azurite", "dist", "src", "table", "main.js");
            Require(File.Exists(script), "installed Azurite required");
            int tablePort = Port(), httpPort = Port();
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            Stage = "start owned Azurite";
            emulator = Start("node", baseline, new() { ["AZURITE_ACCOUNTS"] = $"corefixture:{key}" },
                false, script, "--inMemoryPersistence", "--silent", "--disableTelemetry",
                "--skipApiVersionCheck", "--tableHost", "127.0.0.1", "--tablePort", tablePort.ToString());
            await Ready(emulator, tablePort);
            Stage = "start captured backend";
            backend = Start("dotnet", baseline, new()
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["DOTNET_ENVIRONMENT"] = "Development",
                ["ConnectionStrings__DefaultConnection"] = connection,
                ["AzureStorage__ConnectionString"] =
                    $"DefaultEndpointsProtocol=http;AccountName=corefixture;AccountKey={key};TableEndpoint=http://127.0.0.1:{tablePort}/corefixture;",
                ["SmtpConfig__Password"] = "unused-fixture-password",
                ["Logging__LogLevel__Default"] = "Warning",
                ["API_REQUEST_TIMING"] = "1",
            }, true, Path.Combine(baseline, "reconstruct-nodes-server.dll"),
                "--urls", $"http://127.0.0.1:{httpPort}");
            await Ready(backend, httpPort);
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{httpPort}"),
                Timeout = TimeSpan.FromSeconds(60),
            };
            Stage = "provision login";
            var login = await Post(http, "DeviceAuth/Login", new { ClientId = clientId });
            Guid token = login.Data!["Token"]!.GetValue<Guid>();
            http.DefaultRequestHeaders.Add("Authorization", token.ToString());
            playerId = await repository.QueryFirstAsync<Guid>(
                "SELECT Id FROM Player WHERE ClientId=@ClientId AND IsClientLogin=1", new { ClientId = clientId });
            var characters = await Post(http, "Character/GetList", new { });
            CharacterModel core = characters.Data!.Deserialize<List<CharacterModel>>(Json)!.Single();
            Require(core.Kind == 3 && core.Id.HasValue, "valid core3");
            GameProgressModel progress = new()
            {
                Version = 1, Wave = 1, MaxWaveReached = 1,
                UnlockedBulletKinds = new() { ["0"] = JsonSerializer.SerializeToElement(true) },
                UnlockedCharacterKinds = new() { ["3"] = JsonSerializer.SerializeToElement(true) },
                Battlefield = new()
                {
                    Version = 1, Columns = 7, Rows = 10, Castle = [3, 8],
                    Roads = Enumerable.Range(0, 8).Select(y =>
                        new GameProgressRoadModel { X = 3, Y = y, Rotation = 1 }).ToList(),
                },
                // CORE3 belongs in the castle; only kinds 0..2 can be deployed on build spots.
                DeploymentLog = [],
            };
            AchievementProgressModel achievements = new()
            {
                Groups = AchievementGroups(),
            };
            Stage = "seed representative snapshots";
            var initialWave = await Post(http, "Economy/StartWave",
                new WaveStartRequest { RequestId = Guid.NewGuid(), Wave = 100, Difficulty = 0 });
            Guid killSession = initialWave.Data!["session_id"]!.GetValue<Guid>();
            await Post(http, "Economy/ReportKill", new EnemyKillRequest
                { RequestId = Guid.NewGuid(), SessionId = killSession, EnemyIndex = 306 });
            progress.RequestId = Guid.NewGuid();
            await Save(http, "GameProgress/Save", progress);
            await Save(http, "AchievementProgress/Save", achievements);
            await Save(http, "Talent/Save", new TalentModel { Version = 1 });
            long gold = (await Post(http, "Shop/GetGold", new { })).Data!.GetValue<long>();
            int amount = 0;
            // Seed and verify nonempty inventory, so read routes do not benchmark empty defaults.
            amount = (await Post(http, "Item/AdjustAmount", new ItemDeltaRequest { ItemKey = "diamond", Delta = 1, Category = "currency" }))
                .Data!.GetValue<int>();
            var weapon = await Post(http, "Weapon/Acquire", new WeaponAcquireRequest
            {
                RequestId = Guid.NewGuid(),
                Weapon = new WeaponInstanceModel { Id = "latency-staff", WeaponId = "staff",
                    WeaponFamilyId = "staff", Rarity = "common" },
            });
            Require(weapon.Data != null, "weapon provision");

            if (regressionOnly)
            {
                Stage = "snapshot regressions";
                await Regressions(http, repository, playerId.Value, progress, achievements, regressionChecks);
                Stage = "regressions completed";
                return;
            }

            if (concurrentOnly)
            {
                // Supplemental contention evidence, separate from the 100-per-endpoint baseline.
                int index = 0;
                foreach (string saveRoute in new[] { "GameProgress/Save", "AchievementProgress/Save" })
                {
                    Stage = $"concurrent {saveRoute} + Economy/ReportKill";
                    for (int i = 0; i < 11; i++)
                    {
                        progress.RequestId = Guid.NewGuid();
                        achievements.Groups[0].Total += 1;
                        Task<Sample> saveTask = saveRoute == "GameProgress/Save"
                            ? Save(http, saveRoute, progress) : Save(http, saveRoute, achievements);
                        Task<Result> killTask = Post(http, "Economy/ReportKill", new EnemyKillRequest
                        {
                            RequestId = Guid.NewGuid(), SessionId = killSession, EnemyIndex = index++,
                        });
                        await Task.WhenAll(saveTask, killTask);
                        gold += 108;
                        Require(killTask.Result.Data!["gold"]!.GetValue<long>() == gold, "concurrent exact reward");
                        // i=0 is separate first-pair evidence and is not used in measured means.
                        concurrentSamples.Add(new(saveRoute + (i == 0 ? " (first pair)" : ""),
                            saveTask.Result, killTask.Result.Sample));
                    }
                }
                Require((await Post(http, "Shop/GetGold", new { })).Data!.GetValue<long>() == gold,
                    "concurrent authoritative gold readback");
                var readback = await Post(http, "AchievementProgress/Get", new { });
                Require(readback.Data!["groups"]!.AsArray().Sum(g => g!["items"]!.AsArray().Count) == 33,
                    "all achievement items preserved under contention");
                Stage = "concurrent completed";
                return;
            }

            async Task Measure(string route, Func<int, Task<Sample>> action)
            {
                Stage = route;
                Sample first = await action(0);
                List<Sample> samples = [];
                for (int i = 1; i <= 100; i++) samples.Add(await action(i));
                double mean = samples.Average(s => s.Ms);
                reports.Add(new(route, first, samples, mean, mean < 100));
                await Persist();
                Console.WriteLine($"{route}: n=100 mean={mean:F2} ms first={first.Ms:F2} ms");
            }
            async Task<Sample> Read(string route, Action<JsonNode?> assertion)
            {
                var result = await Post(http, route, new { });
                assertion(result.Data);
                return result.Sample;
            }
            await Measure("Session/Validate", _ => Read("Session/Validate",
                d => Require(d?["LoginKind"]?.GetValue<string>() == "guest", "guest session")));
            await Measure("Character/GetList", _ => Read("Character/GetList",
                d => Require(d!.AsArray().Count == 1 && d[0]!["id"]!.GetValue<Guid>() == core.Id, "core ownership")));
            core.ExpectedRevision = core.Revision;
            await Measure("Character/Save", async _ =>
            {
                var result = await Post(http, "Character/Save", core);
                Require(result.Data!.GetValue<Guid>() == core.Id, "saved core id");
                core.ExpectedRevision = result.Revision;
                return result.Sample;
            });
            await Measure("Shop/GetGold", _ => Read("Shop/GetGold",
                d => Require(d!.GetValue<long>() == gold, "authoritative gold")));
            await Measure("Shop/GetCatalog", _ => Read("Shop/GetCatalog",
                d => Require(d is JsonObject obj && obj.Count > 0, "nonempty shop catalog")));
            await Measure("GameProgress/Get", _ => Read("GameProgress/Get",
                d => Require(d!["battlefield"]!["roads"]!.AsArray().Count == 8 &&
                    d["deployment_log"]!.AsArray().Count == 0, "roads and castle-only core persisted")));
            await Measure("GameProgress/Save", async _ =>
            {
                progress.RequestId = Guid.NewGuid();
                return await Save(http, "GameProgress/Save", progress);
            });
            await Measure("Talent/GetSnapshot", _ => Read("Talent/GetSnapshot",
                d => Require(d!["version"]!.GetValue<int>() == 1, "talent version")));
            await Measure("Item/GetInStockList", _ => Read("Item/GetInStockList",
                d => Require(d!.AsArray().Count > 0, "nonempty fixture inventory")));
            await Measure("Item/AdjustAmount", async _ =>
            {
                var result = await Post(http, "Item/AdjustAmount",
                    new ItemDeltaRequest { ItemKey = "diamond", Delta = 1, Category = "currency" });
                Require(result.Data!.GetValue<int>() == ++amount, "exact item delta");
                return result.Sample;
            });
            await Measure("Weapon/GetInstanceList", _ => Read("Weapon/GetInstanceList",
                d => Require(d!.AsArray().Count == 1, "fixture weapon")));
            await Measure("AchievementProgress/Get", _ => Read("AchievementProgress/Get",
                d => Require(d!["groups"]!.AsArray().Count == 6, "six populated achievement groups")));
            await Measure("AchievementProgress/Save", async i =>
            {
                achievements.Groups[0].Total = 25 + i;
                return await Save(http, "AchievementProgress/Save", achievements);
            });
            await Measure("Economy/StartWave", async _ =>
            {
                Guid id = Guid.NewGuid();
                var result = await Post(http, "Economy/StartWave",
                    new WaveStartRequest { RequestId = id, Wave = 100, Difficulty = 0 });
                Require(result.Data!["session_id"]!.GetValue<Guid>() == id &&
                    result.Data["enemy_count"]!.GetValue<int>() == 307, "fresh owned wave");
                return result.Sample;
            });
            await Measure("Economy/ReportKill", async i =>
            {
                var result = await Post(http, "Economy/ReportKill", new EnemyKillRequest
                { RequestId = Guid.NewGuid(), SessionId = killSession, EnemyIndex = i });
                gold += 108;
                Require(result.Data!["gold"]!.GetValue<long>() == gold, "exact unique kill reward");
                return result.Sample;
            });
            Stage = "final readback";
            Require((await Post(http, "Shop/GetGold", new { })).Data!.GetValue<long>() == gold, "gold readback");
            var savedProgress = await Post(http, "GameProgress/Get", new { });
            Require(savedProgress.Data!["battlefield"]!["roads"]!.AsArray().Count == 8, "roads readback");
        }
        catch (Exception error)
        {
            // Only this local stage label and exception type are public; never provider messages.
            Console.Error.WriteLine($"Fixture blocked at {Stage} ({error.GetType().Name}).");
            if (error is FixtureAssertion assertion) Console.Error.WriteLine($"Rule: {assertion.Rule}");
            throw;
        }
        finally
        {
            try { Stop(backend); }
            finally
            {
                try { Stop(emulator); }
                finally
                {
                    try { await Cleanup(connection, clientId, playerId); cleaned = true; }
                    finally { await Persist(); }
                }
            }
        }

        async Task Persist()
        {
            // Deliberate requested report artifact, not a process log or private config dump.
            var output = new { CapturedBackend = Path.GetFileName(baseline), Stage, CleanupVerified = cleaned,
                SelectOneRtt = rtt, Routes = reports,
                Complete = reports.Count == 15, AllMeansBelow100Ms = reports.Count == 15 && reports.All(r => r.Below100Ms),
                Concurrency = concurrentOnly
                    ? "Supplemental: one first pair then 10 measured save/kill pairs per save route."
                    : "Sequential per-route baseline; concurrent scenario recorded separately.",
                ConcurrentScenarios = concurrentSamples,
                RegressionChecks = regressionChecks,
                Breakdown = Breakdown.ToDictionary(pair => pair.Key, pair => pair.Value) };
            await File.WriteAllTextAsync(Path.Combine(reportDirectory, "baseline.json"),
                JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Report: {reportDirectory}");
        }
    }

    private static List<AchievementGroupModel> AchievementGroups()
    {
        // Client AchievementCatalog / AchievementGroup targets and get_id naming.
        return new[] {
            ("enemy_kills", new[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000 }),
            ("waves_intro", new[] { 10, 30, 50, 80, 100 }),
            ("waves_normal", new[] { 10, 30, 50, 80, 100 }),
            ("waves_hard", new[] { 10, 30, 50, 80, 100 }),
            ("weapons_collected", new[] { 1, 5, 10, 20, 30, 40 }),
            ("affixes_seen", new[] { 1, 5, 10, 20, 30 }),
        }.Select(pair => new AchievementGroupModel { Name = pair.Item1, Total = 25,
            Items = pair.Item2.Select(target => new AchievementItemModel {
                Name = $"{pair.Item1}_{target}", Target = target }).ToList() }).ToList();
    }

    private sealed record Result(JsonNode? Data, Sample Sample, long? Revision);
    private static async Task<Result> Post(HttpClient http, string route, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
        request.Headers.Add("Encode", Encode.New());
        var sw = Stopwatch.StartNew();
        using var response = await http.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        sw.Stop();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var failure = JsonNode.Parse(bytes);
            string? messageKey = failure?["MessageKey"]?.GetValue<string>();
            if (messageKey != null && System.Text.RegularExpressions.Regex.IsMatch(messageKey, @"\A[a-zA-Z0-9_.]+\z"))
                Console.Error.WriteLine($"Rejected {route}: HTTP {(int)response.StatusCode}, key={messageKey}");
        }
        Require(response.StatusCode == HttpStatusCode.OK, $"HTTP {route}");
        var envelope = JsonNode.Parse(bytes)!;
        Require(envelope["Success"]!.GetValue<bool>(), $"business {route}");
        string id = response.Headers.GetValues("X-Request-Timing-Id").Single();
        long? revision = response.Headers.TryGetValues("X-State-Revision", out var values)
            ? long.Parse(values.Single()) : null;
        return new(envelope["Data"]?.DeepClone(), new(sw.Elapsed.TotalMilliseconds, id), revision);
    }

    private static async Task<Sample> Save<T>(HttpClient http, string route, T model) where T : IRevisionedSnapshot
    {
        model.ExpectedRevision ??= model.Revision;
        var result = await Post(http, route, model);
        Require(result.Revision.HasValue, "save revision returned");
        model.Revision = result.Revision!.Value;
        model.ExpectedRevision = result.Revision;
        return result.Sample;
    }

    private static DapperRepository Repository(string connection) => new(new DapperDBContext(connection),
        new DbConnectionLimitService(2, 2), new DbOperateLimitService(2, 2));
    private static int Port()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    private static Process Start(string executable, string directory, Dictionary<string, string> environment,
        bool collect, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = directory,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, item) =>
        {
            if (collect && item.Data?.StartsWith("{\"Kind\":\"RequestTiming\",", StringComparison.Ordinal) == true)
            {
                var node = JsonNode.Parse(item.Data)!;
                Breakdown[node["RequestId"]!.GetValue<string>()] = node;
            }
        };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
    private static async Task Ready(Process process, int port)
    {
        for (int i = 0; i < 120; i++)
        {
            Require(!process.HasExited, "owned process alive");
            try { using var tcp = new TcpClient(); await tcp.ConnectAsync(IPAddress.Loopback, port); return; }
            catch (SocketException) { await Task.Delay(500); }
        }
        throw new TimeoutException("Owned process startup");
    }
    private static void Stop(Process? process)
    {
        if (process == null) return;
        using (process)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            Require(process.WaitForExit(15000), "owned process stopped");
        }
    }
    private static async Task Cleanup(string connection, string clientId, Guid? expected)
    {
        using var repository = Repository(connection);
        var ids = (await repository.QueryAsync<Guid>(
            "SELECT Id FROM Player WHERE ClientId=@ClientId AND IsClientLogin=1",
            new { ClientId = clientId })).ToList();
        Require(ids.Count <= 1, "unique fixture cleanup identity");
        if (ids.Count == 0) { Require(!expected.HasValue, "fixture identity remains locatable"); return; }
        Guid id = ids.Single();
        Require(!expected.HasValue || id == expected, "exact fixture identity");
        string[] tables = ["CharacterSkillNode", "CharacterSkillTree", "Deployment", "Character",
            "WeaponAffixValue", "WeaponAffix", "WeaponUniqueValue", "Weapon", "EconomyRequest", "PlayerParams", "Item",
            "AchievementCollected", "AchievementDetail", "Achievement", "TalentAcquired", "TalentLegacyCategory",
            "Talent", "BattlefieldRoad"];
        var ownedTables = await repository.QueryAsync<string>(
            """
            SELECT QUOTENAME(s.name)+'.'+QUOTENAME(t.name)
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            JOIN sys.columns c ON c.object_id=t.object_id WHERE c.name='CreateUserId'
            """);
        using var transaction = repository.OpenTransaction();
        foreach (string table in tables)
            await repository.ExecuteAsync($"DELETE FROM [{table}] WHERE CreateUserId=@PlayerId", new { PlayerId = id });
        await repository.ExecuteAsync(
            "DELETE FROM Player WHERE Id=@PlayerId AND ClientId=@ClientId AND IsClientLogin=1",
            new { PlayerId = id, ClientId = clientId });
        foreach (string table in ownedTables)
            Require(await repository.QueryFirstAsync<int>($"SELECT COUNT(*) FROM {table} WHERE CreateUserId=@PlayerId",
                new { PlayerId = id }) == 0, "zero fixture-owned residue");
        Require(await repository.QueryFirstAsync<int>("SELECT COUNT(*) FROM Player WHERE ClientId=@ClientId",
            new { ClientId = clientId }) == 0, "zero fixture identity residue");
        transaction.Commit();
        Console.WriteLine("Exact fixture cleanup verified; zero owned rows.");
    }
}
