using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Authentication;
using System.ComponentModel.DataAnnotations;

namespace Application.Users.Commands.Login;

public sealed record LoginWithPasswordCommand(
    [Required, EmailAddress] string Email,
    [Required] string Password)
    : ICommand<LoginResponse>;
