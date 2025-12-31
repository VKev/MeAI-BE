using Infrastructure.Context;
using Infrastructure.EmailTemplates;
using Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Setups;

public static class SeedingSetup
{
    public static async Task SeedAdminAndSubscriptionsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        try
        {
            var seeder = scope.ServiceProvider.GetRequiredService<AdminUserSeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to seed admin user at startup.");
        }

        try
        {
            var subscriptionSeeder = scope.ServiceProvider.GetRequiredService<SubscriptionSeeder>();
            await subscriptionSeeder.SeedAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to seed subscriptions at startup.");
        }

        try
        {
            var context = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            if (await context.Database.CanConnectAsync())
            {
                var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
                if (pending.Count > 0)
                {
                    app.Logger.LogWarning(
                        "Skipping email template seed because there are {Count} pending migrations.",
                        pending.Count);
                    return;
                }

                await EmailTemplateSeeder.SeedAsync(context, app.Logger, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to seed email templates at startup.");
        }
    }
}
