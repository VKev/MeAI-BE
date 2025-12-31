using Microsoft.AspNetCore.Http;

namespace WebApi.Setups;

public static class EndpointSetup
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => new { status = "ok" })
            .Produces(StatusCodes.Status200OK);
        app.MapGet("/api/health", () => new { status = "ok" })
            .Produces(StatusCodes.Status200OK);
    }
}
