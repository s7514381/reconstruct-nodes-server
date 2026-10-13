using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ReconstructNodesServer.Services
{
    public sealed class DatabaseConcurrencyOptions
    {
        public const string SectionName = "DatabaseConcurrency";
        public int MaxConnections { get; set; } = 10;
        public int MaxOperations { get; set; } = 10;
    }

    public static class DatabaseConcurrencyRegistration
    {
        public static IServiceCollection AddDatabaseConcurrency(
            this IServiceCollection services, IConfiguration configuration)
        {
            var options = configuration.GetSection(DatabaseConcurrencyOptions.SectionName)
                .Get<DatabaseConcurrencyOptions>() ?? new DatabaseConcurrencyOptions();
            if (options.MaxConnections <= 0 || options.MaxOperations <= 0)
            {
                throw new InvalidOperationException(
                    "DatabaseConcurrency:MaxConnections 與 MaxOperations 必須為正整數。");
            }

            services.TryAddSingleton<DbConnectionLimitService>(_ =>
                new DbConnectionLimitService(options.MaxConnections, options.MaxConnections));
            services.TryAddSingleton<DbOperateLimitService>(_ =>
                new DbOperateLimitService(options.MaxOperations, options.MaxOperations));
            return services;
        }
    }
}
