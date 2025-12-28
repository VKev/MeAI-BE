using Application.Abstractions.Security;
using Application.Users.Helpers;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Commands.ForgotPassword;

internal sealed class ForgotPasswordCommandHandler(
    IRepository<User> userRepository,
    IEmailRepository emailRepository,
    IVerificationCodeStore verificationCodeStore,
    IConfiguration configuration)
    : ICommandHandler<ForgotPasswordCommand>
{
    private const int CodeTtlMinutes = 10;
    private readonly string _appName = ResolveAppName(configuration);

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
        var tokens = new Dictionary<string, string>
        {
            ["SUBJECT"] = subject,
            ["TITLE"] = "Reset your password",
            ["BODY"] = "Use the code below to reset your password.",
            ["CODE"] = code,
            ["FOOTNOTE"] = "This code expires in 10 minutes.",
            ["APP_NAME"] = _appName
        };

        await emailRepository.SendEmailByKeyAsync(
            user.Email,
            EmailTemplateKeys.PasswordReset,
            tokens,
            cancellationToken);
        return Result.Success();
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string ResolveAppName(IConfiguration configuration)
    {
        var fromName = configuration["Email:FromName"];
        if (!string.IsNullOrWhiteSpace(fromName))
        {
            return fromName;
        }

        var fromEmail = configuration["Email:FromEmail"];
        if (!string.IsNullOrWhiteSpace(fromEmail))
        {
            return fromEmail;
        }

        return "Application";
    }
}
