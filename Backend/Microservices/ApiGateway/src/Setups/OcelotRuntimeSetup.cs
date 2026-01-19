using System.Text.Json;
using System.Text.Json.Nodes;

namespace src.Setups;

internal sealed record OpenApiDocument(string Key, string Title, string Url);

internal sealed record GatewayRuntimeConfig(
    JsonArray Routes,
    IReadOnlyList<OpenApiDocument> OpenApiDocuments,
    string ConfiguredBaseUrl);

internal static class OcelotRuntimeSetup
{
    internal static GatewayRuntimeConfig ConfigureOcelotRuntime(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var runningInContainer = configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER");
        var routes = new JsonArray();

        string ResolveHost(string? envHost, string containerDefault)
        {
            if (!string.IsNullOrWhiteSpace(envHost))
            {
                return envHost;
            }

            return runningInContainer ? containerDefault : "localhost";
        }

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

        void AddRoute(string serviceSegment, string host, int port)
        {
            JsonObject BuildRoute(string upstreamTemplate, string downstreamTemplate)
            {
                var route = new JsonObject
                {
                    ["UpstreamPathTemplate"] = upstreamTemplate,
                    ["UpstreamHttpMethod"] = new JsonArray("Get", "Post", "Put", "Patch", "Delete", "Options"),
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

            routes.Add(BuildRoute($"/api/{serviceSegment}", $"/api/{serviceSegment}"));
            routes.Add(BuildRoute($"/api/{serviceSegment}/{{everything}}", $"/api/{serviceSegment}/{{everything}}"));
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
            new ServiceDefinition(
                Prefix: "USER",
                Service: "User",
                ContainerHost: "user-microservice",
                ContainerPort: 5002,
                LocalPort: 5002),
            new ServiceDefinition(
                Prefix: "AI",
                Service: "Ai",
                ContainerHost: "ai-microservice",
                ContainerPort: 5001,
                LocalPort: 5001)
        };

        var addedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in defaultServices)
        {
            var host = GetHostForPrefix(s.Prefix, s.Service, s.ContainerHost);
            var port = ResolvePort(GetPortForPrefix(s.Prefix, s.Service), s.ContainerPort, s.LocalPort);

            AddRoute(s.Service, host, port);
            addedServices.Add(s.Service);
        }

        var envVars = configuration.AsEnumerable().Where(kv => !string.IsNullOrWhiteSpace(kv.Value));
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in envVars)
        {
            var key = entry.Key;
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (key.StartsWith("Services:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = key.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    prefixes.Add(parts[1].ToUpperInvariant());
                }
            }

            if (key.EndsWith("_MICROSERVICE_HOST", StringComparison.OrdinalIgnoreCase))
            {
                prefixes.Add(key[..^"_MICROSERVICE_HOST".Length]);
            }
            else if (key.EndsWith("_MICROSERVICE_PORT", StringComparison.OrdinalIgnoreCase))
            {
                prefixes.Add(key[..^"_MICROSERVICE_PORT".Length]);
            }
        }

        foreach (var prefix in prefixes)
        {
            var serviceSegment = ServiceNaming.ToServiceSegment(prefix);
            if (addedServices.Contains(serviceSegment))
            {
                continue;
            }

            var host = GetHostForPrefix(prefix, serviceSegment, $"{prefix.ToLowerInvariant()}-microservice");
            var port = ResolvePort(GetPortForPrefix(prefix, serviceSegment), 80, 80);

            AddRoute(serviceSegment, host, port);
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

        var contentRoot = builder.Environment.ContentRootPath;
        var ocelotFileName = "ocelot.runtime.json";
        var runtimeConfigPath = Path.Combine(contentRoot, ocelotFileName);

        File.WriteAllText(runtimeConfigPath, ocelotConfig.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        builder.Configuration
            .SetBasePath(contentRoot)
            .AddJsonFile(ocelotFileName, optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();

        var endpointData = new List<OpenApiDocument>();
        var addedOpenApiServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in defaultServices)
        {
            var host = GetHostForPrefix(s.Prefix, s.Service, s.ContainerHost);
            var port = ResolvePort(GetPortForPrefix(s.Prefix, s.Service), s.ContainerPort, s.LocalPort);

            var key = s.Prefix.ToLowerInvariant();
            var title = $"{s.Service} API";
            var url = $"http://{host}:{port}/openapi/v1.json";
            endpointData.Add(new OpenApiDocument(key, title, url));
            addedOpenApiServices.Add(s.Service);
        }

        foreach (var prefix in prefixes)
        {
            var serviceSegment = ServiceNaming.ToServiceSegment(prefix);
            if (addedOpenApiServices.Contains(serviceSegment))
            {
                continue;
            }

            var host = GetHostForPrefix(prefix, serviceSegment, $"{prefix.ToLowerInvariant()}-microservice");
            var port = ResolvePort(GetPortForPrefix(prefix, serviceSegment), 80, 80);

            var key = prefix.ToLowerInvariant();
            var title = $"{serviceSegment} API";
            var url = $"http://{host}:{port}/openapi/v1.json";
            endpointData.Add(new OpenApiDocument(key, title, url));
            addedOpenApiServices.Add(serviceSegment);
        }

        return new GatewayRuntimeConfig(routes, endpointData, configuredBaseUrl);
    }

    private sealed record ServiceDefinition(
        string Prefix,
        string Service,
        string ContainerHost,
        int ContainerPort,
        int LocalPort);
}
