using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record AddPostBuilderResourcesCommand(
    Guid PostBuilderId,
    Guid UserId,
    IReadOnlyList<Guid> ResourceIds) : IRequest<Result<PostBuilderResourcesResponse>>;

public sealed record PostBuilderResourcesResponse(
    Guid PostBuilderId,
    IReadOnlyList<Guid> ResourceIds);

public sealed class AddPostBuilderResourcesCommandHandler
    : IRequestHandler<AddPostBuilderResourcesCommand, Result<PostBuilderResourcesResponse>>
{
    private readonly IPostBuilderRepository _postBuilderRepository;

    public AddPostBuilderResourcesCommandHandler(IPostBuilderRepository postBuilderRepository)
    {
        _postBuilderRepository = postBuilderRepository;
    }

    public async Task<Result<PostBuilderResourcesResponse>> Handle(
        AddPostBuilderResourcesCommand request,
        CancellationToken cancellationToken)
    {
        var postBuilder = await _postBuilderRepository.GetByIdForUpdateAsync(request.PostBuilderId, cancellationToken);
        if (postBuilder is null || postBuilder.DeletedAt.HasValue)
        {
            return Result.Failure<PostBuilderResourcesResponse>(PostBuilderErrors.NotFound);
        }

        if (postBuilder.UserId != request.UserId)
        {
            return Result.Failure<PostBuilderResourcesResponse>(PostBuilderErrors.Unauthorized);
        }

        // Merge the incoming IDs with whatever the builder already has. Dedupe + drop Guid.Empty
        // so repeated imports of the same media don't bloat the stored JSON.
        var existing = GeminiDraftPostHelper.ParseResourceIds(postBuilder.ResourceIds).ToList();
        var merged = existing
            .Concat(request.ResourceIds.Where(id => id != Guid.Empty))
            .Distinct()
            .ToList();

        postBuilder.ResourceIds = GeminiDraftPostHelper.SerializeResourceIds(merged);
        postBuilder.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        await _postBuilderRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new PostBuilderResourcesResponse(postBuilder.Id, merged));
    }
}
