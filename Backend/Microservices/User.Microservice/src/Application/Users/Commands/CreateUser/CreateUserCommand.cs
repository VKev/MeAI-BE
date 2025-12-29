using Application.Users.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Users.Commands.CreateUser;

public sealed record CreateUserCommand(
    [Required] string Username,
    [Required, EmailAddress] string Email,
    [Required] string Password,
    string? FullName,
    string? PhoneNumber,
    string? Address,
    DateTime? Birthday,
    Guid? AvatarResourceId,
    decimal? MeAiCoin,
    bool? EmailVerified,
    string? Role) : ICommand<AdminUserResponse>;
