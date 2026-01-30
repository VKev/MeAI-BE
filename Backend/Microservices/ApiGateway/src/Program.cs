using SharedLibrary.Authentication;
using src.Setups;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.ConfigureRequestLimits();
var corsPolicyName = builder.Services.AddGatewayCors(configuration);
builder.Services.ConfigureForwardedHeaders();
var gatewayConfig = builder.ConfigureYarpRuntime();

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHttpClient("OpenApiProxy");

var enableDocsUi = configuration.GetValue<bool>("ENABLE_DOCS_UI");

var app = builder.Build();

app.UseForwardedHeaders();
app.UseCloudFrontSchemeAdjustment();
app.UseCors(corsPolicyName);

app.UseGatewayHealthEndpoints();
app.UseGatewayJwtMiddleware();

var uiEnabledNow = enableDocsUi || app.Environment.IsDevelopment();
app.UseRootRedirectToScalar(uiEnabledNow);
app.MapGatewayOpenApi(gatewayConfig.OpenApiDocuments, gatewayConfig.ConfiguredBaseUrl);

if (true)
{
    app.MapGatewayScalarUi(gatewayConfig.OpenApiDocuments);
}

app.MapReverseProxy();
app.LogRegisteredRoutes(gatewayConfig.Routes);
app.Run();
