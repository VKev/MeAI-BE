using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Users.Helpers;
using Application.Users.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Authentication;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record RegisterUserCommand(
    string Username,
    string Email,
    string Password,
    string? FullName,
    string? PhoneNumber) : IRequest<Result<LoginResponse>>;

public sealed class RegisterUserCommandHandler
    : IRequestHandler<RegisterUserCommand, Result<LoginResponse>>
{
    private const int RefreshTokenDays = 7;
    private const int AccessTokenMinutesFallback = 60;
    private const int VerificationCodeMinutes = 10;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;
    private readonly IRepository<RefreshToken> _refreshTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IEmailRepository _emailRepository;
    private readonly IVerificationCodeStore _verificationCodeStore;
    private readonly int _accessTokenMinutes;
    private readonly string _appName;

    public RegisterUserCommandHandler(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration,
        IEmailRepository emailRepository,
        IVerificationCodeStore verificationCodeStore)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
        _refreshTokenRepository = unitOfWork.Repository<RefreshToken>();
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _emailRepository = emailRepository;
        _verificationCodeStore = verificationCodeStore;
        _accessTokenMinutes = int.TryParse(configuration["Jwt:ExpirationMinutes"], out var minutes)
            ? minutes
            : AccessTokenMinutesFallback;
        _appName = ResolveAppName(configuration);
    }

    public async Task<Result<LoginResponse>> Handle(RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var normalizedUsername = NormalizeUsername(request.Username);

        var existingUsers = await _userRepository.GetAll()
            .AsNoTracking()
            .Where(user => user.Email.ToLower() == normalizedEmail || user.Username.ToLower() == normalizedUsername)
            .ToListAsync(cancellationToken);

        if (existingUsers.Any(user => user.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<LoginResponse>(new Error("Auth.EmailTaken", "Email is already registered"));
        }

        if (existingUsers.Any(user => user.Username.Equals(normalizedUsername, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<LoginResponse>(new Error("Auth.UsernameTaken", "Username is already taken"));
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Username = request.Username.Trim(),
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FullName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Provider = "local",
            EmailVerified = false,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _userRepository.AddAsync(user, cancellationToken);
        await SendVerificationCodeAsync(user, _emailRepository, _appName, _verificationCodeStore, cancellationToken);

        var role = await GetOrCreateDefaultRole(_roleRepository, cancellationToken);
        var userRole = new UserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            RoleId = role.Id,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _userRoleRepository.AddAsync(userRole, cancellationToken);

        var roles = new List<string> { role.Name };
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

    private static string ResolveAppName(IConfiguration configuration)
    {
        var fromName = configuration["Email:FromName"];
        if (!string.IsNullOrWhiteSpace(fromName))
        {
            return fromName;
        }

        var fromEmail = configuration["Email:FromEmail"];
        if (!string.IsNullOrWhiteSpace(fromEmail))
        {
            return fromEmail;
        }

        return "Application";
    }

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

    private static async Task SendVerificationCodeAsync(
        User user,
        IEmailRepository emailRepository,
        string appName,
        IVerificationCodeStore verificationCodeStore,
        CancellationToken cancellationToken)
    {
        var code = VerificationCodeGenerator.GenerateNumericCode();
        await verificationCodeStore.StoreAsync(
            VerificationCodePurpose.EmailVerification,
            user.Email,
            code,
            TimeSpan.FromMinutes(VerificationCodeMinutes),
            cancellationToken);

        const string subject = "Verify your email";
        var tokens = new Dictionary<string, string>
        {
            ["SUBJECT"] = subject,
            ["TITLE"] = subject,
            ["BODY"] = "Use the code below to verify your email address.",
            ["CODE"] = code,
            ["FOOTNOTE"] = "This code expires in 10 minutes.",
            ["APP_NAME"] = appName
        };

        await emailRepository.SendEmailByKeyAsync(
            user.Email,
            EmailTemplateKeys.EmailVerification,
            tokens,
            cancellationToken);
    }
}
