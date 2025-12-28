using System.Threading.Tasks;
using Application.Users.Helpers;
using Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Contracts.UserCreating;

namespace Application.Consumers;

public sealed class WelcomeEmailConsumer(IEmailRepository emailRepository, IConfiguration configuration)
    : IConsumer<WelcomeEmailRequested>
{
    public async Task Consume(ConsumeContext<WelcomeEmailRequested> context)
    {
        var message = context.Message;
        var appName = ResolveAppName(configuration);
        var displayName = string.IsNullOrWhiteSpace(message.FullName)
            ? message.Username
            : message.FullName;
        var safeName = string.IsNullOrWhiteSpace(displayName) ? "there" : displayName;
        var subject = $"Welcome to {appName}";

        var tokens = new Dictionary<string, string>
        {
            ["SUBJECT"] = subject,
            ["TITLE"] = subject,
            ["BODY"] = "Thanks for creating your account. You can now explore your workspace.",
            ["APP_NAME"] = appName,
            ["USER_NAME"] = safeName
        };

        await emailRepository.SendEmailByKeyAsync(
            message.Email,
            EmailTemplateKeys.Welcome,
            tokens,
            context.CancellationToken);
    }

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

    Task IConsumer<WelcomeEmailRequested>.Consume(ConsumeContext<WelcomeEmailRequested> context)
    {
        throw new System.NotImplementedException();
    }
}
