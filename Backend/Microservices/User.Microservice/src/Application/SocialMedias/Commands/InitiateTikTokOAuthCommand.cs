using Application.Abstractions.Data;
using Application.Abstractions.TikTok;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record InitiateTikTokOAuthCommand(
    Guid UserId,
    string? Scopes) : IRequest<Result<TikTokOAuthInitiationResponse>>;

public sealed record TikTokOAuthInitiationResponse(string AuthorizationUrl, string State);

public sealed class InitiateTikTokOAuthCommandHandler
    : IRequestHandler<InitiateTikTokOAuthCommand, Result<TikTokOAuthInitiationResponse>>
{
    private readonly ITikTokOAuthService _tikTokOAuthService;
    private readonly IMemoryCache _memoryCache;
    private readonly IRepository<User> _userRepository;

    public InitiateTikTokOAuthCommandHandler(
        ITikTokOAuthService tikTokOAuthService,
        IMemoryCache memoryCache,
        IUnitOfWork unitOfWork)
    {
        _tikTokOAuthService = tikTokOAuthService;
        _memoryCache = memoryCache;
        _userRepository = unitOfWork.Repository<User>();
    }

    public async Task<Result<TikTokOAuthInitiationResponse>> Handle(
        InitiateTikTokOAuthCommand request,
        CancellationToken cancellationToken)
    {
        var userExists = await _userRepository.GetAll()
            .AsNoTracking()
            .AnyAsync(user => user.Id == request.UserId && !user.IsDeleted, cancellationToken);

        if (!userExists)
        {
            return Result.Failure<TikTokOAuthInitiationResponse>(
                new Error("User.NotFound", "User not found"));
        }

        var scopes = string.IsNullOrWhiteSpace(request.Scopes)
            ? "user.info.basic,video.publish,video.upload,user.info.profile,user.info.stats"
            : request.Scopes;

        var (authorizationUrl, state, codeVerifier) = _tikTokOAuthService.GenerateAuthorizationUrl(request.UserId, scopes);

        // Store code_verifier in cache with state as key for 10 minutes
        _memoryCache.Set($"tiktok_code_verifier_{state}", codeVerifier, TimeSpan.FromMinutes(10));

        var response = new TikTokOAuthInitiationResponse(authorizationUrl, state);
        return Result.Success(response);
    }
}
