using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Tests.Services;

/// <summary>
/// 明確授權才執行：一個隨機訪客、自己的 HTTP/Azurite 程序，finally 精準清除 fixture。
/// 不執行 migration、不使用既有訪客、不向正式 Azure 寫入 HTTP 日誌。
/// </summary>
[TestClass]
[TestCategory("CoreHttpIntegration")]
[DoNotParallelize]
public class CoreHttpIntegrationTests
{
    [TestMethod]
    public async Task RealHttp_CoreEquipmentSkillsAndRelogin_PreserveGuid_AndCleanupFixture()
    {
        if (Environment.GetEnvironmentVariable("CORE_HTTP_SQL_TESTS") != "1")
        {
            Assert.Inconclusive("Requires explicit CORE_HTTP_SQL_TESTS=1 approval.");
        }
        string connection = Required("CHARACTER_SHOP_SQL_CONNECTION");
        string project = Required("CORE_HTTP_SERVER_DIRECTORY");
        string azuriteScript = Required("CORE_HTTP_AZURITE_TABLE_SCRIPT");
        string serverDll = Path.Combine(project, "bin", "Debug", "net10.0", "reconstruct-nodes-server.dll");
        Assert.IsTrue(File.Exists(serverDll), "Build the Debug backend before opting in.");
        Assert.IsTrue(File.Exists(azuriteScript), "An installed local Azurite table emulator is required.");
        var sql = new System.Data.SqlClient.SqlConnectionStringBuilder(connection);
        string host = sql.DataSource.Split('\\', ',')[0];
        Assert.IsTrue(host is "." or "(local)" or "(localdb)" or "localhost" or "127.0.0.1" ||
            host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase),
            "This fixture only permits a local development SQL Server.");

        string clientId = $"core-http-fixture-{Guid.NewGuid():N}";
        string weaponEntry = $"core-fixture-weapon-{Guid.NewGuid():N}";
        Guid? fixtureId = null;
        Process? backend = null;
        Process? emulator = null;
        using var repository = Repository(connection);
        Assert.AreEqual(0, await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM Player WHERE ClientId=@ClientId", new { ClientId = clientId }));
        Console.WriteLine($"Fixture ClientId: {clientId}");
        try
        {
            int tablePort = FreePort();
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            emulator = Start("node", project, new Dictionary<string, string>
            {
                ["AZURITE_ACCOUNTS"] = $"corefixture:{key}",
            }, azuriteScript, "--inMemoryPersistence", "--silent", "--disableTelemetry",
                "--skipApiVersionCheck", "--tableHost", "127.0.0.1", "--tablePort", tablePort.ToString());
            await WaitForPort(emulator, tablePort);
            int httpPort = FreePort();
            string address = $"http://127.0.0.1:{httpPort}";
            backend = Start("dotnet", project, new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["DOTNET_ENVIRONMENT"] = "Development",
                ["ConnectionStrings__DefaultConnection"] = connection,
                ["AzureStorage__ConnectionString"] =
                    $"DefaultEndpointsProtocol=http;AccountName=corefixture;AccountKey={key};TableEndpoint=http://127.0.0.1:{tablePort}/corefixture;",
                ["SmtpConfig__Password"] = "unused-fixture-password",
                ["Logging__LogLevel__Default"] = "Warning",
            }, serverDll, "--urls", address);
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(30),
            };
            await WaitForHttp(backend, http);
            Console.WriteLine($"Owned backend PID {backend.Id}, port {httpPort}; owned in-memory Azurite PID {emulator.Id}.");

            BackendLoginData login = await Post<BackendLoginData>(http, "DeviceAuth/Login", new { ClientId = clientId });
            http.DefaultRequestHeaders.Add("Authorization", login.Token.ToString());
            var players = (await repository.QueryAsync<Guid>(
                "SELECT Id FROM Player WHERE ClientId=@ClientId AND IsClientLogin=1", new { ClientId = clientId })).ToList();
            Assert.HasCount(1, players);
            fixtureId = players.Single();
            Console.WriteLine($"Fixture player GUID: {fixtureId}");
            CharacterModel core = (await Post<List<CharacterModel>>(http, "Character/GetList", new { })).Single();
            Assert.AreEqual(3, core.Kind);
            Assert.IsTrue(core.Id.HasValue && core.Id != Guid.Empty);
            Assert.IsTrue(string.IsNullOrEmpty(core.WeaponEntryId));
            Assert.AreEqual(1, core.SkillTrees.Single().Kind);
            Assert.HasCount(0, core.SkillTrees.Single().AuxiliaryNodes);
            Guid coreId = core.Id.Value;
            CharacterModel repeated = (await Post<List<CharacterModel>>(http, "Character/GetList", new { })).Single();
            Assert.AreEqual(coreId, repeated.Id);
            Console.WriteLine("PASS: new guest has exactly one CORE3, FIREBALL1, no weapon; repeated read preserves GUID.");

            using (var seed = repository.OpenTransaction())
            {
                var argument = SnapshotSaveTests.CreateServiceArgument(repository, fixtureId.Value);
                await new WeaponService(argument).Save(new WeaponInstanceModel
                {
                    Id = weaponEntry, WeaponId = "staff", WeaponFamilyId = "staff", Rarity = "common",
                });
                seed.Commit();
            }
            core.WeaponEntryId = weaponEntry;
            core.WeaponQualityVersion = 2;
            core.SkillTrees[0].AuxiliaryNodes =
                [new GameProgressSkillNodeModel { Type = "active", Kind = 0 }];
            Assert.AreEqual(coreId, await Post<Guid>(http, "Character/Save", core));
            await AssertPersisted(http, coreId, weaponEntry, emptySkills: false);
            Console.WriteLine("PASS: fixture-only WeaponService seed; HTTP weapon and changed skill-node save/read.");

            core.SkillTrees = [];
            await Post<Guid>(http, "Character/Save", core);
            await AssertPersisted(http, coreId, weaponEntry, emptySkills: true);
            login = await Post<BackendLoginData>(http, "DeviceAuth/Login", new { ClientId = clientId });
            http.DefaultRequestHeaders.Remove("Authorization");
            http.DefaultRequestHeaders.Add("Authorization", login.Token.ToString());
            await AssertPersisted(http, coreId, weaponEntry, emptySkills: true);
            Console.WriteLine("PASS: intentionally empty skills and equipped weapon survive real same-ClientId relogin.");

            core.SkillTrees = [new GameProgressSkillLoadoutModel
            {
                Kind = 1, AuxiliaryNodes = [new GameProgressSkillNodeModel { Type = "active", Kind = 0 }],
            }];
            await Post<Guid>(http, "Character/Save", core);
            Guid oldToken = login.Token;
            login = await Post<BackendLoginData>(http, "DeviceAuth/Login", new { ClientId = clientId });
            Assert.AreNotEqual(oldToken, login.Token);
            http.DefaultRequestHeaders.Remove("Authorization");
            http.DefaultRequestHeaders.Add("Authorization", login.Token.ToString());
            await AssertPersisted(http, coreId, weaponEntry, emptySkills: false);
            Assert.AreEqual(1, await repository.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM Player WHERE ClientId=@ClientId AND IsClientLogin=1", new { ClientId = clientId }));
            Assert.AreEqual(1, await repository.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM Character WHERE CreateUserId=@PlayerId AND Kind=3 AND Status != -1",
                new { PlayerId = fixtureId }));
            Console.WriteLine("PASS: restored FIREBALL1 with active node, same core GUID/weapon after second relogin; one guest/core.");
        }
        finally
        {
            // 先停止自己擁有的程序，避免背景請求與清理交錯；絕不依程序名稱停止其他服務。
            try { StopOwned(backend); }
            finally
            {
                try { StopOwned(emulator); }
                finally { await Cleanup(connection, clientId, fixtureId); }
            }
        }
    }

    private static async Task AssertPersisted(HttpClient http, Guid id, string weapon, bool emptySkills)
    {
        var core = (await Post<List<CharacterModel>>(http, "Character/GetList", new { })).Single();
        Assert.AreEqual(id, core.Id);
        Assert.AreEqual(3, core.Kind);
        Assert.AreEqual(weapon, core.WeaponEntryId);
        Assert.AreEqual("staff", core.WeaponId);
        Assert.AreEqual(2, core.WeaponQualityVersion);
        if (emptySkills) { Assert.HasCount(0, core.SkillTrees); }
        else
        {
            Assert.AreEqual(1, core.SkillTrees.Single().Kind);
            Assert.AreEqual("active", core.SkillTrees.Single().AuxiliaryNodes.Single().Type);
            Assert.AreEqual(0, core.SkillTrees.Single().AuxiliaryNodes.Single().Kind);
        }
    }

    private static async Task<T> Post<T>(HttpClient http, string path, object body)
    {
        using var response = await http.PostAsJsonAsync(path, body);
        // 不輸出 response body、Token 或連線字串。
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"HTTP failure at {path}");
        var envelope = await response.Content.ReadFromJsonAsync<BackendApiResult<T>>();
        Assert.IsNotNull(envelope);
        Assert.IsTrue(envelope.Success, $"Business failure at {path}: {envelope.MessageKey}");
        return envelope.Data;
    }

    private static async Task Cleanup(string connection, string clientId, Guid? expectedId)
    {
        using var repository = Repository(connection);
        var players = (await repository.QueryAsync<Guid>(
            "SELECT Id FROM Player WHERE ClientId=@ClientId AND IsClientLogin=1", new { ClientId = clientId })).ToList();
        Assert.IsLessThanOrEqualTo(1, players.Count, "Unexpected duplicate fixture identity; manual scoped investigation required.");
        Guid? id = players.Count == 1 ? players[0] : expectedId;
        if (expectedId.HasValue && players.Count == 1) { Assert.AreEqual(expectedId.Value, players[0]); }
        if (id.HasValue)
        {
            string[] tables = ["CharacterSkillNode", "CharacterSkillTree", "Deployment", "Character",
                "WeaponAffixValue", "WeaponAffix", "WeaponUniqueValue", "Weapon", "EconomyRequest", "PlayerParams", "Item"];
            using (var cleanup = repository.OpenTransaction())
            {
                foreach (string table in tables)
                {
                    await repository.ExecuteAsync($"DELETE FROM [{table}] WHERE CreateUserId=@PlayerId", new { PlayerId = id });
                }
                await repository.ExecuteAsync(
                    "DELETE FROM Player WHERE Id=@PlayerId AND ClientId=@ClientId AND IsClientLogin=1",
                    new { PlayerId = id, ClientId = clientId });
                cleanup.Commit();
            }
            // 檢查所有具有 CreateUserId 的實體表，而不只檢查預期有寫入的表。
            var ownedTables = await repository.QueryAsync<string>(
                """
                SELECT QUOTENAME(s.name)+'.'+QUOTENAME(t.name)
                FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
                JOIN sys.columns c ON c.object_id=t.object_id WHERE c.name='CreateUserId'
                """, null);
            int checkedTables = 0;
            foreach (string table in ownedTables)
            {
                Assert.AreEqual(0, await repository.QueryFirstAsync<int>(
                    $"SELECT COUNT(*) FROM {table} WHERE CreateUserId=@PlayerId", new { PlayerId = id }),
                    $"Fixture residue in {table}");
                checkedTables++;
            }
            Console.WriteLine($"CLEANUP: zero fixture-owned rows across {checkedTables} ownership tables.");
        }
        Assert.AreEqual(0, await repository.QueryFirstAsync<int>(
            "SELECT COUNT(*) FROM Player WHERE ClientId=@ClientId", new { ClientId = clientId }));
        Console.WriteLine("CLEANUP: zero Player rows for unique fixture ClientId; owned processes stopped.");
    }

    private static DapperRepository Repository(string connection) =>
        new(new DapperDBContext(connection), new DbConnectionLimitService(2, 2), new DbOperateLimitService(2, 2));

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Missing opt-in setting: {name}");

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Process Start(string executable, string directory, Dictionary<string, string> environment, params string[] args)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (string arg in args) { info.ArgumentList.Add(arg); }
        foreach (var (name, value) in environment) { info.Environment[name] = value; }
        var process = Process.Start(info) ?? throw new InvalidOperationException("Owned process failed to start.");
        // 消耗輸出但不儲存／印出，避免框架診斷意外包含敏感設定。
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitForPort(Process process, int port)
    {
        for (int attempt = 0; attempt < 120; attempt++)
        {
            Assert.IsFalse(process.HasExited, "Owned Azurite exited during startup.");
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, port);
                return;
            }
            catch (SocketException) { await Task.Delay(500); }
        }
        Assert.Fail("Owned Azurite did not become responsive within 60 seconds.");
    }

    private static async Task WaitForHttp(Process process, HttpClient http)
    {
        for (int attempt = 0; attempt < 120; attempt++)
        {
            Assert.IsFalse(process.HasExited, "Owned backend exited during startup.");
            try
            {
                using var response = await http.GetAsync("swagger/v1/swagger.json");
                if (response.StatusCode == HttpStatusCode.OK) { return; }
            }
            catch (HttpRequestException) { }
            await Task.Delay(500);
        }
        Assert.Fail("Owned backend did not become HTTP responsive within 60 seconds.");
    }

    private static void StopOwned(Process? process)
    {
        if (process == null) { return; }
        using (process)
        {
            int pid = process.Id;
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
            Assert.IsTrue(process.WaitForExit(15000), $"Owned PID {pid} failed to stop.");
            Console.WriteLine($"STOPPED owned PID {pid}.");
        }
    }
}
