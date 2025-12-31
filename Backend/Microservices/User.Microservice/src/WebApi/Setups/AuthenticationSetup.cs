using SharedLibrary.Middleware;

namespace WebApi.Setups;

public static class AuthenticationSetup
{
    public static void UseAuthenticationPipeline(this WebApplication app)
    {
        app.UseMiddleware<JwtMiddleware>();
        app.UseAuthorization();
    }
}
