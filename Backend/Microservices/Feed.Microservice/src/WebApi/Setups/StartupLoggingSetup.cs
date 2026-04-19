namespace WebApi.Setups;

public static class StartupLoggingSetup
{
    public static void LogStartupInfo(this WebApplication app, IConfiguration configuration)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Feed microservice started on {Urls}",
            configuration["ASPNETCORE_URLS"] ?? "http://localhost:5007");
    }
}
