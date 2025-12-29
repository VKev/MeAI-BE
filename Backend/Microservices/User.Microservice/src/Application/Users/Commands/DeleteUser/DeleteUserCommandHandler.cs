using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands.DeleteUser;

internal sealed class DeleteUserCommandHandler(IRepository<User> userRepository)
    : ICommandHandler<DeleteUserCommand>
{
    public async Task<Result> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null || user.IsDeleted)
        {
            return Result.Failure(new Error("User.NotFound", "User not found"));
        }

        user.IsDeleted = true;
        user.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        userRepository.Update(user);

        return Result.Success();
    }
}
