using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record DeleteUserCommand(Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, Result<bool>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<RefreshToken> _refreshTokenRepository;

    public DeleteUserCommandHandler(IUnitOfWork unitOfWork)
    {
        _userRepository = unitOfWork.Repository<User>();
        _refreshTokenRepository = unitOfWork.Repository<RefreshToken>();
    }

    public async Task<Result<bool>> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null || user.IsDeleted)
        {
            return Result.Failure<bool>(new Error("User.NotFound", "User not found"));
        }

        var revokedAt = DateTimeExtensions.PostgreSqlUtcNow;
        user.IsDeleted = true;
        user.DeletedAt = revokedAt;
        user.UpdatedAt = revokedAt;
        _userRepository.Update(user);

        var issuedTokens = await _refreshTokenRepository.GetAll()
            .Where(token =>
                token.UserId == user.Id &&
                (token.RevokedAt == null || token.AccessTokenRevokedAt == null))
            .ToListAsync(cancellationToken);

        foreach (var issuedToken in issuedTokens)
        {
            issuedToken.RevokedAt ??= revokedAt;
            issuedToken.AccessTokenRevokedAt ??= revokedAt;
            _refreshTokenRepository.Update(issuedToken);
        }

        return Result.Success(true);
    }
}
