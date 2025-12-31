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

public sealed record UpdateUserCommand(
    Guid UserId,
    string? Username,
    string? Email,
    string? Password,
    string? FullName,
    string? PhoneNumber,
    string? Address,
    DateTime? Birthday,
    Guid? AvatarResourceId,
    decimal? MeAiCoin,
    bool? EmailVerified) : IRequest<Result<AdminUserResponse>>;

public sealed class UpdateUserCommandHandler
    : IRequestHandler<UpdateUserCommand, Result<AdminUserResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;
    private readonly IPasswordHasher _passwordHasher;

    public UpdateUserCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<AdminUserResponse>> Handle(UpdateUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.UserId, cancellationToken);

        if (user == null || user.IsDeleted)
        {
            return Result.Failure<AdminUserResponse>(new Error("User.NotFound", "User not found"));
        }

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var normalizedUsername = NormalizeUsername(request.Username);
            var usernameExists = await _userRepository.GetAll()
                .AsNoTracking()
                .AnyAsync(
                    u => u.Username.ToLower() == normalizedUsername && u.Id != user.Id,
                    cancellationToken);

            if (usernameExists)
            {
                return Result.Failure<AdminUserResponse>(
                    new Error("User.UsernameTaken", "Username is already taken"));
            }

            user.Username = request.Username.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalizedEmail = NormalizeEmail(request.Email);
            var emailExists = await _userRepository.GetAll()
                .AsNoTracking()
                .AnyAsync(
                    u => u.Email.ToLower() == normalizedEmail && u.Id != user.Id,
                    cancellationToken);

            if (emailExists)
            {
                return Result.Failure<AdminUserResponse>(
                    new Error("User.EmailTaken", "Email is already registered"));
            }

            user.Email = normalizedEmail;
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = _passwordHasher.HashPassword(request.Password);
        }

        if (request.FullName != null)
        {
            user.FullName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim();
        }

        if (request.PhoneNumber != null)
        {
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        }

        if (request.Address != null)
        {
            user.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        }

        if (request.Birthday.HasValue)
        {
            user.Birthday = request.Birthday;
        }

        if (request.AvatarResourceId.HasValue)
        {
            user.AvatarResourceId = request.AvatarResourceId;
        }

        if (request.MeAiCoin.HasValue)
        {
            user.MeAiCoin = request.MeAiCoin;
        }

        if (request.EmailVerified.HasValue)
        {
            user.EmailVerified = request.EmailVerified.Value;
        }

        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _userRepository.Update(user);

        var roles = await ResolveRolesAsync(user.Id, cancellationToken);
        return Result.Success(AdminUserMapping.ToResponse(user, roles));
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string NormalizeUsername(string username) =>
        username.Trim().ToLowerInvariant();

    private async Task<List<string>> ResolveRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userRoles = await _userRoleRepository.GetAll()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && !ur.IsDeleted)
            .ToListAsync(cancellationToken);

        if (userRoles.Count == 0)
        {
            return new List<string> { UserRoleConstants.User };
        }

        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();
        var roles = await _roleRepository.GetAll()
            .AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .ToListAsync(cancellationToken);

        var roleNames = roles
            .Select(role => role.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return roleNames.Count == 0 ? new List<string> { UserRoleConstants.User } : roleNames;
    }
}
