using System;
using System.Collections.Generic;

namespace Application.Users;

internal static class UserRoleConstants
{
    internal const string Admin = "ADMIN";
    internal const string User = "USER";
    internal const string Banned = "BANNED";

    internal static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        Admin,
        User,
        Banned
    };
}
