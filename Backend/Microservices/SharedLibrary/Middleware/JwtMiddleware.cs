using Microsoft.AspNetCore.Http;
using SharedLibrary.Authentication;

namespace SharedLibrary.Middleware;

public class JwtMiddleware(RequestDelegate next, IJwtTokenService jwtTokenService)
{
    public async Task InvokeAsync(HttpContext context)
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

        await next(context);
    }

    private static string? ExtractToken(HttpContext context)
    {
        var cookieNames = new[] { "access_token", "AccessToken", "jwt" };
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
