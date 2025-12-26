using Application.Resources.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Resources.Queries.GetResources;

public sealed record GetResourcesQuery(
    [Required] Guid UserId) : IQuery<IReadOnlyList<ResourceResponse>>;
