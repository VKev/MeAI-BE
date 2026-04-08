using Application;
using Infrastructure;
using Infrastructure.Configuration;
using Infrastructure.Configs;
using Scalar.AspNetCore;
using Serilog;
using SharedLibrary.Configs;
using WebApi.OpenApi;
using WebApi.Setups;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var solutionDirectory = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ?? "";
if (!string.IsNullOrWhiteSpace(solutionDirectory))
{
    DotNetEnv.Env.Load(Path.Combine(solutionDirectory, ".env"));
}

var builder = WebApplication.CreateBuilder(args);
var shouldAutoApplyMigrations = builder.ConfigureAutoMigrations();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAuthorization();
builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer(GeminiOpenApiTransformers.TransformAsync);
    options.AddOperationTransformer(PostsOpenApiTransformers.TransformAsync);
});
var corsPolicyName = builder.AddCorsPolicy();

builder.ConfigureSerilogLogging();
builder.Services.AddSingleton<EnvironmentConfig>();
builder.Services.Configure<VeoOptions>(
    builder.Configuration.GetSection(VeoOptions.SectionName));
builder.Services.Configure<SampleSeedOptions>(
    builder.Configuration.GetSection(SampleSeedOptions.SectionName));
builder.AddDatabase();

builder.Services
    .AddApplication()
    .AddInfrastructure();

var app = builder.Build();

app.ApplyMigrationsIfEnabled(shouldAutoApplyMigrations);
await app.SeedSampleDataAsync();
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

app.MapControllers();

app.LogStartupInfo(builder.Configuration);

app.Run();
