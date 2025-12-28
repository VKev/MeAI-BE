using Infrastructure.EmailTemplates;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Setups;

public static class EmailTemplateSetup
{
    public static async Task SeedEmailTemplatesAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MyDbContext>();

        try
        {
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
