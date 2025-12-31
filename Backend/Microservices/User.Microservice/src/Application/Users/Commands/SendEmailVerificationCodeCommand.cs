using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Users.Helpers;
using Application.Users.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Commands;

public sealed record SendEmailVerificationCodeCommand(string Email) : IRequest<Result<MessageResponse>>;

public sealed class SendEmailVerificationCodeCommandHandler
    : IRequestHandler<SendEmailVerificationCodeCommand, Result<MessageResponse>>
{
    private const int CodeTtlMinutes = 10;
    private readonly IRepository<User> _userRepository;
    private readonly IEmailRepository _emailRepository;
    private readonly IVerificationCodeStore _verificationCodeStore;
    private readonly string _appName;

    public SendEmailVerificationCodeCommandHandler(
        IUnitOfWork unitOfWork,
        IEmailRepository emailRepository,
        IVerificationCodeStore verificationCodeStore,
        IConfiguration configuration)
    {
        _userRepository = unitOfWork.Repository<User>();
        _emailRepository = emailRepository;
        _verificationCodeStore = verificationCodeStore;
        _appName = ResolveAppName(configuration);
    }

    public async Task<Result<MessageResponse>> Handle(SendEmailVerificationCodeCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _userRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user == null || user.EmailVerified)
        {
            return Result.Success(new MessageResponse("If the email exists, a verification code was sent."));
        }

        var code = VerificationCodeGenerator.GenerateNumericCode();
        await _verificationCodeStore.StoreAsync(
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

        await _emailRepository.SendEmailByKeyAsync(
            user.Email,
            EmailTemplateKeys.EmailVerification,
            tokens,
            cancellationToken);

        return Result.Success(new MessageResponse("If the email exists, a verification code was sent."));
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
