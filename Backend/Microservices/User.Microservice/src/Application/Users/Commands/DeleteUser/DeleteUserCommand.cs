using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Users.Commands.DeleteUser;

public sealed record DeleteUserCommand([Required] Guid UserId) : ICommand;
