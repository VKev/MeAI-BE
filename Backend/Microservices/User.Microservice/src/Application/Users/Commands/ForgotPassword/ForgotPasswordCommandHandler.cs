using Application.Abstractions.Security;
using Application.Users.Helpers;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Commands.ForgotPassword;

internal sealed class ForgotPasswordCommandHandler(
    IRepository<User> userRepository,
    IEmailRepository emailRepository,
    IVerificationCodeStore verificationCodeStore)
    : ICommandHandler<ForgotPasswordCommand>
{
    private const int CodeTtlMinutes = 10;

    public async Task<Result> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var users = await userRepository.FindAsync(
            user => user.Email.ToLower() == normalizedEmail,
            cancellationToken);

        var user = users.FirstOrDefault();
        if (user == null)
        {
            return Result.Success();
        }

        var code = VerificationCodeGenerator.GenerateNumericCode();
        await verificationCodeStore.StoreAsync(
            VerificationCodePurpose.PasswordReset,
            normalizedEmail,
            code,
            TimeSpan.FromMinutes(CodeTtlMinutes),
            cancellationToken);

        const string subject = "Password reset code";
        var htmlBody = $"<p>Use this code to reset your password: <strong>{code}</strong>.</p>";
        var textBody = $"Use this code to reset your password: {code}.";

        await emailRepository.SendEmailAsync(user.Email, subject, htmlBody, textBody, cancellationToken);
        return Result.Success();
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
