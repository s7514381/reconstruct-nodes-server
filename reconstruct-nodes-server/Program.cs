using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library;
using System.Reflection;
using ReconstructNodesServer;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Filter;
using ReconstructNodesServer.Host;
using ReconstructNodesServer.Library.Auth;
using ReconstructNodesServer.Auth;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Utility;
using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;

var builder = ApplicationBuilderFactory.CreateBuilder(args);

// Add services to the container.
IServiceCollection services = builder.Services;
services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = ApiValidationResponse.Create;
});
services.AddExceptionHandler<ApiExceptionHandler>();
services.AddProblemDetails();

services.AddMvc().AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = null);

services.AddEndpointsApiExplorer();
services.AddSwaggerGen();

services.AddSingleton<ILoggerFactory, LoggerFactory>();

string? sqlConnString = builder.Configuration.GetConnectionString("DefaultConnection");

services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(sqlConnString));
services.AddDatabaseConcurrency(builder.Configuration);

services.AddScoped<IDapperRepository>(x =>
{
    var dapperDBContext = new DapperDBContext(sqlConnString) { SQLType = SQLType.SQLSERVER };
    return new DapperRepository(dapperDBContext,
        x.GetRequiredService<DbConnectionLimitService>(),
        x.GetRequiredService<DbOperateLimitService>());
});

var serviceList = Assembly.GetExecutingAssembly().ExportedTypes
                .Where(x => x.Namespace?.StartsWith($"ReconstructNodesServer.Argument") == true
                         || x.Namespace?.StartsWith($"ReconstructNodesServer.Services") == true
                         || x.Namespace?.StartsWith($"ReconstructNodesServer.Filter") == true)
                .Where(x => x.IsClass && x.IsPublic && !x.IsAbstract)
                .Where(x => !typeof(SemaphoreSlimService).IsAssignableFrom(x)
                         && x != typeof(DatabaseConcurrencyOptions));
if (serviceList != null) 
{
    foreach (Type type in serviceList) { services.AddScoped(type); }
}

services.AddTransient(typeof(Lazy<>), typeof(Lazier<>));

services.AddSingleton<MailSender>();
services.AddSingleton<AzureStoreService>();
//

//services.AddHostedService<TimedHostedService>();
services.AddHostedService(x => new DatabaseWarmupService(sqlConnString,
    x.GetRequiredService<ILogger<DatabaseWarmupService>>()));

services.AddMemoryCache();

// Secret 中的 Google 桌面與 Web 憑證隨發布部署；User Secrets、環境變數與命令列仍可覆寫。
GoogleAuthOptions googleAuthOptions = builder.Configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();
services.AddSingleton(googleAuthOptions);
services.AddHttpClient("google-oauth", client => client.Timeout = TimeSpan.FromSeconds(15));
services.AddHttpClient("google-jwks", client => client.Timeout = TimeSpan.FromSeconds(15));
services.AddSingleton<IGoogleSigningKeySource>(sp => new GoogleSigningKeySource(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("google-jwks"),
    sp.GetRequiredService<GoogleAuthOptions>()));
services.AddSingleton<IGoogleCodeExchanger>(sp => new GoogleCodeExchanger(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("google-oauth"),
    sp.GetRequiredService<GoogleAuthOptions>()));
services.AddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
services.AddScoped<IGoogleSessionStore, GoogleSessionStore>();
services.AddScoped<GoogleLoginService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseMiddleware<APIRequest>();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();


internal class Lazier<T> : Lazy<T> where T : class
{
    public Lazier(IServiceProvider provider) : base(provider.GetRequiredService<T>) { }
}