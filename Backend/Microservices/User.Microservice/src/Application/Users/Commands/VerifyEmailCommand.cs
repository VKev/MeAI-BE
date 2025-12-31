using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Users.Helpers;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record VerifyEmailCommand(string Email, string Code) : IRequest<Result<MessageResponse>>;

public sealed class VerifyEmailCommandHandler
    : IRequestHandler<VerifyEmailCommand, Result<MessageResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IVerificationCodeStore _verificationCodeStore;

    public VerifyEmailCommandHandler(
        IUnitOfWork unitOfWork,
        IVerificationCodeStore verificationCodeStore)
    {
        _userRepository = unitOfWork.Repository<User>();
        _verificationCodeStore = verificationCodeStore;
    }

    public async Task<Result<MessageResponse>> Handle(VerifyEmailCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user == null)
        {
            return Result.Failure<MessageResponse>(new Error("Auth.UserNotFound", "User not found"));
        }

        var isValid = await _verificationCodeStore.ValidateAsync(
            VerificationCodePurpose.EmailVerification,
            normalizedEmail,
            request.Code,
            cancellationToken);

        if (!isValid)
        {
            return Result.Failure<MessageResponse>(
                new Error("Auth.InvalidVerificationCode", "Invalid or expired code"));
        }

        user.EmailVerified = true;
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _userRepository.Update(user);

        await _verificationCodeStore.RemoveAsync(
            VerificationCodePurpose.EmailVerification,
            normalizedEmail,
            cancellationToken);

        return Result.Success(new MessageResponse("Email verified successfully."));
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
