using SharedLibrary.Common.ResponseModel;

namespace Application.Users;

public static class UserAuthenticationRules
{
    public const string BannedRoleName = UserRoleConstants.Banned;

    public static Error AccountDeactivated() =>
        new("Auth.AccountDeactivated", "Account has been deactivated");

    public static Error AccountBanned() =>
        new("Auth.AccountBanned", "Account has been banned");

    public static bool HasBannedRole(IEnumerable<string> roles) =>
        roles.Any(role => string.Equals(role, BannedRoleName, StringComparison.OrdinalIgnoreCase));
}
