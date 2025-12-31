using Application.Abstractions.Data;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Authentication;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record CreateUserCommand(
    string Username,
    string Email,
    string Password,
    string? FullName,
    string? PhoneNumber,
    string? Address,
    DateTime? Birthday,
    Guid? AvatarResourceId,
    decimal? MeAiCoin,
    bool? EmailVerified,
    string? Role) : IRequest<Result<AdminUserResponse>>;

public sealed class CreateUserCommandHandler
    : IRequestHandler<CreateUserCommand, Result<AdminUserResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;
    private readonly IPasswordHasher _passwordHasher;

    public CreateUserCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<AdminUserResponse>> Handle(CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var normalizedUsername = NormalizeUsername(request.Username);

        var existingUsers = await _userRepository.GetAll()
            .AsNoTracking()
            .Where(user =>
                user.Email.ToLower() == normalizedEmail ||
                user.Username.ToLower() == normalizedUsername)
            .ToListAsync(cancellationToken);

        if (existingUsers.Any(user => user.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<AdminUserResponse>(
                new Error("User.EmailTaken", "Email is already registered"));
        }

        if (existingUsers.Any(user => user.Username.Equals(normalizedUsername, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<AdminUserResponse>(
                new Error("User.UsernameTaken", "Username is already taken"));
        }

        var roleName = ResolveRoleName(request.Role);
        if (roleName == null)
        {
            return Result.Failure<AdminUserResponse>(
                new Error("User.RoleInvalid", "Role must be ADMIN, USER, or BANNED"));
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Username = request.Username.Trim(),
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FullName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            Birthday = request.Birthday,
            AvatarResourceId = request.AvatarResourceId,
            MeAiCoin = request.MeAiCoin ?? 0m,
            Provider = "admin",
            EmailVerified = request.EmailVerified ?? false,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _userRepository.AddAsync(user, cancellationToken);

        var role = await GetOrCreateRoleAsync(roleName, cancellationToken);
        var userRole = new UserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            RoleId = role.Id,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _userRoleRepository.AddAsync(userRole, cancellationToken);

        return Result.Success(AdminUserMapping.ToResponse(user, new List<string> { role.Name }));
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string NormalizeUsername(string username) =>
        username.Trim().ToLowerInvariant();

    private static string? ResolveRoleName(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return UserRoleConstants.User;
        }

        var normalized = role.Trim().ToUpperInvariant();
        return UserRoleConstants.AllowedRoles.Contains(normalized) ? normalized : null;
    }

    private async Task<Role> GetOrCreateRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Name == roleName, cancellationToken);

        if (role != null)
        {
            return role;
        }

        role = new Role
        {
            Id = Guid.CreateVersion7(),
            Name = roleName,
            Description = ResolveRoleDescription(roleName),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _roleRepository.AddAsync(role, cancellationToken);
        return role;
    }

    private static string ResolveRoleDescription(string roleName) =>
        roleName switch
        {
            UserRoleConstants.Admin => "Administrator",
            UserRoleConstants.Banned => "Banned user",
            _ => "Standard user"
        };
}
