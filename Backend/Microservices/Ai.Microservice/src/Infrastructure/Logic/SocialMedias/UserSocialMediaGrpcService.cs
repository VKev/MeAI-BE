using Application.Abstractions.SocialMedias;
using Grpc.Core;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Grpc.UserResources;

namespace Infrastructure.Logic.SocialMedias;

public sealed class UserSocialMediaGrpcService : IUserSocialMediaService
{
    private readonly UserSocialMediaService.UserSocialMediaServiceClient _client;

    public UserSocialMediaGrpcService(UserSocialMediaService.UserSocialMediaServiceClient client)
    {
        _client = client;
    }

    public async Task<Result<IReadOnlyList<UserSocialMediaResult>>> GetSocialMediasAsync(
        Guid userId,
        IReadOnlyList<Guid> socialMediaIds,
        CancellationToken cancellationToken)
    {
        if (socialMediaIds.Count == 0)
        {
            return Result.Failure<IReadOnlyList<UserSocialMediaResult>>(
                new Error("UserSocialMedia.Missing", "At least one social media id is required."));
        }

        var request = new GetSocialMediasByIdsRequest
        {
            UserId = userId.ToString()
        };

        request.SocialMediaIds.AddRange(socialMediaIds.Select(id => id.ToString()));

        try
        {
            var response = await _client.GetSocialMediasByIdsAsync(request, cancellationToken: cancellationToken);
            var result = response.SocialMedias.Select(item => new UserSocialMediaResult(
                Guid.Parse(item.SocialMediaId),
                item.Type,
                string.IsNullOrWhiteSpace(item.MetadataJson) ? null : item.MetadataJson)).ToList();

            return Result.Success<IReadOnlyList<UserSocialMediaResult>>(result);
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyList<UserSocialMediaResult>>(
                new Error("UserSocialMedia.GrpcError", ex.Status.Detail));
        }
    }
}
