using Infrastructure.Logic.Seeding;

namespace WebApi.Setups;

public static class SeedingSetup
{
    public static async Task SeedSampleDataAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        try
        {
            var sampleDataSeeder = scope.ServiceProvider.GetRequiredService<SampleDataSeeder>();
            await sampleDataSeeder.SeedAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to seed AI sample data at startup.");
        }
    }
}
