using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SharedLibrary.Authentication;

namespace SharedLibrary.Middleware;

public class JwtMiddleware
{
    private readonly RequestDelegate _next;

    public JwtMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IJwtTokenService jwtTokenService)
    {
        var token = ExtractToken(context);

        if (!string.IsNullOrEmpty(token))
        {
            var principal = jwtTokenService.ValidateToken(token);
            if (principal != null)
            {
                context.User = principal;
            }
        }

        await _next(context);
    }

    private static string? ExtractToken(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/hubs") &&
            context.Request.Query.TryGetValue("access_token", out var queryToken) &&
            !string.IsNullOrWhiteSpace(queryToken))
        {
            return queryToken.ToString().Trim();
        }

        var cookieNames = new[] { "access_token" };
        foreach (var name in cookieNames)
        {
            if (context.Request.Cookies.TryGetValue(name, out var cookieToken) &&
                !string.IsNullOrWhiteSpace(cookieToken))
            {
                return cookieToken.Trim();
            }
        }

        var authorizationHeader = context.Request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrEmpty(authorizationHeader))
            return null;

        if (authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorizationHeader["Bearer ".Length..].Trim();

        return null;
    }
}
