using Microsoft.Extensions.Configuration.Json;

namespace ReconstructNodesServer.Utility;

public static class ApplicationBuilderFactory
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        using var hostConfiguration = new ConfigurationManager();
        hostConfiguration
            .AddEnvironmentVariables("ASPNETCORE_")
            .AddEnvironmentVariables("DOTNET_")
            .AddCommandLine(args);

#if DEBUG
        string defaultEnvironment = Environments.Development;
#else
        string defaultEnvironment = Environments.Production;
#endif

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            EnvironmentName = hostConfiguration[HostDefaults.EnvironmentKey] ?? defaultEnvironment
        });

        var environmentSettings = builder.Configuration.Sources
            .OfType<JsonConfigurationSource>()
            .Single(source => source.Path == $"appsettings.{builder.Environment.EnvironmentName}.json");
        int insertionIndex = builder.Configuration.Sources.IndexOf(environmentSettings) + 1;
        foreach (string fileName in new[] { "azure-storage.json", "smtp.json", "broadcast-admin.json" })
        {
            builder.Configuration.Sources.Insert(insertionIndex++, new JsonConfigurationSource
            {
                FileProvider = builder.Environment.ContentRootFileProvider,
                Path = System.IO.Path.Combine("Secret", fileName),
                Optional = true,
                ReloadOnChange = false
            });
        }
        foreach (var (fileName, isWebClient) in new[]
                 { ("google-desktop.json", false), ("google-web.json", true) })
        {
            builder.Configuration.Sources.Insert(insertionIndex++,
                new GoogleOAuthConfigurationSource
                {
                    FileProvider = builder.Environment.ContentRootFileProvider,
                    Path = System.IO.Path.Combine("Secret", fileName),
                    IsWebClient = isWebClient,
                    Optional = true,
                    ReloadOnChange = false
                });
        }

        return builder;
    }
}
