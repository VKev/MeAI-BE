using Application;
using Infrastructure;
using Infrastructure.Configuration;
using Infrastructure.Context;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Scalar.AspNetCore;
using Serilog;
using SharedLibrary.Configs;
using WebApi.Grpc;
using WebApi.Middleware;
using WebApi.Setups;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);
var shouldAutoApplyMigrations = builder.ConfigureAutoMigrations();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5007, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });
    options.ListenAnyIP(5008, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAuthorization();
builder.Services.AddGrpc();
builder.Services.AddOpenApi(options =>
{

});
builder.Services.AddAutoMapper(_ => { }, typeof(WebApi.AssemblyReference).Assembly);

var corsPolicyName = builder.AddCorsPolicy();
builder.ConfigureSerilogLogging();

builder.Services.AddSingleton<EnvironmentConfig>();
builder.Services.Configure<FeedSeedOptions>(
    builder.Configuration.GetSection(FeedSeedOptions.SectionName));
builder.AddDatabase();

builder.Services
    .AddApplication()
    .AddInfrastructure();

var app = builder.Build();
app.ApplyMigrationsIfEnabled(shouldAutoApplyMigrations);
await app.SeedFeedDemoDataAsync();
app.MapHealthEndpoints();
app.MapDebugEndpoints();

// ---------- middleware order matters ----------

// 2) Logging
app.UseSerilogRequestLogging();

// 3) FluentValidation errors
app.UseMiddleware<ValidationExceptionMiddleware>();

// 4) Only redirect to HTTPS if scheme is already corrected by step #2
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

// 5) CORS
app.UseCors(corsPolicyName);

// 6) Auth pipeline
app.UseAuthenticationPipeline();

// Log startup information
app.LogStartupInfo(builder.Configuration);

app.MapOpenApi();
app.MapScalarApiReference("docs", opts =>
{
    opts.WithTitle("Feed API");
});

app.MapGrpcService<FeedAnalyticsGrpcService>();
app.MapGrpcService<FeedPostPublishGrpcService>();
app.MapControllers();

app.Run();

public partial class Program
{
}
