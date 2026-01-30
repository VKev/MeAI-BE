namespace src.Setups;

internal static class RouteLoggingSetup
{
    internal static void LogRegisteredRoutes(this WebApplication app, IReadOnlyList<RouteSummary>? routes)
    {
        if (routes is null)
        {
            return;
        }

        var routeTemplates = routes
            .Select(route => route.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        app.Logger.LogInformation(
            "YARP routes registered: {Count} -> {Routes}",
            routeTemplates.Length,
            string.Join(", ", routeTemplates));
    }
}
