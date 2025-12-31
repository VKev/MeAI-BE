using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record DeleteUserCommand(Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, Result<bool>>
{
    private readonly IRepository<User> _userRepository;

    public DeleteUserCommandHandler(IUnitOfWork unitOfWork)
    {
        _userRepository = unitOfWork.Repository<User>();
    }

    public async Task<Result<bool>> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null || user.IsDeleted)
        {
            return Result.Failure<bool>(new Error("User.NotFound", "User not found"));
        }

        user.IsDeleted = true;
        user.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _userRepository.Update(user);

        return Result.Success(true);
    }
}
