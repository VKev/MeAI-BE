namespace WebApi.Setups;

public static class CorsSetup
{
    private const string CorsPolicyName = "AllowFrontend";

    public static string AddCorsPolicy(this WebApplicationBuilder builder)
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        var allowedCorsOrigins = (configuredOrigins ?? [])
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowedCorsOrigins.Length == 0) allowedCorsOrigins = ["http://localhost:5173"];

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                policy
                    .WithOrigins(allowedCorsOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return CorsPolicyName;
    }
}
