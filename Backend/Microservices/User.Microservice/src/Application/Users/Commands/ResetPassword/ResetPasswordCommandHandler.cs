using Application.Abstractions.Security;
using Application.Users.Helpers;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Authentication;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;
using System.Linq;

namespace Application.Users.Commands.ResetPassword;

internal sealed class ResetPasswordCommandHandler(
    IRepository<User> userRepository,
    IVerificationCodeStore verificationCodeStore,
    IPasswordHasher passwordHasher)
    : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var users = await userRepository.FindAsync(
            user => user.Email.ToLower() == normalizedEmail,
            cancellationToken);

        var user = users.FirstOrDefault();
        if (user == null)
        {
            return Result.Failure(new Error("Auth.InvalidPasswordReset", "Invalid or expired reset code"));
        }

        var isValid = await verificationCodeStore.ValidateAsync(
            VerificationCodePurpose.PasswordReset,
            normalizedEmail,
            request.Code,
            cancellationToken);

        if (!isValid)
        {
            return Result.Failure(new Error("Auth.InvalidPasswordReset", "Invalid or expired reset code"));
        }

        user.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        userRepository.Update(user);
        await verificationCodeStore.RemoveAsync(
            VerificationCodePurpose.PasswordReset,
            normalizedEmail,
            cancellationToken);

        return Result.Success();
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
