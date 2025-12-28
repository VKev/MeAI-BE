using System.ComponentModel.DataAnnotations;
using SharedLibrary.Abstractions.Messaging;

namespace Application.Users.Commands.VerifyEmail;

public sealed record VerifyEmailCommand(
    [Required, EmailAddress] string Email,
    [Required, StringLength(6, MinimumLength = 6)] string Code
) : ICommand;
