using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record ActivateUserCommand(Guid UserId) : IRequest<Result<bool>>;

public sealed class ActivateUserCommandHandler : IRequestHandler<ActivateUserCommand, Result<bool>>
{
    private readonly IRepository<User> _userRepository;

    public ActivateUserCommandHandler(IUnitOfWork unitOfWork)
    {
        _userRepository = unitOfWork.Repository<User>();
    }

    public async Task<Result<bool>> Handle(ActivateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return Result.Failure<bool>(new Error("User.NotFound", "User not found"));
        }

        if (!user.IsDeleted)
        {
            return Result.Success(true);
        }

        user.IsDeleted = false;
        user.DeletedAt = null;
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _userRepository.Update(user);

        return Result.Success(true);
    }
}
