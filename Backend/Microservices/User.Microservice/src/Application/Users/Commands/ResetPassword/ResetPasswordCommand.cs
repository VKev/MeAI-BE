using System.ComponentModel.DataAnnotations;
using SharedLibrary.Abstractions.Messaging;

namespace Application.Users.Commands.ResetPassword;

public sealed record ResetPasswordCommand(
    [Required, EmailAddress] string Email,
    [Required] string Code,
    [Required] string NewPassword
) : ICommand;
