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

    public static void MapDebugEndpoints(this WebApplication app)
    {
        app.MapGet("/debug/headers", (HttpContext context) =>
        {
            var headers = context.Request.Headers
                .ToDictionary(h => h.Key, h => h.Value.ToString());
            return Results.Ok(new
            {
                headers,
                scheme = context.Request.Scheme,
                host = context.Request.Host.ToString(),
                path = context.Request.Path.ToString()
            });
        })
            .Produces(StatusCodes.Status200OK);
    }
}
