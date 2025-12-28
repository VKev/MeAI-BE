using Microsoft.AspNetCore.Http;
using WebApi.Contracts;

namespace WebApi.Setups;

public static class EndpointSetup
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => new HealthResponse("ok"))
            .Produces<HealthResponse>(StatusCodes.Status200OK);
        app.MapGet("/api/health", () => new HealthResponse("ok"))
            .Produces<HealthResponse>(StatusCodes.Status200OK);
    }

    public static void MapDebugEndpoints(this WebApplication app)
    {
        app.MapGet("/debug/headers", (HttpContext context) =>
        {
            var headers = context.Request.Headers
                .ToDictionary(h => h.Key, h => h.Value.ToString());
            return Results.Ok(new DebugHeadersResponse(
                headers,
                context.Request.Scheme,
                context.Request.Host.ToString(),
                context.Request.Path.ToString()));
        })
            .Produces<DebugHeadersResponse>(StatusCodes.Status200OK);
    }

}
