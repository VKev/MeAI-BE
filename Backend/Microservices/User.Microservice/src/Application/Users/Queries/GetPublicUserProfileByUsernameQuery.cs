using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Queries;

public sealed record GetPublicUserProfileByUsernameQuery(string Username) : IRequest<Result<PublicUserProfileResponse>>;

public sealed record GetPublicUserProfilesByIdsQuery(IReadOnlyCollection<Guid> UserIds)
    : IRequest<Result<IReadOnlyList<PublicUserProfileResponse>>>;

public sealed class GetPublicUserProfileByUsernameQueryHandler
    : IRequestHandler<GetPublicUserProfileByUsernameQuery, Result<PublicUserProfileResponse>>
{
    private readonly PublicUserProfileReader _reader;

    public GetPublicUserProfileByUsernameQueryHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _reader = new PublicUserProfileReader(unitOfWork, objectStorageService);
    }

    public async Task<Result<PublicUserProfileResponse>> Handle(
        GetPublicUserProfileByUsernameQuery request,
        CancellationToken cancellationToken)
    {
        var response = await _reader.GetByUsernameAsync(request.Username, cancellationToken);
        return response is null
            ? Result.Failure<PublicUserProfileResponse>(new Error("User.NotFound", "User not found"))
            : Result.Success(response);
    }
}

public sealed class GetPublicUserProfilesByIdsQueryHandler
    : IRequestHandler<GetPublicUserProfilesByIdsQuery, Result<IReadOnlyList<PublicUserProfileResponse>>>
{
    private readonly PublicUserProfileReader _reader;

    public GetPublicUserProfilesByIdsQueryHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _reader = new PublicUserProfileReader(unitOfWork, objectStorageService);
    }

    public async Task<Result<IReadOnlyList<PublicUserProfileResponse>>> Handle(
        GetPublicUserProfilesByIdsQuery request,
        CancellationToken cancellationToken)
    {
        var responses = await _reader.GetByIdsAsync(request.UserIds, cancellationToken);
        return Result.Success<IReadOnlyList<PublicUserProfileResponse>>(responses);
    }
}

internal sealed class PublicUserProfileReader
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Resource> _resourceRepository;
    private readonly IObjectStorageService _objectStorageService;

    public PublicUserProfileReader(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _userRepository = unitOfWork.Repository<User>();
        _resourceRepository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<PublicUserProfileResponse?> GetByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var normalizedUsername = username.Trim().ToLowerInvariant();

        var user = await _userRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => !item.IsDeleted && item.Username != null && item.Username.ToLower() == normalizedUsername,
                cancellationToken);

        if (user is null)
        {
            return null;
        }

        var avatarUrlsByResourceId = await GetAvatarUrlsByResourceIdsAsync(new[] { user.AvatarResourceId }, cancellationToken);
        return Map(user, avatarUrlsByResourceId);
    }

    public async Task<IReadOnlyList<PublicUserProfileResponse>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return Array.Empty<PublicUserProfileResponse>();
        }

        var users = await _userRepository.GetAll()
            .AsNoTracking()
            .Where(item => !item.IsDeleted && ids.Contains(item.Id))
            .ToListAsync(cancellationToken);

        if (users.Count == 0)
        {
            return Array.Empty<PublicUserProfileResponse>();
        }

        var avatarUrlsByResourceId = await GetAvatarUrlsByResourceIdsAsync(
            users.Select(item => item.AvatarResourceId).ToList(),
            cancellationToken);

        var usersById = users.ToDictionary(item => item.Id);

        return ids
            .Where(usersById.ContainsKey)
            .Select(id => Map(usersById[id], avatarUrlsByResourceId))
            .ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string?>> GetAvatarUrlsByResourceIdsAsync(
        IReadOnlyCollection<Guid?> avatarResourceIds,
        CancellationToken cancellationToken)
    {
        var resourceIds = avatarResourceIds
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (resourceIds.Count == 0)
        {
            return new Dictionary<Guid, string?>();
        }

        var resources = await _resourceRepository.GetAll()
            .AsNoTracking()
            .Where(item => resourceIds.Contains(item.Id) && !item.IsDeleted)
            .ToListAsync(cancellationToken);

        return resources.ToDictionary(
            item => item.Id,
            item =>
            {
                var presignedResult = _objectStorageService.GetPresignedUrl(item.Link);
                return presignedResult.IsSuccess ? presignedResult.Value : null;
            });
    }

    private static PublicUserProfileResponse Map(
        User user,
        IReadOnlyDictionary<Guid, string?> avatarUrlsByResourceId)
    {
        string? avatarUrl = null;
        if (user.AvatarResourceId.HasValue &&
            avatarUrlsByResourceId.TryGetValue(user.AvatarResourceId.Value, out var resolvedAvatarUrl))
        {
            avatarUrl = resolvedAvatarUrl;
        }

        return new PublicUserProfileResponse(
            user.Id,
            user.Username ?? string.Empty,
            user.FullName,
            avatarUrl);
    }
}

