using Application;
using Infrastructure;
using Infrastructure.Configuration;
using Infrastructure.Configs;
using Scalar.AspNetCore;
using Serilog;
using SharedLibrary.Configs;
using WebApi.OpenApi;
using WebApi.Setups;
using WebApi.Grpc;
using Microsoft.AspNetCore.Server.Kestrel.Core;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var solutionDirectory = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ?? "";
if (!string.IsNullOrWhiteSpace(solutionDirectory))
{
    DotNetEnv.Env.Load(Path.Combine(solutionDirectory, ".env"));
}

var builder = WebApplication.CreateBuilder(args);
var shouldAutoApplyMigrations = builder.ConfigureAutoMigrations();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });
    options.ListenAnyIP(5005, listenOptions =>
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
    options.AddOperationTransformer(AiGenerationOpenApiTransformers.TransformAsync);
    options.AddOperationTransformer(PostsOpenApiTransformers.TransformAsync);
    options.AddOperationTransformer(RecommendationsOpenApiTransformers.TransformAsync);
});
var corsPolicyName = builder.AddCorsPolicy();

builder.ConfigureSerilogLogging();
builder.Services.AddSingleton<EnvironmentConfig>();
builder.Services.Configure<VeoOptions>(
    builder.Configuration.GetSection(VeoOptions.SectionName));
builder.Services.Configure<GenerationStorageEstimates>(
    builder.Configuration.GetSection(GenerationStorageEstimates.SectionName));
builder.Services.Configure<SampleSeedOptions>(
    builder.Configuration.GetSection(SampleSeedOptions.SectionName));
builder.AddDatabase();

builder.Services
    .AddApplication()
    .AddInfrastructure();

var app = builder.Build();

app.ApplyMigrationsIfEnabled(shouldAutoApplyMigrations);
await app.SeedSampleDataAsync();
await app.BackfillResourceProvenanceAsync();
app.MapHealthEndpoints();

app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(corsPolicyName);
app.UseAuthenticationPipeline();

app.MapOpenApi();
app.MapScalarApiReference("docs", opts =>
{
    opts.WithTitle("Ai API");
});

app.MapGrpcService<AiFeedPostGrpcService>();
app.MapControllers();

app.LogStartupInfo(builder.Configuration);

app.Run();

public partial class Program
{
}
