using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Authentication;
using System.ComponentModel.DataAnnotations;

namespace Application.Users.Commands.Register;

public sealed record RegisterUserCommand(
    [Required] string Username,
    [Required, EmailAddress] string Email,
    [Required] string Password,
    string? FullName = null,
    string? PhoneNumber = null)
    : ICommand<LoginResponse>;
