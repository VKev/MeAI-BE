using Application;
using Infrastructure;
using Infrastructure.Configs;
using Infrastructure.Configuration;
using Scalar.AspNetCore;
using Serilog;
using SharedLibrary.Configs;
using WebApi.Middleware;
using WebApi.Setups.OpenApi;
using WebApi.Grpc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using WebApi.Setups;

var builder = WebApplication.CreateBuilder(args);
var shouldAutoApplyMigrations = builder.ConfigureAutoMigrations();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5002, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });
    options.ListenAnyIP(5004, listenOptions =>
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
    options.AddSchemaTransformer<FileUploadSchemaTransformer>();
    options.AddOperationTransformer<ResourcesMultipartOperationTransformer>();
});
builder.Services.AddAutoMapper(_ => { }, typeof(WebApi.AssemblyReference).Assembly);

builder.Services.ConfigureForwardedHeaders();
var corsPolicyName = builder.AddCorsPolicy();
builder.ConfigureSerilogLogging();

builder.Services.AddSingleton<EnvironmentConfig>();
builder.Services.Configure<AdminSeedOptions>(
    builder.Configuration.GetSection(AdminSeedOptions.SectionName));
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection("Email"));
builder.Services.Configure<StripeOptions>(
    builder.Configuration.GetSection(StripeOptions.SectionName));
builder.AddDatabase();

builder.Services
    .AddApplication()
    .AddInfrastructure();

var app = builder.Build();
app.ApplyMigrationsIfEnabled(shouldAutoApplyMigrations);
await app.SeedAdminAndSubscriptionsAsync();
app.MapHealthEndpoints();
app.MapDebugEndpoints();

// ---------- middleware order matters ----------

// 1) Forwarded headers FIRST
app.UseForwardedHeaders();

// 2) Respect CloudFront viewer scheme (HTTPS at the edge)
app.UseCloudFrontSchemeAdjustment();

// 3) Logging
app.UseSerilogRequestLogging();

// 4) FluentValidation errors
app.UseMiddleware<ValidationExceptionMiddleware>();

// 5) Only redirect to HTTPS if scheme is already corrected by step #2
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

// 6) CORS
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

app.MapGrpcService<UserResourceGrpcService>();
app.MapGrpcService<UserSocialMediaGrpcService>();
app.MapControllers();

app.Run();
