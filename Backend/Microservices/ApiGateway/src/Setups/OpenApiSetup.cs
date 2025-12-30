using System.Text.Json;
using Scalar.AspNetCore;

namespace src.Setups;

internal static class OpenApiSetup
{
    internal static void MapGatewayOpenApi(
        this WebApplication app,
        IReadOnlyList<OpenApiDocument> documents,
        string configuredBaseUrl)
    {
        var openApiLookup = documents.ToDictionary(
            entry => entry.Key,
            entry => entry,
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

            var serviceSegment = ServiceNaming.ToServiceSegment(service);
            var patched = TryPatchOpenApiServers(ctx, content, configuredBaseUrl, serviceSegment);
            await ctx.Response.WriteAsync(patched);
        });
    }

    internal static void MapGatewayScalarUi(this WebApplication app, IReadOnlyList<OpenApiDocument> documents)
    {
        app.MapScalarApiReference("scalar", opts =>
        {
            opts.WithTitle("API Gateway - Scalar");
            opts.WithOpenApiRoutePattern("/openapi/{documentName}");

            var isDefault = true;
            foreach (var entry in documents.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                opts.AddDocument(entry.Key, entry.Title, routePattern: null, isDefault: isDefault);
                isDefault = false;
            }
        });
    }

    private static string ResolveRequestScheme(HttpContext context)
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

    private static string TryPatchOpenApiServers(
        HttpContext context,
        string openApiJson,
        string configuredBaseUrl,
        string serviceSegment)
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
}
