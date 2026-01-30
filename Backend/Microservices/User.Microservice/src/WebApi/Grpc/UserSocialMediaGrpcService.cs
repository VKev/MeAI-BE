using Application.SocialMedias.Queries;
using Grpc.Core;
using MediatR;
using SharedLibrary.Grpc.UserResources;

namespace WebApi.Grpc;

public sealed class UserSocialMediaGrpcService : UserSocialMediaService.UserSocialMediaServiceBase
{
    private readonly IMediator _mediator;

    public UserSocialMediaGrpcService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<GetSocialMediasByIdsResponse> GetSocialMediasByIds(
        GetSocialMediasByIdsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        var socialMediaIds = new List<Guid>();
        foreach (var socialMediaId in request.SocialMediaIds)
        {
            if (!Guid.TryParse(socialMediaId, out var parsedId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid social_media_id."));
            }

            socialMediaIds.Add(parsedId);
        }

        var result = await _mediator.Send(
            new GetSocialMediasByIdsQuery(userId, socialMediaIds),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
        }

        var response = new GetSocialMediasByIdsResponse();
        response.SocialMedias.AddRange(result.Value.Select(item => new SocialMediaRecord
        {
            SocialMediaId = item.Id.ToString(),
            Type = item.Type ?? string.Empty,
            MetadataJson = item.Metadata?.RootElement.GetRawText() ?? string.Empty
        }));

        return response;
    }
}
