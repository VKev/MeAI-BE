using Application.Resources.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Resources.Commands.CreateResource;

public sealed record CreateResourceCommand(
    [Required] Guid UserId,
    [Required] string Link,
    string? Status,
    string? ResourceType,
    string? ContentType) : ICommand<ResourceResponse>;
