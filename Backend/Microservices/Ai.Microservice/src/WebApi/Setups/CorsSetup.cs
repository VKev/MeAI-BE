namespace WebApi.Setups;

public static class CorsSetup
{
    private const string CorsPolicyName = "AllowFrontend";

    public static string AddCorsPolicy(this WebApplicationBuilder builder)
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        var allowedCorsOrigins = (configuredOrigins ?? Array.Empty<string>())
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowedCorsOrigins.Length == 0)
        {
            allowedCorsOrigins = new[] { "http://localhost:5173", "http://localhost:3030" };
        }

        // `*` sentinel → echo the request's Origin header back via SetIsOriginAllowed.
        // Required for AllowCredentials to work across arbitrary origins; browsers reject
        // a literal `Access-Control-Allow-Origin: *` when credentials are allowed.
        var allowAnyOrigin = allowedCorsOrigins.Any(o =>
            string.Equals(o, "*", StringComparison.Ordinal));

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                if (allowAnyOrigin)
                {
                    policy.SetIsOriginAllowed(_ => true);
                }
                else
                {
                    policy.WithOrigins(allowedCorsOrigins);
                }

                policy
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return CorsPolicyName;
    }
}
