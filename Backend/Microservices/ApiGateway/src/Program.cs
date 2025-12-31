using Ocelot.DependencyInjection;
using SharedLibrary.Authentication;
using src.Setups;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.ConfigureRequestLimits();
var corsPolicyName = builder.Services.AddGatewayCors(configuration);
builder.Services.ConfigureForwardedHeaders();
var gatewayConfig = builder.ConfigureOcelotRuntime();

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddOcelot(builder.Configuration);
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

if (uiEnabledNow)
{
    app.MapGatewayScalarUi(gatewayConfig.OpenApiDocuments);
}

app.UseOcelotForApi();
app.LogRegisteredRoutes(gatewayConfig.Routes);
app.Run();
