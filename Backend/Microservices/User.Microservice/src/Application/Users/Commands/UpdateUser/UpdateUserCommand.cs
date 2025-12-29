using Application.Users.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Users.Commands.UpdateUser;

public sealed record UpdateUserCommand(
    [Required] Guid UserId,
    string? Username,
    [EmailAddress] string? Email,
    string? Password,
    string? FullName,
    string? PhoneNumber,
    string? Address,
    DateTime? Birthday,
    Guid? AvatarResourceId,
    decimal? MeAiCoin,
    bool? EmailVerified) : ICommand<AdminUserResponse>;
