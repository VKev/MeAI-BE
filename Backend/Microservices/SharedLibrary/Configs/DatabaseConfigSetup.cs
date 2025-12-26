using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace SharedLibrary.Configs
{
    public class DatabaseConfigSetup(IConfiguration configuration, EnvironmentConfig env) : IConfigureOptions<DatabaseConfig>
    {
        private readonly string _configurationSectionName = "DatabaseConfigurations";

        public void Configure(DatabaseConfig options)
        {
            var sslMode = configuration["DATABASE_SSLMODE"] ?? "Prefer";
            options.ConnectionString = $"Host={env.DatabaseHost};Port={env.DatabasePort};Database={env.DatabaseName};Username={env.DatabaseUser};Password={env.DatabasePassword};SslMode={sslMode}";

            // Allow optional overrides from configuration section (env or other providers)
            var section = configuration.GetSection(_configurationSectionName);
            options.MaxRetryCount = section.GetValue<int?>("MaxRetryCount") ?? options.MaxRetryCount;
            options.CommandTimeout = section.GetValue<int?>("CommandTimeout") ?? options.CommandTimeout;
            options.EnableDetailedErrors = section.GetValue<bool?>("EnableDetailedErrors") ?? options.EnableDetailedErrors;
            options.EnableSensitiveDataLogging = section.GetValue<bool?>("EnableSensitiveDataLogging") ?? options.EnableSensitiveDataLogging;
        }
    }
}
