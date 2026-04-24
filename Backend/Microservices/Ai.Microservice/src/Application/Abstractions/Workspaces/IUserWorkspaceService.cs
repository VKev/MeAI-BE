using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Workspaces;

public interface IUserWorkspaceService
{
    Task<Result<IReadOnlyList<UserWorkspaceResult>>> GetWorkspacesAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<Result<UserWorkspaceResult?>> GetWorkspaceAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken cancellationToken);
}

public sealed record UserWorkspaceResult(
    Guid WorkspaceId,
    string Name,
    string? Type,
    string? Description,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
