using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Users.Helpers;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Authentication;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record ResetPasswordCommand(string Email, string Code, string NewPassword)
    : IRequest<Result<MessageResponse>>;

public sealed class ResetPasswordCommandHandler
    : IRequestHandler<ResetPasswordCommand, Result<MessageResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IVerificationCodeStore _verificationCodeStore;
    private readonly IPasswordHasher _passwordHasher;

    public ResetPasswordCommandHandler(
        IUnitOfWork unitOfWork,
        IVerificationCodeStore verificationCodeStore,
        IPasswordHasher passwordHasher)
    {
        _userRepository = unitOfWork.Repository<User>();
        _verificationCodeStore = verificationCodeStore;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<MessageResponse>> Handle(ResetPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user == null)
        {
            return Result.Failure<MessageResponse>(
                new Error("Auth.InvalidPasswordReset", "Invalid or expired reset code"));
        }

        var isValid = await _verificationCodeStore.ValidateAsync(
            VerificationCodePurpose.PasswordReset,
            normalizedEmail,
            request.Code,
            cancellationToken);

        if (!isValid)
        {
            return Result.Failure<MessageResponse>(
                new Error("Auth.InvalidPasswordReset", "Invalid or expired reset code"));
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _userRepository.Update(user);

        await _verificationCodeStore.RemoveAsync(
            VerificationCodePurpose.PasswordReset,
            normalizedEmail,
            cancellationToken);

        return Result.Success(new MessageResponse("Password reset successfully."));
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
