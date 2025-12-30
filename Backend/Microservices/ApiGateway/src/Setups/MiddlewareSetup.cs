using System.Text.Json;
using Ocelot.Middleware;
using SharedLibrary.Middleware;

namespace src.Setups;

internal static class MiddlewareSetup
{
    internal static IApplicationBuilder UseGatewayHealthEndpoints(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            var p = ctx.Request.Path.Value ?? "";
            if (p.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { status = "ok" }));
                return;
            }
            await next();
        });
    }

    internal static IApplicationBuilder UseGatewayJwtMiddleware(this IApplicationBuilder app)
    {
        return app.UseWhen(ctx =>
                !ctx.Request.Path.StartsWithSegments("/scalar") &&
                !ctx.Request.Path.StartsWithSegments("/openapi") &&
                !ctx.Request.Path.StartsWithSegments("/health") &&
                !ctx.Request.Path.StartsWithSegments("/api/health"),
            branch => branch.UseMiddleware<JwtMiddleware>());
    }

    internal static IApplicationBuilder UseRootRedirectToScalar(this IApplicationBuilder app, bool uiEnabled)
    {
        if (!uiEnabled)
        {
            return app;
        }

        return app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path == "/" || string.IsNullOrEmpty(ctx.Request.Path))
            {
                ctx.Response.Redirect("/scalar", permanent: false);
                return;
            }
            await next();
        });
    }

    internal static IApplicationBuilder UseOcelotForApi(this IApplicationBuilder app)
    {
        return app.UseWhen(ctx =>
                ctx.Request.Path.StartsWithSegments("/api") &&
                !ctx.Request.Path.StartsWithSegments("/api/health"),
            branch => branch.UseOcelot().Wait());
    }
}
