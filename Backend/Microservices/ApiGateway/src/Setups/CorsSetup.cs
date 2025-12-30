namespace src.Setups;

internal static class CorsSetup
{
    private const string CorsPolicyName = "AllowFrontend";

    internal static string AddGatewayCors(this IServiceCollection services, IConfiguration configuration)
    {
        var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        var allowedCorsOrigins = (configuredOrigins ?? [])
            .Select(origin => origin?.Trim())
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin!)
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

        services.AddCors(options =>
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

        return CorsPolicyName;
    }
}
