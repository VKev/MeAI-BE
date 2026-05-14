using Application.Abstractions.Ai;
using Grpc.Core;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Grpc.AiFeed;

namespace Infrastructure.Logic.Ai;

public sealed class AiFeedPostGrpcService : IAiFeedPostService
{
    private readonly AiFeedPostService.AiFeedPostServiceClient _client;

    public AiFeedPostGrpcService(AiFeedPostService.AiFeedPostServiceClient client)
    {
        _client = client;
    }

    public async Task<Result<AiFeedMirrorPostResult>> CreateMirrorPostAsync(
        CreateAiMirrorPostRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.CreateMirrorPostAsync(new CreateMirrorPostRequest
            {
                UserId = request.UserId.ToString(),
                WorkspaceId = request.WorkspaceId?.ToString() ?? string.Empty,
                SocialMediaId = request.SocialMediaId?.ToString() ?? string.Empty,
                Title = request.Title ?? string.Empty,
                Status = request.Status ?? string.Empty,
                Content = new AiFeedPostContent
                {
                    Content = request.Content ?? string.Empty,
                    HashtagText = request.HashtagText ?? string.Empty,
                    PostType = request.PostType ?? string.Empty,
                    ResourceIds = { request.ResourceIds.Select(id => id.ToString()) }
                }
            }, cancellationToken: cancellationToken);

            var postId = Guid.TryParse(response.PostId, out var parsedPostId)
                ? parsedPostId
                : Guid.Empty;
            var createdAt = DateTime.TryParse(response.CreatedAt, out var parsedCreatedAt)
                ? (DateTime?)parsedCreatedAt
                : null;

            return Result.Success(new AiFeedMirrorPostResult(postId, createdAt));
        }
        catch (RpcException ex)
        {
            return Result.Failure<AiFeedMirrorPostResult>(
                new Error("AiFeed.GrpcError", ex.Status.Detail));
        }
    }

    public async Task<Result<bool>> DeleteMirrorPostAsync(
        DeleteAiMirrorPostRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.DeleteMirrorPostAsync(new DeleteMirrorPostRequest
            {
                UserId = request.UserId.ToString(),
                PostId = request.PostId.ToString()
            }, cancellationToken: cancellationToken);

            return Result.Success(response.Deleted);
        }
        catch (RpcException ex)
        {
            return Result.Failure<bool>(
                new Error("AiFeed.GrpcError", ex.Status.Detail));
        }
    }
}
