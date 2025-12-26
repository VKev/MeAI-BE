using System.Text.Json;
using System.Text.Json.Nodes;
using Scalar.AspNetCore;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using SharedLibrary.Authentication;
using SharedLibrary.Middleware;
using Microsoft.AspNetCore.HttpOverrides;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Configure Kestrel to handle large multipart/form-data uploads
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 104857600; // 100 MB
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104857600; // 100 MB
    options.ValueLengthLimit = 104857600;
    options.MultipartHeadersLengthLimit = 104857600;
});

var runningInContainer = configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER");

string ResolveHost(string? envHost, string containerDefault)
{
    if (!string.IsNullOrWhiteSpace(envHost))
    {
        return envHost;
    }

    return runningInContainer ? containerDefault : "localhost";
}

const string CorsPolicyName = "AllowFrontend";
var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
var allowedCorsOrigins = (configuredOrigins ?? [])
    .Select(origin => origin?.Trim())
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin!) // filter above ensures non-null
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (allowedCorsOrigins.Length == 0)
{
    allowedCorsOrigins = new[] { "http://localhost:5173" };
}

bool allowAnyLoopback = allowedCorsOrigins.Any(origin =>
{
    if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return uri.IsLoopback;
    }

    var lowered = origin.ToLowerInvariant();
    return lowered.Contains("localhost") || lowered.Contains("127.0.0.1") || lowered.Contains("::1");
});

var explicitExternalOrigins = new HashSet<string>(
    allowedCorsOrigins.Where(origin =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && !uri.IsLoopback),
    StringComparer.OrdinalIgnoreCase);

string GetHostForPrefix(string prefix, string serviceSegment, string containerDefault)
{
    return ResolveHost(
        configuration[$"{prefix}_MICROSERVICE_HOST"]
        ?? configuration[$"Services:{serviceSegment}:Host"]
        ?? configuration[$"Services__{serviceSegment}__Host"],
        containerDefault);
}

string? GetPortForPrefix(string prefix, string serviceSegment)
{
    return configuration[$"{prefix}_MICROSERVICE_PORT"]
           ?? configuration[$"Services:{serviceSegment}:Port"]
           ?? configuration[$"Services__{serviceSegment}__Port"];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (explicitExternalOrigins.Contains(origin))
                {
                    return true;
                }

                if (!allowAnyLoopback)
                {
                    return false;
                }

                return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var routes = new JsonArray();

string ToServiceSegment(string prefix)
{
    var lower = prefix.ToLowerInvariant();
    var parts = lower.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
    return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
}

void AddRoute(string prefix, string serviceSegment, string host, int port)
{
    JsonObject BuildRoute(string upstreamTemplate, string downstreamTemplate)
    {
        var route = new JsonObject
        {
            ["UpstreamPathTemplate"] = upstreamTemplate,
            ["UpstreamHttpMethod"] = new JsonArray("Get", "Post", "Put", "Delete", "Options"),
            ["DownstreamScheme"] = "http",
            ["DownstreamHostAndPorts"] = new JsonArray(new JsonObject
            {
                ["Host"] = host,
                ["Port"] = port
            }),
            ["DownstreamPathTemplate"] = downstreamTemplate
        };

        return route;
    }

    // Ensure both root and nested endpoints are forwarded.
    routes.Add(BuildRoute($"/api/{serviceSegment}", "/"));
    routes.Add(BuildRoute($"/api/{serviceSegment}/{{everything}}", "/{everything}"));
}

int ResolvePort(string? envPort, int containerDefault, int localDefault)
{
    if (int.TryParse(envPort, out var parsed))
    {
        return parsed;
    }

    return runningInContainer ? containerDefault : localDefault;
}

var defaultServices = new[]
{
    new
    {
        Prefix = "USER",
        Service = "User",
        ContainerHost = "user-microservice",
        ContainerPort = 5002,
        LocalPort = 5002
    }
};

var addedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (var s in defaultServices)
{
    var host = GetHostForPrefix(s.Prefix, s.Service, s.ContainerHost);
    var port = ResolvePort(GetPortForPrefix(s.Prefix, s.Service), s.ContainerPort, s.LocalPort);

    AddRoute(s.Prefix, s.Service, host, port);
    addedServices.Add(s.Service);
}

var envVars = configuration.AsEnumerable().Where(kv => !string.IsNullOrWhiteSpace(kv.Value));
var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (var entry in envVars)
{
    var key = entry.Key;
    if (string.IsNullOrEmpty(key)) continue;
    if (key.StartsWith("Services:", StringComparison.OrdinalIgnoreCase))
    {
        var parts = key.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            prefixes.Add(parts[1].ToUpperInvariant());
        }
    }
    if (key.EndsWith("_MICROSERVICE_HOST", StringComparison.OrdinalIgnoreCase))
        prefixes.Add(key[..^"_MICROSERVICE_HOST".Length]);
    else if (key.EndsWith("_MICROSERVICE_PORT", StringComparison.OrdinalIgnoreCase))
        prefixes.Add(key[..^"_MICROSERVICE_PORT".Length]);
}

foreach (var prefix in prefixes)
{
    var serviceSegment = ToServiceSegment(prefix);
    if (addedServices.Contains(serviceSegment)) continue;

    var host = GetHostForPrefix(prefix, serviceSegment, $"{prefix.ToLowerInvariant()}-microservice");
    var port = ResolvePort(GetPortForPrefix(prefix, serviceSegment), 80, 80);

    AddRoute(prefix, serviceSegment, host, port);
    addedServices.Add(serviceSegment);
}

var configuredBaseUrl = configuration["BASE_URL"] ?? "http://localhost:2406";

var ocelotConfig = new JsonObject
{
    ["Routes"] = routes,
    ["GlobalConfiguration"] = new JsonObject
    {
        ["BaseUrl"] = configuredBaseUrl
    }
};

var endpointData = new List<(string Key, string Name, string Url)>();
var addedOpenApiServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (var s in defaultServices)
{
    var host = GetHostForPrefix(s.Prefix, s.Service, s.ContainerHost);
    var port = ResolvePort(GetPortForPrefix(s.Prefix, s.Service), s.ContainerPort, s.LocalPort);

    var key = s.Prefix.ToLowerInvariant();
    var name = $"{s.Service} API";
    var url = $"http://{host}:{port}/openapi/v1.json";

    endpointData.Add((key, name, url));
    addedOpenApiServices.Add(s.Service);
}

foreach (var prefix in prefixes)
{
    var serviceSegment = ToServiceSegment(prefix);
    if (addedOpenApiServices.Contains(serviceSegment)) continue;

    var host = GetHostForPrefix(prefix, serviceSegment, $"{prefix.ToLowerInvariant()}-microservice");
    var port = ResolvePort(GetPortForPrefix(prefix, serviceSegment), 80, 80);

    var key = prefix.ToLowerInvariant();
    var name = $"{serviceSegment} API";
    var url = $"http://{host}:{port}/openapi/v1.json";

    endpointData.Add((key, name, url));
    addedOpenApiServices.Add(serviceSegment);
}

var contentRoot = builder.Environment.ContentRootPath;
var ocelotFileName = "ocelot.runtime.json";
var runtimeConfigPath = Path.Combine(contentRoot, ocelotFileName);

File.WriteAllText(runtimeConfigPath, ocelotConfig.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

builder.Configuration
    .SetBasePath(contentRoot)
    .AddJsonFile(ocelotFileName, optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddOcelot(builder.Configuration);
builder.Services.AddHttpClient("OpenApiProxy");

bool enableDocsUi = configuration.GetValue<bool>("ENABLE_DOCS_UI");

var app = builder.Build();

app.UseForwardedHeaders();

// Ensure downstream components respect original viewer scheme behind proxies/CDN
app.Use((ctx, next) =>
{
    var protoHeader = ctx.Request.Headers["CloudFront-Forwarded-Proto"].FirstOrDefault()
                      ?? ctx.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();

    var cfVisitor = ctx.Request.Headers["CF-Visitor"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(protoHeader) && !string.IsNullOrWhiteSpace(cfVisitor))
    {
        // CF-Visitor: {"scheme":"https"}
        var schemeVal = cfVisitor.Split('"').FirstOrDefault(s => s.Equals("https", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(schemeVal))
        {
            protoHeader = schemeVal;
        }
    }

    if (!string.IsNullOrWhiteSpace(protoHeader))
    {
        var normalized = protoHeader.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();

        if (!string.IsNullOrEmpty(normalized))
        {
            ctx.Request.Scheme = normalized;
            ctx.Request.IsHttps = string.Equals(normalized, "https", StringComparison.OrdinalIgnoreCase);
        }
    }

    return next();
});

app.UseCors(CorsPolicyName);

app.Use(async (ctx, next) =>
{
    var p = ctx.Request.Path.Value ?? "";
    if (p.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { status = "ok" }));
        return;
    }
    await next();
});

app.UseWhen(ctx =>
    !ctx.Request.Path.StartsWithSegments("/scalar") &&
    !ctx.Request.Path.StartsWithSegments("/openapi") &&
    !ctx.Request.Path.StartsWithSegments("/health") &&
    !ctx.Request.Path.StartsWithSegments("/api/health"),
    branch => branch.UseMiddleware<JwtMiddleware>());

bool uiEnabledNow = true;
//enableDocsUi || app.Environment.IsDevelopment();

app.Use(async (ctx, next) =>
{
    if (uiEnabledNow && (ctx.Request.Path == "/" || string.IsNullOrEmpty(ctx.Request.Path)))
    {
        ctx.Response.Redirect("/scalar", permanent: false);
        return;
    }
    await next();
});

var openApiLookup = endpointData.ToDictionary(
    entry => entry.Key,
    entry => (Title: entry.Name, Url: entry.Url),
    StringComparer.OrdinalIgnoreCase);

app.MapGet("/openapi", (HttpContext ctx) =>
{
    var service = ctx.Request.Query["service"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(service) && openApiLookup.ContainsKey(service))
    {
        ctx.Response.Redirect($"/openapi/{service}", permanent: false);
        return Task.CompletedTask;
    }

    ctx.Response.ContentType = "application/json";
    var payload = openApiLookup.Select(item => new { key = item.Key, title = item.Value.Title });
    return ctx.Response.WriteAsync(JsonSerializer.Serialize(payload));
});

app.MapGet("/openapi/{service}", async (HttpContext ctx, string service, IHttpClientFactory httpClientFactory) =>
{
    if (!openApiLookup.TryGetValue(service, out var entry))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Unknown service", service }));
        return;
    }

    var client = httpClientFactory.CreateClient("OpenApiProxy");
    using var response = await client.GetAsync(entry.Url, ctx.RequestAborted);
    var content = await response.Content.ReadAsStringAsync(ctx.RequestAborted);

    ctx.Response.StatusCode = (int)response.StatusCode;
    ctx.Response.ContentType = "application/json";

    if (!response.IsSuccessStatusCode)
    {
        await ctx.Response.WriteAsync(content);
        return;
    }

    var serviceSegment = ToServiceSegment(service);
    var patched = TryPatchOpenApiServers(ctx, content, configuredBaseUrl, serviceSegment);
    await ctx.Response.WriteAsync(patched);
});

if (uiEnabledNow)
{
    app.MapScalarApiReference("scalar", opts =>
    {
        opts.WithTitle("API Gateway - Scalar");
        opts.WithOpenApiRoutePattern("/openapi/{documentName}");

        var isDefault = true;
        foreach (var entry in openApiLookup.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            opts.AddDocument(entry.Key, entry.Value.Title, routePattern: null, isDefault: isDefault);
            isDefault = false;
        }
    });
}

app.UseWhen(ctx =>
    ctx.Request.Path.StartsWithSegments("/api") &&
    !ctx.Request.Path.StartsWithSegments("/api/health"),
    branch => branch.UseOcelot().Wait());

if (routes is not null)
{
    var routeTemplates = routes
        .Select(route => route?["UpstreamPathTemplate"]?.GetValue<string>())
        .Where(template => !string.IsNullOrWhiteSpace(template))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(template => template, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    app.Logger.LogInformation("Ocelot routes registered: {Count} -> {Routes}", routeTemplates.Length, string.Join(", ", routeTemplates));
}
app.Run();

static string ResolveRequestScheme(HttpContext context)
{
    var protoHeader = context.Request.Headers["CloudFront-Forwarded-Proto"].FirstOrDefault()
                      ?? context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
    var cfVisitor = context.Request.Headers["CF-Visitor"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(protoHeader) && !string.IsNullOrWhiteSpace(cfVisitor))
    {
        var schemeVal = cfVisitor.Split('"').FirstOrDefault(s => s.Equals("https", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(schemeVal))
        {
            protoHeader = schemeVal;
        }
    }
    var scheme = protoHeader?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()
                    ?.Trim();
    if (string.IsNullOrEmpty(scheme))
    {
        scheme = context.Request.Scheme;
    }

    return scheme;
}

static string TryPatchOpenApiServers(HttpContext context, string openApiJson, string configuredBaseUrl, string serviceSegment)
{
    var openApi = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(openApiJson);
    if (openApi == null)
    {
        return openApiJson;
    }

    var scheme = ResolveRequestScheme(context);
    var servicePath = $"/api/{serviceSegment}";
    var serverUrl = $"{scheme}://{context.Request.Host}{servicePath}";
    if (Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseUri))
    {
        var builder = new UriBuilder(baseUri);
        if (!string.IsNullOrWhiteSpace(scheme))
        {
            builder.Scheme = scheme;
            builder.Port = scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                ? (baseUri.Port == 80 ? 443 : baseUri.Port)
                : (baseUri.Port == 443 ? 80 : baseUri.Port);
        }
        var basePath = builder.Path?.TrimEnd('/') ?? "";
        builder.Path = string.IsNullOrWhiteSpace(basePath) || basePath == "/" ? servicePath : $"{basePath}{servicePath}";
        serverUrl = builder.Uri.ToString().TrimEnd('/');
    }

    var servers = new Newtonsoft.Json.Linq.JArray
    {
        new Newtonsoft.Json.Linq.JObject
        {
            ["url"] = serverUrl
        }
    };

    openApi["servers"] = servers;

    var components = openApi["components"] as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
    var securitySchemes = components["securitySchemes"] as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();

    if (securitySchemes["Bearer"] == null)
    {
        securitySchemes["Bearer"] = new Newtonsoft.Json.Linq.JObject
        {
            ["type"] = "http",
            ["scheme"] = "bearer",
            ["bearerFormat"] = "JWT",
            ["in"] = "header"
        };
    }

    components["securitySchemes"] = securitySchemes;
    openApi["components"] = components;

    if (openApi["security"] is not Newtonsoft.Json.Linq.JArray securityArray || securityArray.Count == 0)
    {
        openApi["security"] = new Newtonsoft.Json.Linq.JArray
        {
            new Newtonsoft.Json.Linq.JObject
            {
                ["Bearer"] = new Newtonsoft.Json.Linq.JArray()
            }
        };
    }

    return openApi.ToString();
}
