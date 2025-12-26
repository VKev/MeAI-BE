using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Authentication;
using System.ComponentModel.DataAnnotations;

namespace Application.Users.Commands.Login;

public sealed record LoginWithGoogleCommand([Required] string IdToken)
    : ICommand<LoginResponse>;
