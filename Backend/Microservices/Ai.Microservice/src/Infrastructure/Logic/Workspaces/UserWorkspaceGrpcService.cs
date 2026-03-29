using Application.Abstractions.Workspaces;
using Grpc.Core;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Grpc.UserResources;

namespace Infrastructure.Logic.Workspaces;

public sealed class UserWorkspaceGrpcService : IUserWorkspaceService
{
    private readonly UserWorkspaceService.UserWorkspaceServiceClient _client;

    public UserWorkspaceGrpcService(UserWorkspaceService.UserWorkspaceServiceClient client)
    {
        _client = client;
    }

    public async Task<Result<UserWorkspaceResult?>> GetWorkspaceAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var request = new GetWorkspaceByIdRequest
        {
            UserId = userId.ToString(),
            WorkspaceId = workspaceId.ToString()
        };

        try
        {
            var response = await _client.GetWorkspaceByIdAsync(request, cancellationToken: cancellationToken);
            var createdAt = TryParseDateTime(response.CreatedAt);
            var updatedAt = TryParseDateTime(response.UpdatedAt);

            return Result.Success<UserWorkspaceResult?>(new UserWorkspaceResult(
                Guid.Parse(response.WorkspaceId),
                response.Name,
                string.IsNullOrWhiteSpace(response.Type) ? null : response.Type,
                string.IsNullOrWhiteSpace(response.Description) ? null : response.Description,
                createdAt,
                updatedAt));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return Result.Success<UserWorkspaceResult?>(null);
        }
        catch (RpcException ex)
        {
            return Result.Failure<UserWorkspaceResult?>(
                new Error("UserWorkspace.GrpcError", ex.Status.Detail));
        }
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
