using Application.Abstractions.Resources;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.SocialMedia;

namespace Infrastructure.Logic.Consumers;

public sealed class SocialMediaUnlinkedConsumer : IConsumer<SocialMediaUnlinked>
{
    private readonly IPostRepository _postRepository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IRecommendPostRepository _recommendPostRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly ILogger<SocialMediaUnlinkedConsumer> _logger;

    public SocialMediaUnlinkedConsumer(
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        IRecommendPostRepository recommendPostRepository,
        IUserResourceService userResourceService,
        ILogger<SocialMediaUnlinkedConsumer> logger)
    {
        _postRepository = postRepository;
        _postPublicationRepository = postPublicationRepository;
        _recommendPostRepository = recommendPostRepository;
        _userResourceService = userResourceService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SocialMediaUnlinked> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        var targetPublications = await _postPublicationRepository.GetBySocialMediaIdForUpdateAsync(
            message.SocialMediaId,
            cancellationToken);

        var publicationPostIds = targetPublications
            .Select(publication => publication.PostId)
            .Distinct()
            .ToList();

        var directPosts = await _postRepository.GetByUserIdAndSocialMediaIdForUpdateAsync(
            message.UserId,
            message.SocialMediaId,
            cancellationToken);

        var publicationPosts = await _postRepository.GetByIdsForUpdateAsync(
            publicationPostIds,
            cancellationToken);

        var postsById = directPosts
            .Concat(publicationPosts)
            .Where(post => post.UserId == message.UserId)
            .GroupBy(post => post.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var candidatePostIds = publicationPostIds
            .Concat(postsById.Keys)
            .Distinct()
            .ToList();

        var allCandidatePublications = await _postPublicationRepository.GetByPostIdsIncludingDeletedForUpdateAsync(
            candidatePostIds,
            cancellationToken);

        var postsWithOtherActivePublication = allCandidatePublications
            .Where(publication =>
                publication.SocialMediaId != message.SocialMediaId &&
                !publication.DeletedAt.HasValue)
            .Select(publication => publication.PostId)
            .ToHashSet();

        var postsToDelete = postsById.Values
            .Where(post => !postsWithOtherActivePublication.Contains(post.Id))
            .ToList();
        var postIdsToDelete = postsToDelete
            .Select(post => post.Id)
            .ToHashSet();

        var resourceIdsToDelete = await GetUnusedResourceIdsForHardDeleteAsync(
            message.UserId,
            postsToDelete,
            postIdsToDelete,
            cancellationToken);

        var publicationsToDelete = targetPublications
            .Where(publication => !postIdsToDelete.Contains(publication.PostId))
            .ToList();

        var recommendPostsToDelete = await _recommendPostRepository.GetByOriginalPostIdsForUpdateAsync(
            postIdsToDelete.ToList(),
            cancellationToken);

        _postPublicationRepository.DeleteRange(publicationsToDelete);
        _recommendPostRepository.RemoveRange(recommendPostsToDelete);
        _postRepository.DeleteRange(postsToDelete);
        await _postRepository.SaveChangesAsync(cancellationToken);

        var deletedResources = 0;
        if (resourceIdsToDelete.Count > 0)
        {
            var deleteResourcesResult = await _userResourceService.DeleteResourcesAsync(
                message.UserId,
                resourceIdsToDelete,
                true,
                cancellationToken);

            if (deleteResourcesResult.IsFailure)
            {
                _logger.LogWarning(
                    "Social media unlink resource cleanup failed. CorrelationId: {CorrelationId}, UserId: {UserId}, SocialMediaId: {SocialMediaId}, ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}",
                    message.CorrelationId,
                    message.UserId,
                    message.SocialMediaId,
                    deleteResourcesResult.Error.Code,
                    deleteResourcesResult.Error.Description);
            }
            else
            {
                deletedResources = deleteResourcesResult.Value;
            }
        }

        _logger.LogInformation(
            "Social media unlink cleanup completed. CorrelationId: {CorrelationId}, UserId: {UserId}, SocialMediaId: {SocialMediaId}, Platform: {Platform}, DeletedPublications: {DeletedPublications}, DeletedPosts: {DeletedPosts}, DeletedResources: {DeletedResources}",
            message.CorrelationId,
            message.UserId,
            message.SocialMediaId,
            message.Platform,
            targetPublications.Count,
            postsToDelete.Count,
            deletedResources);
    }

    private async Task<List<Guid>> GetUnusedResourceIdsForHardDeleteAsync(
        Guid userId,
        IReadOnlyCollection<Post> postsToDelete,
        IReadOnlyCollection<Guid> postIdsToDelete,
        CancellationToken cancellationToken)
    {
        var candidateResourceIds = postsToDelete
            .SelectMany(post => ParseResourceIds(post.Content?.ResourceList))
            .Distinct()
            .ToList();

        if (candidateResourceIds.Count == 0)
        {
            return [];
        }

        var remainingPosts = await _postRepository.GetActiveByUserIdExcludingIdsAsync(
            userId,
            postIdsToDelete.ToList(),
            cancellationToken);

        var resourceIdsStillInUse = remainingPosts
            .SelectMany(post => ParseResourceIds(post.Content?.ResourceList))
            .ToHashSet();

        return candidateResourceIds
            .Where(resourceId => !resourceIdsStillInUse.Contains(resourceId))
            .ToList();
    }

    private static IEnumerable<Guid> ParseResourceIds(IReadOnlyCollection<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            yield break;
        }

        foreach (var value in values)
        {
            if (Guid.TryParse(value, out var resourceId) && resourceId != Guid.Empty)
            {
                yield return resourceId;
            }
        }
    }
}
