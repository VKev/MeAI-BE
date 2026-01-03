using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SharedLibrary.Authentication;

namespace SharedLibrary.Middleware;

public class JwtMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IJwtTokenService _jwtTokenService;

    public JwtMiddleware(RequestDelegate next, IJwtTokenService jwtTokenService)
    {
        _next = next;
        _jwtTokenService = jwtTokenService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var token = ExtractToken(context);

        if (!string.IsNullOrEmpty(token))
        {
            var principal = _jwtTokenService.ValidateToken(token);
            if (principal != null)
            {
                context.User = principal;
            }
        }

        await _next(context);
    }

    private static string? ExtractToken(HttpContext context)
    {
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
