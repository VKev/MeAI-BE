using Application;
using Infrastructure;
using Scalar.AspNetCore;
using Serilog;
using SharedLibrary.Configs;
using WebApi.Setups;

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
builder.Services.AddOpenApi();

builder.ConfigureSerilogLogging();
builder.Services.AddSingleton<EnvironmentConfig>();
builder.AddDatabase();

builder.Services
    .AddApplication()
    .AddInfrastructure();

var app = builder.Build();

app.ApplyMigrationsIfEnabled(shouldAutoApplyMigrations);
app.MapHealthEndpoints();

app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference("docs", opts =>
{
    opts.WithTitle("Ai API");
});

app.MapControllers();

app.LogStartupInfo(builder.Configuration);

app.Run();
