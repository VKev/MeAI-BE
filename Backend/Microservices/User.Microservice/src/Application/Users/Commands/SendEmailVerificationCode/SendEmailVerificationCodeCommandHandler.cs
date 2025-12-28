using Application.Abstractions.Security;
using Application.Users.Helpers;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using System.Linq;

namespace Application.Users.Commands.SendEmailVerificationCode;

internal sealed class SendEmailVerificationCodeCommandHandler(
    IRepository<User> userRepository,
    IEmailRepository emailRepository,
    IVerificationCodeStore verificationCodeStore,
    IConfiguration configuration)
    : ICommandHandler<SendEmailVerificationCodeCommand>
{
    private const int CodeTtlMinutes = 10;
    private readonly string _appName = ResolveAppName(configuration);

    public async Task<Result> Handle(SendEmailVerificationCodeCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var users = await userRepository.FindAsync(
            user => user.Email.ToLower() == normalizedEmail,
            cancellationToken);

        var user = users.FirstOrDefault();
        if (user == null || user.EmailVerified)
        {
            return Result.Success();
        }

        var code = VerificationCodeGenerator.GenerateNumericCode();
        await verificationCodeStore.StoreAsync(
            VerificationCodePurpose.EmailVerification,
            normalizedEmail,
            code,
            TimeSpan.FromMinutes(CodeTtlMinutes),
            cancellationToken);

        const string subject = "Verify your email";
        var tokens = new Dictionary<string, string>
        {
            ["SUBJECT"] = subject,
            ["TITLE"] = subject,
            ["BODY"] = "Use the code below to verify your email address.",
            ["CODE"] = code,
            ["FOOTNOTE"] = "This code expires in 10 minutes.",
            ["APP_NAME"] = _appName
        };

        await emailRepository.SendEmailByKeyAsync(
            user.Email,
            EmailTemplateKeys.EmailVerification,
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
