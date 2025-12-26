using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Resources.Commands.DeleteResource;

public sealed record DeleteResourceCommand(
    [Required] Guid ResourceId,
    [Required] Guid UserId) : ICommand;
