using Application.Resources.Models;
using Domain.Entities;

namespace Application.Resources;

internal static class ResourceMapping
{
    internal static ResourceResponse ToResponse(Resource resource, string link) =>
        new(
            resource.Id,
            resource.WorkspaceId,
            link,
            resource.Status,
            resource.ResourceType,
            resource.ContentType,
            resource.SizeBytes,
            resource.OriginKind,
            resource.OriginSourceUrl,
            resource.OriginChatSessionId,
            resource.OriginChatId,
            resource.CreatedAt,
            resource.UpdatedAt);

    internal static ResourceResponse ToResponse(Resource resource) =>
        new(
            resource.Id,
            resource.WorkspaceId,
            resource.Link,
            resource.Status,
            resource.ResourceType,
            resource.ContentType,
            resource.SizeBytes,
            resource.OriginKind,
            resource.OriginSourceUrl,
            resource.OriginChatSessionId,
            resource.OriginChatId,
            resource.CreatedAt,
            resource.UpdatedAt);
}
