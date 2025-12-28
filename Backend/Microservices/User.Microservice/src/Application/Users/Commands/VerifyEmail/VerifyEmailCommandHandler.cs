using Application.Abstractions.Security;
using Application.Users.Helpers;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands.VerifyEmail;

internal sealed class VerifyEmailCommandHandler(
    IRepository<User> userRepository,
    IVerificationCodeStore verificationCodeStore)
    : ICommandHandler<VerifyEmailCommand>
{
    public async Task<Result> Handle(VerifyEmailCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var users = await userRepository.FindAsync(
            user => user.Email.ToLower() == normalizedEmail,
            cancellationToken);
        var user = users.FirstOrDefault();

        if (user == null)
        {
            return Result.Failure(new Error("Auth.UserNotFound", "User not found"));
        }

        var isValid = await verificationCodeStore.ValidateAsync(
            VerificationCodePurpose.EmailVerification,
            normalizedEmail,
            request.Code,
            cancellationToken);

        if (!isValid)
        {
            return Result.Failure(new Error("Auth.InvalidVerificationCode", "Invalid or expired code"));
        }

        user.EmailVerified = true;
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        userRepository.Update(user);
        await verificationCodeStore.RemoveAsync(
            VerificationCodePurpose.EmailVerification,
            normalizedEmail,
            cancellationToken);

        return Result.Success();
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
