using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedLibrary.Configs;

namespace WebApi.Setups;

public static class DatabaseSetup
{
    public static void AddDatabase(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureOptions<DatabaseConfigSetup>();
        builder.Services.AddDbContext<MyDbContext>((serviceProvider, options) =>
        {
            var databaseConfig = serviceProvider.GetRequiredService<IOptions<DatabaseConfig>>().Value;
            options.UseNpgsql(databaseConfig.ConnectionString, actions =>
            {
                actions.EnableRetryOnFailure(databaseConfig.MaxRetryCount);
                actions.CommandTimeout(databaseConfig.CommandTimeout);
            });

            if (builder.Environment.IsDevelopment())
            {
                options.EnableDetailedErrors(databaseConfig.EnableDetailedErrors);
                options.EnableSensitiveDataLogging(databaseConfig.EnableSensitiveDataLogging);
            }
        });
    }
}
