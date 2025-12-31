using Serilog;

namespace WebApi.Setups;

public static class SerilogSetup
{
    public static void ConfigureSerilogLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((hostingContext, loggerConfiguration) =>
            loggerConfiguration
                .ReadFrom.Configuration(hostingContext.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console());
    }
}
