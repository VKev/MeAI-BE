using Application;
using Infrastructure;
using Infrastructure.Configs;
using Scalar.AspNetCore;
using Serilog;
using WebApi.Setups;

var builder = WebApplication.CreateBuilder(args);
var shouldAutoApplyMigrations = builder.ConfigureAutoMigrations();

builder.Services
    .AddControllers()
    .AddApplicationPart(typeof(WebApi.Controllers.AuthController).Assembly);

builder.Services.AddValidation();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

builder.Services.ConfigureForwardedHeaders();
var corsPolicyName = builder.AddCorsPolicy();
builder.ConfigureSerilogLogging();
builder.AddDatabase();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

builder.Services
    .AddApplication()
    .AddInfrastructure();

var app = builder.Build();
app.ApplyMigrationsIfEnabled(shouldAutoApplyMigrations);
await app.SeedEmailTemplatesAsync();
app.MapHealthEndpoints();
app.MapDebugEndpoints();

// ---------- middleware order matters ----------

// 1) Forwarded headers FIRST
app.UseForwardedHeaders();

// 2) Respect CloudFront viewer scheme (HTTPS at the edge)
app.UseCloudFrontSchemeAdjustment();

// 3) Logging
app.UseSerilogRequestLogging();

// 4) Only redirect to HTTPS if scheme is already corrected by step #2
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

// 5) CORS
app.UseCors(corsPolicyName);

// 7) Auth pipeline
app.UseAuthenticationPipeline();

// Log startup information
app.LogStartupInfo(builder.Configuration);

app.MapOpenApi();
app.MapScalarApiReference("docs", opts =>
{
    opts.WithTitle("User API");
});

app.MapControllers();

app.Run();
