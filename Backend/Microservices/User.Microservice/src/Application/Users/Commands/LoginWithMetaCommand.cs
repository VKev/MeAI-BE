using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Data;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Authentication;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record LoginWithMetaCommand(string AccessToken) : IRequest<Result<LoginResponse>>;

public sealed class LoginWithMetaCommandHandler
    : IRequestHandler<LoginWithMetaCommand, Result<LoginResponse>>
{
    private const int RefreshTokenDays = 7;
    private const int AccessTokenMinutesFallback = 60;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;
    private readonly IRepository<RefreshToken> _refreshTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly int _accessTokenMinutes;
    private readonly string? _metaAppId;
    private readonly string? _metaAppSecret;

    public LoginWithMetaCommandHandler(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
        _refreshTokenRepository = unitOfWork.Repository<RefreshToken>();
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _accessTokenMinutes = int.TryParse(configuration["Jwt:ExpirationMinutes"], out var minutes)
            ? minutes
            : AccessTokenMinutesFallback;
        _metaAppId = configuration["Meta:AppId"];
        _metaAppSecret = configuration["Meta:AppSecret"];
    }

    public async Task<Result<LoginResponse>> Handle(LoginWithMetaCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<LoginResponse>(new Error("Auth.InvalidMetaToken", "Missing Meta access token"));
        }

        if (string.IsNullOrWhiteSpace(_metaAppId) || string.IsNullOrWhiteSpace(_metaAppSecret))
        {
            return Result.Failure<LoginResponse>(new Error("Auth.MetaNotConfigured", "Meta OAuth is not configured"));
        }

        using var client = new HttpClient();

        var debugToken = await ValidateTokenAsync(client, request.AccessToken, _metaAppId, _metaAppSecret,
            cancellationToken);
        if (debugToken == null || !debugToken.IsValid || !string.Equals(debugToken.AppId, _metaAppId,
                StringComparison.Ordinal))
        {
            return Result.Failure<LoginResponse>(new Error("Auth.InvalidMetaToken", "Invalid Meta access token"));
        }

        var profile = await FetchProfileAsync(client, request.AccessToken, cancellationToken);
        if (profile == null || string.IsNullOrWhiteSpace(profile.Email))
        {
            return Result.Failure<LoginResponse>(
                new Error("Auth.InvalidMetaToken", "Meta account email missing"));
        }

        var normalizedEmail = NormalizeEmail(profile.Email);
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        List<string> roles;

        if (user == null)
        {
            var username = await GenerateUniqueUsernameAsync(profile, _userRepository, cancellationToken);

            user = new User
            {
                Id = Guid.CreateVersion7(),
                Username = username,
                Email = normalizedEmail,
                PasswordHash = _passwordHasher.HashPassword(Guid.CreateVersion7().ToString("N")),
                FullName = string.IsNullOrWhiteSpace(profile.Name) ? null : profile.Name.Trim(),
                Provider = "meta",
                EmailVerified = true,
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            };

            await _userRepository.AddAsync(user, cancellationToken);

            var role = await GetOrCreateDefaultRole(_roleRepository, cancellationToken);
            var userRole = new UserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                RoleId = role.Id,
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            };

            await _userRoleRepository.AddAsync(userRole, cancellationToken);
            roles = new List<string> { role.Name };
        }
        else
        {
            if (!string.Equals(user.Provider, "meta", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<LoginResponse>(
                    new Error("Auth.ProviderMismatch", "Account is not linked with Meta"));
            }

            roles = await ResolveRolesAsync(user.Id, _roleRepository, _userRoleRepository, cancellationToken);
        }

        var accessToken = _jwtTokenService.GenerateToken(user.Id, user.Email, roles);
        var refreshToken = await GenerateUniqueRefreshTokenAsync(
            _jwtTokenService,
            _refreshTokenRepository,
            cancellationToken);

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = HashToken(refreshToken),
            AccessTokenJti = ExtractAccessTokenJti(accessToken),
            ExpiresAt = DateTimeExtensions.PostgreSqlUtcNow.AddDays(RefreshTokenDays),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _refreshTokenRepository.AddAsync(refreshTokenEntity, cancellationToken);

        var displayName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
        var response = new LoginResponse(
            accessToken,
            refreshToken,
            DateTimeExtensions.PostgreSqlUtcNow.AddMinutes(_accessTokenMinutes),
            user.Id,
            displayName,
            user.Email,
            roles);

        return Result.Success(response);
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string NormalizeUsername(string username) =>
        username.Trim().ToLowerInvariant();

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private static string ExtractAccessTokenJti(string accessToken)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);
        var jti = jwt.Claims.FirstOrDefault(claim =>
            string.Equals(claim.Type, JwtRegisteredClaimNames.Jti, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(claim.Type, "jti", StringComparison.OrdinalIgnoreCase))?.Value;

        if (string.IsNullOrWhiteSpace(jti))
        {
            throw new InvalidOperationException("Access token missing jti claim.");
        }

        return jti;
    }

    private static async Task<string> GenerateUniqueRefreshTokenAsync(
        IJwtTokenService jwtTokenService,
        IRepository<RefreshToken> refreshTokenRepository,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var token = jwtTokenService.GenerateRefreshToken();
            var hash = HashToken(token);
            var exists = await refreshTokenRepository.GetAll()
                .AsNoTracking()
                .AnyAsync(rt => rt.TokenHash == hash, cancellationToken);
            if (!exists)
            {
                return token;
            }
        }

        throw new InvalidOperationException("Failed to generate a unique refresh token.");
    }

    private static async Task<List<string>> ResolveRolesAsync(
        Guid userId,
        IRepository<Role> roleRepository,
        IRepository<UserRole> userRoleRepository,
        CancellationToken cancellationToken)
    {
        var roleIds = await userRoleRepository.GetAll()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0)
        {
            return new List<string> { "USER" };
        }

        var roleNames = await roleRepository.GetAll()
            .AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .Select(role => role.Name)
            .ToListAsync(cancellationToken);

        var roles = roleNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return roles.Count == 0 ? new List<string> { "USER" } : roles;
    }

    private static async Task<Role> GetOrCreateDefaultRole(
        IRepository<Role> roleRepository,
        CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetAll()
            .FirstOrDefaultAsync(r => r.Name == "USER", cancellationToken);

        if (role != null)
        {
            return role;
        }

        role = new Role
        {
            Id = Guid.CreateVersion7(),
            Name = "USER",
            Description = "Standard user",
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await roleRepository.AddAsync(role, cancellationToken);
        return role;
    }

    private static async Task<string> GenerateUniqueUsernameAsync(
        MetaProfile profile,
        IRepository<User> userRepository,
        CancellationToken cancellationToken)
    {
        var baseName = profile.Email?.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                       ?? "meta_user";
        baseName = baseName.Replace(".", "_").Replace("-", "_");
        baseName = NormalizeUsername(baseName);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = attempt == 0 ? baseName : $"{baseName}_{attempt}";
            var exists = await userRepository.GetAll()
                .AsNoTracking()
                .AnyAsync(u => u.Username.ToLower() == candidate, cancellationToken);
            if (!exists)
            {
                return candidate;
            }
        }

        var fallback = $"{baseName}_{Guid.CreateVersion7():N}";
        return fallback.Length > 30 ? fallback[..30] : fallback;
    }

    private static async Task<MetaDebugToken?> ValidateTokenAsync(
        HttpClient client,
        string accessToken,
        string appId,
        string appSecret,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://graph.facebook.com/debug_token?input_token={Uri.EscapeDataString(accessToken)}&access_token={appId}|{appSecret}";
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var debug = JsonSerializer.Deserialize<MetaDebugTokenResponse>(payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return debug?.Data;
    }

    private static async Task<MetaProfile?> FetchProfileAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://graph.facebook.com/me?fields=id,name,email&access_token={Uri.EscapeDataString(accessToken)}";
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<MetaProfile>(payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private sealed record MetaDebugTokenResponse([property: JsonPropertyName("data")] MetaDebugToken? Data);

    private sealed record MetaDebugToken(
        [property: JsonPropertyName("is_valid")] bool IsValid,
        [property: JsonPropertyName("app_id")] string? AppId);

    private sealed record MetaProfile(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("email")] string? Email);
}
