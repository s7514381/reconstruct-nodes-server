using Microsoft.Extensions.Hosting;
using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Tests.Configuration;

[TestClass]
[DoNotParallelize]
public class ApplicationBuilderFactoryTests
{
    [TestMethod]
    public void DefaultEnvironment_FollowsBuildConfiguration()
    {
        WithEnvironment(null, null, () =>
        {
            var builder = ApplicationBuilderFactory.CreateBuilder([]);
#if DEBUG
            Assert.AreEqual(Environments.Development, builder.Environment.EnvironmentName);
#else
            Assert.AreEqual(Environments.Production, builder.Environment.EnvironmentName);
#endif
        });
    }

    [TestMethod]
    [DataRow("Development", null, "Development")]
    [DataRow("Staging", null, "Staging")]
    [DataRow("Staging", "Production", "Production")]
    public void ExplicitEnvironment_OverridesDefault(string aspnet, string? dotnet, string expected)
    {
        WithEnvironment(aspnet, dotnet, () =>
        {
            var builder = ApplicationBuilderFactory.CreateBuilder([]);
            Assert.AreEqual(expected, builder.Environment.EnvironmentName);
        });
    }

    [TestMethod]
    public void CommandLineEnvironment_OverridesBothEnvironmentVariables()
    {
        WithEnvironment("Development", "Production", () =>
        {
            var builder = ApplicationBuilderFactory.CreateBuilder(["--environment", "Staging"]);
            Assert.AreEqual("Staging", builder.Environment.EnvironmentName);
        });
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{"ConfigurationProbe":{"Changed":"production"}}""")]
    public void ProductionSettings_OnlyOverrideSpecifiedKeys(string productionJson)
    {
        string root = Path.Combine(Path.GetTempPath(), $"configuration-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string? original = Environment.GetEnvironmentVariable("ConfigurationProbe__External");
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"),
                """{"ConfigurationProbe":{"Changed":"base","Unchanged":"base","External":"base"}}""");
            File.WriteAllText(Path.Combine(root, "appsettings.Production.json"), productionJson);
            Environment.SetEnvironmentVariable("ConfigurationProbe__External", "environment");
            var builder = ApplicationBuilderFactory.CreateBuilder(
                ["--environment", "Production", "--contentRoot", root]);
            using var app = builder.Build();

            Assert.AreEqual(productionJson == "{}" ? "base" : "production",
                builder.Configuration["ConfigurationProbe:Changed"]);
            Assert.AreEqual("base", builder.Configuration["ConfigurationProbe:Unchanged"]);
            Assert.AreEqual("environment", builder.Configuration["ConfigurationProbe:External"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConfigurationProbe__External", original);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false, null, null, "production")]
    [DataRow(true, null, null, "local")]
    [DataRow(true, "environment", null, "environment")]
    [DataRow(true, "environment", "command", "command")]
    public void GoogleSettings_PreservePriority(
        bool hasLocalFile, string? environmentValue, string? commandValue, string expected)
    {
        WithGoogleSettingsRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"),
                """{"GoogleAuth":{"ClientId":"base","ServerClientId":"android","JwksCacheMinutes":60}}""");
            File.WriteAllText(Path.Combine(root, "appsettings.Production.json"),
                """{"GoogleAuth":{"ClientId":"production"}}""");
            if (hasLocalFile)
            {
                File.WriteAllText(Path.Combine(root, "Secret", "google-desktop.json"),
                    """{"installed":{"client_id":"local","client_secret":"test-secret","token_uri":"https://untrusted.invalid"}}""");
                File.WriteAllText(Path.Combine(root, "Secret", "google-web.json"),
                    """{"web":{"client_id":"local","client_secret":"unused-web-secret"}}""");
            }

            string? original = Environment.GetEnvironmentVariable("GoogleAuth__ClientId");
            string? originalServer = Environment.GetEnvironmentVariable("GoogleAuth__ServerClientId");
            try
            {
                Environment.SetEnvironmentVariable("GoogleAuth__ClientId", environmentValue);
                Environment.SetEnvironmentVariable("GoogleAuth__ServerClientId", environmentValue);
                var args = new List<string> { "--environment", "Production", "--contentRoot", root };
                if (commandValue != null)
                {
                    args.AddRange(["--GoogleAuth:ClientId", commandValue]);
                    args.AddRange(["--GoogleAuth:ServerClientId", commandValue]);
                }
                var builder = ApplicationBuilderFactory.CreateBuilder(args.ToArray());
                using var app = builder.Build();

                Assert.AreEqual(expected, builder.Configuration["GoogleAuth:ClientId"]);
                Assert.AreEqual("60", builder.Configuration["GoogleAuth:JwksCacheMinutes"]);
                Assert.AreEqual(hasLocalFile ? expected : "android", builder.Configuration["GoogleAuth:ServerClientId"]);
                if (hasLocalFile)
                {
                    Assert.AreEqual("test-secret", builder.Configuration["GoogleAuth:ClientSecret"]);
                }
                Assert.IsNull(builder.Configuration["GoogleAuth:TokenEndpoint"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable("GoogleAuth__ClientId", original);
                Environment.SetEnvironmentVariable("GoogleAuth__ServerClientId", originalServer);
            }
        });
    }

    [TestMethod]
    [DataRow(false, null, null, "production")]
    [DataRow(true, null, null, "local")]
    [DataRow(true, "environment", null, "environment")]
    [DataRow(true, "environment", "command", "command")]
    public void AzureSettings_PreservePriority(
        bool hasLocalFile, string? environmentValue, string? commandValue, string expected)
    {
        WithGoogleSettingsRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"),
                """{"AzureStorage":{"ConnectionString":"base"}}""");
            File.WriteAllText(Path.Combine(root, "appsettings.Production.json"),
                """{"AzureStorage":{"ConnectionString":"production"}}""");
            if (hasLocalFile)
            {
                File.WriteAllText(Path.Combine(root, "Secret", "azure-storage.json"),
                    """{"AzureStorage":{"ConnectionString":"local"}}""");
            }

            string? original = Environment.GetEnvironmentVariable("AzureStorage__ConnectionString");
            try
            {
                Environment.SetEnvironmentVariable("AzureStorage__ConnectionString", environmentValue);
                var args = new List<string> { "--environment", "Production", "--contentRoot", root };
                if (commandValue != null)
                {
                    args.AddRange(["--AzureStorage:ConnectionString", commandValue]);
                }
                var builder = ApplicationBuilderFactory.CreateBuilder(args.ToArray());
                using var app = builder.Build();

                Assert.AreEqual(expected, builder.Configuration["AzureStorage:ConnectionString"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable("AzureStorage__ConnectionString", original);
            }
        });
    }

    [TestMethod]
    public void AzureSettings_InvalidJsonFails()
    {
        WithGoogleSettingsRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "Secret", "azure-storage.json"), "{ invalid json");
            Assert.ThrowsExactly<InvalidDataException>(() =>
                ApplicationBuilderFactory.CreateBuilder(
                    ["--environment", "Production", "--contentRoot", root]));
        });
    }

    [TestMethod]
    [DataRow(false, null, null, "production")]
    [DataRow(true, null, null, "local")]
    [DataRow(true, "environment", null, "environment")]
    [DataRow(true, "environment", "command", "command")]
    public void SmtpSettings_PreservePriority(
        bool hasLocalFile, string? environmentValue, string? commandValue, string expected)
    {
        WithGoogleSettingsRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"),
                """{"SmtpConfig":{"Password":"base","Server":"smtp.example.invalid"}}""");
            File.WriteAllText(Path.Combine(root, "appsettings.Production.json"),
                """{"SmtpConfig":{"Password":"production"}}""");
            if (hasLocalFile)
            {
                File.WriteAllText(Path.Combine(root, "Secret", "smtp.json"),
                    """{"SmtpConfig":{"Password":"local"}}""");
            }

            string? original = Environment.GetEnvironmentVariable("SmtpConfig__Password");
            try
            {
                Environment.SetEnvironmentVariable("SmtpConfig__Password", environmentValue);
                var args = new List<string> { "--environment", "Production", "--contentRoot", root };
                if (commandValue != null)
                {
                    args.AddRange(["--SmtpConfig:Password", commandValue]);
                }
                var builder = ApplicationBuilderFactory.CreateBuilder(args.ToArray());
                using var app = builder.Build();

                Assert.AreEqual(expected, builder.Configuration["SmtpConfig:Password"]);
                Assert.AreEqual("smtp.example.invalid", builder.Configuration["SmtpConfig:Server"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SmtpConfig__Password", original);
            }
        });
    }

    [TestMethod]
    public void SmtpSettings_InvalidJsonFails()
    {
        WithGoogleSettingsRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "Secret", "smtp.json"), "{ invalid json");
            Assert.ThrowsExactly<InvalidDataException>(() =>
                ApplicationBuilderFactory.CreateBuilder(
                    ["--environment", "Production", "--contentRoot", root]));
        });
    }

    [TestMethod]
    public void SmtpSettings_CompleteSectionLoadsWithoutAppsettingsSection()
    {
        WithGoogleSettingsRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), "{}");
            File.WriteAllText(Path.Combine(root, "Secret", "smtp.json"),
                """{"SmtpConfig":{"SenderName":"Test sender","Server":"smtp.example.invalid","Port":587,"Account":"test@example.invalid","Password":"test-password","Sender":"sender@example.invalid"}}""");
            string? original = Environment.GetEnvironmentVariable("SmtpConfig__Password");
            try
            {
                Environment.SetEnvironmentVariable("SmtpConfig__Password", null);
                var builder = ApplicationBuilderFactory.CreateBuilder(
                    ["--environment", "Production", "--contentRoot", root]);
                using var app = builder.Build();
                var sender = new MailSender(builder.Configuration);

                Assert.AreEqual("Test sender", sender._config.SenderName);
                Assert.AreEqual("smtp.example.invalid", sender._config.Server);
                Assert.AreEqual(587, sender._config.Port);
                Assert.AreEqual("test@example.invalid", sender._config.Account);
                Assert.AreEqual("test-password", sender._config.Password);
                Assert.AreEqual("sender@example.invalid", sender._config.Sender);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SmtpConfig__Password", original);
            }
        });
    }

    [TestMethod]
    [DataRow("{ invalid json")]
    [DataRow("""{"web":{"client_id":"id","client_secret":"secret"}}""")]
    [DataRow("""{"installed":{"client_id":"id"}}""")]
    [DataRow("""{"installed":{"client_id":"","client_secret":"secret"}}""")]
    [DataRow("""{"installed":{"client_id":123,"client_secret":"secret"}}""")]
    [DataRow("""{"installed":{"client_id":"id","client_secret":" "}}""")]
    [DataRow("[]")]
    public void GoogleSettings_InvalidCredentialsFail(string json)
    {
        WithGoogleSettingsRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "Secret", "google-desktop.json"), json);
            Assert.ThrowsExactly<InvalidDataException>(() =>
                ApplicationBuilderFactory.CreateBuilder(
                    ["--environment", "Production", "--contentRoot", root]));
        });
    }

    [TestMethod]
    public void GoogleSettings_WebOnlyDoesNotConfigureDesktopOrLoadAndroidFile()
    {
        WithGoogleSettingsRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "Secret", "google-web.json"),
                """{"web":{"client_id":"web-id","client_secret":"unused","token_uri":"https://untrusted.invalid"}}""");
            File.WriteAllText(Path.Combine(root, "Secret", "google-android.json"), "not loaded");
            var builder = ApplicationBuilderFactory.CreateBuilder(
                ["--environment", "Production", "--contentRoot", root]);
            using var app = builder.Build();

            Assert.AreEqual("web-id", builder.Configuration["GoogleAuth:ServerClientId"]);
            Assert.IsNull(builder.Configuration["GoogleAuth:ClientId"]);
            Assert.IsNull(builder.Configuration["GoogleAuth:ClientSecret"]);
            Assert.IsNull(builder.Configuration["GoogleAuth:TokenEndpoint"]);
        });
    }

    [TestMethod]
    [DataRow("{ invalid json")]
    [DataRow("""{"installed":{"client_id":"id"}}""")]
    [DataRow("""{"web":{}}""")]
    [DataRow("""{"web":{"client_id":""}}""")]
    [DataRow("""{"web":{"client_id":123}}""")]
    [DataRow("[]")]
    public void GoogleSettings_InvalidWebCredentialsFail(string json)
    {
        WithGoogleSettingsRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "Secret", "google-web.json"), json);
            Assert.ThrowsExactly<InvalidDataException>(() =>
                ApplicationBuilderFactory.CreateBuilder(
                    ["--environment", "Production", "--contentRoot", root]));
        });
    }

    [TestMethod]
    public void GoogleSettings_DevelopmentUserSecretsOverrideLocalFile()
    {
        WithGoogleSettingsRoot(root =>
        {
            string? originalAppData = Environment.GetEnvironmentVariable("APPDATA");
            string? originalClientId = Environment.GetEnvironmentVariable("GoogleAuth__ClientId");
            string? originalServerClientId = Environment.GetEnvironmentVariable("GoogleAuth__ServerClientId");
            try
            {
                Environment.SetEnvironmentVariable("APPDATA", root);
                Environment.SetEnvironmentVariable("GoogleAuth__ClientId", null);
                Environment.SetEnvironmentVariable("GoogleAuth__ServerClientId", null);
                string secretsDirectory = Path.Combine(root, "Microsoft", "UserSecrets",
                    "reconstruct-nodes-server-local");
                Directory.CreateDirectory(secretsDirectory);
                File.WriteAllText(Path.Combine(secretsDirectory, "secrets.json"),
                    """{"GoogleAuth":{"ClientId":"test-secret","ServerClientId":"test-web-secret"}}""");
                File.WriteAllText(Path.Combine(root, "Secret", "google-desktop.json"),
                    """{"installed":{"client_id":"local","client_secret":"local-secret"}}""");
                File.WriteAllText(Path.Combine(root, "Secret", "google-web.json"),
                    """{"web":{"client_id":"local-web"}}""");
                var builder = ApplicationBuilderFactory.CreateBuilder(
                    ["--environment", "Development", "--contentRoot", root,
                     "--applicationName", typeof(ApplicationBuilderFactory).Assembly.GetName().Name!]);
                using var app = builder.Build();

                Assert.AreEqual("test-secret", builder.Configuration["GoogleAuth:ClientId"]);
                Assert.AreEqual("test-web-secret", builder.Configuration["GoogleAuth:ServerClientId"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable("APPDATA", originalAppData);
                Environment.SetEnvironmentVariable("GoogleAuth__ClientId", originalClientId);
                Environment.SetEnvironmentVariable("GoogleAuth__ServerClientId", originalServerClientId);
            }
        });
    }

    private static void WithGoogleSettingsRoot(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), $"google-configuration-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Secret"));
        try
        {
            test(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WithEnvironment(string? aspnet, string? dotnet, Action test)
    {
        string? originalAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        string? originalDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", aspnet);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", dotnet);
            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalAspnet);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", originalDotnet);
        }
    }
}
