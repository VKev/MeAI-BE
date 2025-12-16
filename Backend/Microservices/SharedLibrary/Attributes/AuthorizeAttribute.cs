using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SharedLibrary.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AuthorizeAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[]? _roles;

    public AuthorizeAttribute(params string[] roles)
    {
        _roles = roles?.Length > 0 ? roles : null;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Skip authorization if action is decorated with [AllowAnonymous]
        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
            return;

        var user = context.HttpContext.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new JsonResult(new { message = "Unauthorized" }) 
            { 
                StatusCode = StatusCodes.Status401Unauthorized 
            };
            return;
        }

        // Check roles if specified
        if (_roles != null && _roles.Length > 0)
        {
            var userRoles = user.Claims
                .Where(x => x.Type == ClaimTypes.Role)
                .Select(x => x.Value)
                .ToList();

            if (!_roles.Any(role => userRoles.Contains(role)))
            {
                context.Result = new JsonResult(new { message = "Forbidden - Insufficient permissions" }) 
                { 
                    StatusCode = StatusCodes.Status403Forbidden 
                };
                return;
            }
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class AllowAnonymousAttribute : Attribute
{
}
