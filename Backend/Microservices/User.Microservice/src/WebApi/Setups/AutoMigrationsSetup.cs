using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedLibrary.Migrations;

namespace WebApi.Setups;

public static class AutoMigrationsSetup
{
    private const string AutoApplyMigrationsEnvVar = "AutoApply__Migrations";

    public static bool ConfigureAutoMigrations(this WebApplicationBuilder builder)
    {
        var autoApplySetting = builder.Configuration["AutoApply:Migrations"]
                               ?? builder.Configuration[AutoApplyMigrationsEnvVar];
        var shouldAutoApplyMigrations = bool.TryParse(autoApplySetting, out var parsedAutoApply) && parsedAutoApply;

        if (!shouldAutoApplyMigrations) builder.Services.Replace(ServiceDescriptor.Scoped<IMigrator, NoOpMigrator>());

        return shouldAutoApplyMigrations;
    }

    public static void ApplyMigrationsIfEnabled(this WebApplication app, bool shouldAutoApplyMigrations)
    {
        if (shouldAutoApplyMigrations)
        {
            using var scope = app.Services.CreateScope();
            try
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
                var pending = dbContext.Database.GetPendingMigrations().ToList();
                if (pending.Count > 0)
                {
                    app.Logger.LogInformation("Applying {Count} pending EF Core migrations: {Migrations}",
                        pending.Count, string.Join(", ", pending));
                    dbContext.Database.Migrate();
                    app.Logger.LogInformation("EF Core migrations applied successfully at startup.");
                }
                else
                {
                    app.Logger.LogInformation("No pending EF Core migrations detected; skipping apply.");
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex,
                    "Failed to apply EF Core migrations at startup. Continuing without applying migrations.");
            }
        }
        else
        {
            app.Logger.LogInformation("EF Core migrations skipped (set {EnvVar}=true to enable).",
                AutoApplyMigrationsEnvVar);
        }
    }
}