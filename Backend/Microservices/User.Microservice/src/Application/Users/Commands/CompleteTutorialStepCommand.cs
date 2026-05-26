using Application.Abstractions.Data;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record CompleteTutorialStepCommand(
    Guid UserId,
    int Step) : IRequest<Result<UserProfileResponse>>;

public sealed class CompleteTutorialStepCommandHandler
    : IRequestHandler<CompleteTutorialStepCommand, Result<UserProfileResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;

    public CompleteTutorialStepCommandHandler(IUnitOfWork unitOfWork)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
    }

    public async Task<Result<UserProfileResponse>> Handle(
        CompleteTutorialStepCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.UserId, cancellationToken);

        if (user == null || user.IsDeleted)
        {
            return Result.Failure<UserProfileResponse>(new Error("User.NotFound", "User not found"));
        }

        var completedAt = DateTimeExtensions.PostgreSqlUtcNow;
        switch (request.Step)
        {
            case 1:
                user.TutorialStep1CompletedAt ??= completedAt;
                break;
            case 2:
                if (!user.TutorialStep1CompletedAt.HasValue)
                {
                    return Result.Failure<UserProfileResponse>(
                        new Error("Tutorial.Step1Required", "Tutorial step 1 must be completed first"));
                }

                user.TutorialStep2CompletedAt ??= completedAt;
                break;
            default:
                return Result.Failure<UserProfileResponse>(
                    new Error("Tutorial.InvalidStep", "Tutorial step must be 1 or 2"));
        }

        user.UpdatedAt = completedAt;
        _userRepository.Update(user);

        var roles = await ResolveRolesAsync(user.Id, cancellationToken);
        return Result.Success(UserProfileMapping.ToResponse(user, roles));
    }

    private async Task<List<string>> ResolveRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userRoles = await _userRoleRepository.GetAll()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && !ur.IsDeleted)
            .ToListAsync(cancellationToken);

        if (userRoles.Count == 0)
        {
            return [UserRoleConstants.User];
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

        return roleNames.Count == 0 ? [UserRoleConstants.User] : roleNames;
    }
}
