using Infrastructure.Logic.Seeding;

namespace WebApi.Setups;

public static class SeedingSetup
{
    public static async Task SeedFeedDemoDataAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        try
        {
            var seeder = scope.ServiceProvider.GetRequiredService<FeedDemoDataSeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to seed feed demo data at startup.");
        }
    }
}
