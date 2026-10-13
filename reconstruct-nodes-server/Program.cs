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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
IServiceCollection services = builder.Services;
services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("ApiValidation").LogWarning("Invalid API request at {Path}", context.HttpContext.Request.Path);
        return new BadRequestObjectResult(BackendApiResult<object>.Create(400, ApiMessageKeys.RequestInvalid));
    };
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
services.AddSingleton<FirebaseService>();
//

//services.AddHostedService<TimedHostedService>();

services.AddMemoryCache();

// Google 登入：設定以 GoogleAuth 區段讀取，ClientSecret 只允許來自 user-secrets 或環境變數，不寫入 appsettings
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