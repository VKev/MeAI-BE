using System.ComponentModel.DataAnnotations;
using SharedLibrary.Abstractions.Messaging;

namespace Application.Users.Commands.ForgotPassword;

public sealed record ForgotPasswordCommand(
    [Required, EmailAddress] string Email
) : ICommand;
