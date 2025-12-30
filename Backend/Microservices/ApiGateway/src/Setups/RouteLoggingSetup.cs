using System.Text.Json.Nodes;

namespace src.Setups;

internal static class RouteLoggingSetup
{
    internal static void LogRegisteredRoutes(this WebApplication app, JsonArray? routes)
    {
        if (routes is null)
        {
            return;
        }

        var routeTemplates = routes
            .Select(route => route?["UpstreamPathTemplate"]?.GetValue<string>())
            .Where(template => !string.IsNullOrWhiteSpace(template))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(template => template, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        app.Logger.LogInformation(
            "Ocelot routes registered: {Count} -> {Routes}",
            routeTemplates.Length,
            string.Join(", ", routeTemplates));
    }
}
