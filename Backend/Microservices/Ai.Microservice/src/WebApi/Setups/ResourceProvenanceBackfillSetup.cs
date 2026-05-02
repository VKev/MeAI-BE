using Infrastructure.Logic.Services;

namespace WebApi.Setups;

public static class ResourceProvenanceBackfillSetup
{
    public static async Task BackfillResourceProvenanceAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        try
        {
            var backfillService = scope.ServiceProvider.GetRequiredService<ResourceProvenanceBackfillService>();
            await backfillService.BackfillAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to backfill AI resource provenance at startup.");
        }
    }
}
