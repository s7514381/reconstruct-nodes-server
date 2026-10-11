using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library;
using System.Reflection;
using ReconstructNodesServer;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Filter;
using ReconstructNodesServer.Host;
using ReconstructNodesServer.Repositories;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Utility;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
IServiceCollection services = builder.Services;
services.AddControllers();

services.AddMvc().AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = null);

services.AddEndpointsApiExplorer();
services.AddSwaggerGen();

services.AddSingleton<ILoggerFactory, LoggerFactory>();

string? sqlConnString = builder.Configuration.GetConnectionString("DefaultConnection");

services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(sqlConnString));

services.AddScoped<IDapperRepository>(x =>
{
    var dapperDBContext = new DapperDBContext(sqlConnString) { SQLType = SQLType.SQLSERVER };
    return new DapperRepository(dapperDBContext);
});

var serviceList = Assembly.GetExecutingAssembly().ExportedTypes
                .Where(x => x.Namespace?.StartsWith($"ReconstructNodesServer.Argument") == true
                         || x.Namespace?.StartsWith($"ReconstructNodesServer.Services") == true
                         || x.Namespace?.StartsWith($"ReconstructNodesServer.Filter") == true)
                .Where(x => x.IsClass && x.IsPublic && !x.IsAbstract);
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseMiddleware<APIRequest>();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();


internal class Lazier<T> : Lazy<T> where T : class
{
    public Lazier(IServiceProvider provider) : base(provider.GetRequiredService<T>) { }
}