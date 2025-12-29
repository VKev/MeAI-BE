using Application.Users.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Users.Commands.SetUserRole;

public sealed record SetUserRoleCommand(
    [Required] Guid UserId,
    [Required] string Role) : ICommand<AdminUserResponse>;
