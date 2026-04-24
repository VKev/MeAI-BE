using Application.Posts.Commands;
using Domain.Entities;
using Grpc.Core;
using MediatR;
using SharedLibrary.Grpc.AiFeed;

namespace WebApi.Grpc;

public sealed class AiFeedPostGrpcService : AiFeedPostService.AiFeedPostServiceBase
{
    private readonly IMediator _mediator;

    public AiFeedPostGrpcService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<CreateMirrorPostResponse> CreateMirrorPost(
        CreateMirrorPostRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        var command = new CreatePostCommand(
            UserId: userId,
            WorkspaceId: TryParseGuid(request.WorkspaceId),
            ChatSessionId: null,
            SocialMediaId: TryParseGuid(request.SocialMediaId),
            Title: NormalizeString(request.Title),
            Content: MapContent(request.Content),
            Status: NormalizeString(request.Status));

        var result = await _mediator.Send(command, context.CancellationToken);
        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, result.Error.Description));
        }

        return new CreateMirrorPostResponse
        {
            PostId = result.Value.Id.ToString(),
            CreatedAt = result.Value.CreatedAt?.ToString("O") ?? string.Empty
        };
    }

    private static PostContent? MapContent(AiFeedPostContent? content)
    {
        if (content is null)
        {
            return null;
        }

        var resourceIds = content.ResourceIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();

        return new PostContent
        {
            Content = NormalizeString(content.Content),
            Hashtag = NormalizeString(content.HashtagText),
            ResourceList = resourceIds.Count == 0 ? null : resourceIds,
            PostType = NormalizeString(content.PostType)
        };
    }

    private static Guid? TryParseGuid(string? value)
    {
        return Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : null;
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
