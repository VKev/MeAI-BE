using Application.Abstractions.Resources;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Publishing;

public interface ISocialPublishMediaNormalizer
{
    Task<Result<SocialPublishMediaNormalizationResult>> NormalizeAsync(
        Guid userId,
        Guid? workspaceId,
        string platform,
        string? postType,
        IReadOnlyList<UserResourcePresignResult> resources,
        CancellationToken cancellationToken);
}

public sealed record SocialPublishMediaNormalizationResult(
    IReadOnlyList<UserResourcePresignResult> Resources,
    IReadOnlyList<Guid> TemporaryResourceIds);
