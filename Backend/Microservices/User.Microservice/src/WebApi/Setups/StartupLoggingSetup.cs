namespace WebApi.Setups;

public static class StartupLoggingSetup
{
    public static void LogStartupInfo(this WebApplication app, IConfiguration configuration)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("User microservice started on port {Port}",
            configuration["ASPNETCORE_URLS"] ?? "5002");
    }
}