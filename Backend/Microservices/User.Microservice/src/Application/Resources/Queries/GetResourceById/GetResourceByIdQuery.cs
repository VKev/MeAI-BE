using Application.Resources.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Resources.Queries.GetResourceById;

public sealed record GetResourceByIdQuery(
    [Required] Guid ResourceId,
    [Required] Guid UserId) : IQuery<ResourceResponse>;
