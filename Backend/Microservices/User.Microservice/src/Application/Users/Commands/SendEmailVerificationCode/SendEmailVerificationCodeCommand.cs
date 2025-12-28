using System.ComponentModel.DataAnnotations;
using SharedLibrary.Abstractions.Messaging;

namespace Application.Users.Commands.SendEmailVerificationCode;

public sealed record SendEmailVerificationCodeCommand(
    [Required, EmailAddress] string Email
) : ICommand;
