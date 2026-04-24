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

    public async Task<Result<IReadOnlyList<UserSocialMediaSummaryResult>>> GetSocialMediasByUserAsync(
        Guid userId,
        string? platform,
        CancellationToken cancellationToken)
    {
        var request = new GetSocialMediasByUserRequest
        {
            UserId = userId.ToString(),
            Platform = platform ?? string.Empty
        };

        try
        {
            var response = await _client.GetSocialMediasByUserAsync(request, cancellationToken: cancellationToken);
            return Result.Success<IReadOnlyList<UserSocialMediaSummaryResult>>(
                response.SocialMedias.Select(MapSummary).ToList());
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyList<UserSocialMediaSummaryResult>>(
                new Error("UserSocialMedia.GrpcError", ex.Status.Detail));
        }
    }

    public async Task<Result<IReadOnlyList<UserSocialMediaSummaryResult>>> GetWorkspaceSocialMediasAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var request = new GetWorkspaceSocialMediasRequest
        {
            UserId = userId.ToString(),
            WorkspaceId = workspaceId.ToString()
        };

        try
        {
            var response = await _client.GetWorkspaceSocialMediasAsync(request, cancellationToken: cancellationToken);
            return Result.Success<IReadOnlyList<UserSocialMediaSummaryResult>>(
                response.SocialMedias.Select(MapSummary).ToList());
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyList<UserSocialMediaSummaryResult>>(
                new Error("UserSocialMedia.GrpcError", ex.Status.Detail));
        }
    }

    private static UserSocialMediaSummaryResult MapSummary(SocialMediaSummaryRecord item)
    {
        return new UserSocialMediaSummaryResult(
            Guid.Parse(item.SocialMediaId),
            item.Type,
            string.IsNullOrWhiteSpace(item.Username) ? null : item.Username,
            string.IsNullOrWhiteSpace(item.DisplayName) ? null : item.DisplayName,
            string.IsNullOrWhiteSpace(item.ProfilePictureUrl) ? null : item.ProfilePictureUrl,
            string.IsNullOrWhiteSpace(item.PageId) ? null : item.PageId,
            string.IsNullOrWhiteSpace(item.PageName) ? null : item.PageName,
            TryParseDateTime(item.CreatedAt),
            TryParseDateTime(item.UpdatedAt));
    }

    private static DateTime? TryParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(
            value,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }
}
