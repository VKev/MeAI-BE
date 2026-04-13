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

public sealed record ChangePasswordCommand(
    Guid UserId,
    string OldPassword,
    string NewPassword) : IRequest<Result<MessageResponse>>;

public sealed class ChangePasswordCommandHandler
    : IRequestHandler<ChangePasswordCommand, Result<MessageResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    public ChangePasswordCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
    {
        _userRepository = unitOfWork.Repository<User>();
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<MessageResponse>> Handle(
        ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.UserId, cancellationToken);

        if (user == null || user.IsDeleted)
        {
            return Result.Failure<MessageResponse>(new Error("User.NotFound", "User not found"));
        }

        if (!_passwordHasher.VerifyPassword(request.OldPassword, user.PasswordHash))
        {
            return Result.Failure<MessageResponse>(
                new Error("Auth.InvalidOldPassword", "Old password is incorrect"));
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _userRepository.Update(user);

        return Result.Success(new MessageResponse("Password changed successfully."));
    }
}
