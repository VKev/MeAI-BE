using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Authentication;
using System.ComponentModel.DataAnnotations;

namespace Application.Users.Commands.Refresh;

public sealed record RefreshTokenCommand([Required] string RefreshToken)
    : ICommand<LoginResponse>;
