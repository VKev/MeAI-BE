using Application.Users.Helpers;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Extensions;

namespace Infrastructure.EmailTemplates;

public static class EmailTemplateSeeder
{
    private sealed record TemplateDefinition(
        string Key,
        string Description,
        string Subject,
        string HtmlFileName,
        string? TextBody);

    private static readonly TemplateDefinition[] Templates =
    [
        new TemplateDefinition(
            EmailTemplateKeys.EmailVerification,
            "Email verification code",
            "Verify your email",
            "email-verification.html",
            "Your verification code is {{CODE}}."),
        new TemplateDefinition(
            EmailTemplateKeys.PasswordReset,
            "Password reset code",
            "Password reset code",
            "password-reset.html",
            "Use this code to reset your password: {{CODE}}."),
        new TemplateDefinition(
            EmailTemplateKeys.Welcome,
            "Welcome email",
            "Welcome to {{APP_NAME}}",
            "welcome.html",
            "Welcome to {{APP_NAME}}, {{USER_NAME}}.")
    ];

    public static async Task SeedAsync(MyDbContext context, ILogger logger, CancellationToken cancellationToken)
    {
        var hasAnyTemplates = await context.Set<EmailTemplate>().AnyAsync(cancellationToken);
        if (hasAnyTemplates)
        {
            logger.LogInformation("Email templates already exist; skipping seed.");
            return;
        }

        var basePath = Path.Combine(AppContext.BaseDirectory, "EmailTemplates");
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var templateEntities = new List<EmailTemplate>(Templates.Length);
        var contentEntities = new List<EmailTemplateContent>(Templates.Length);

        foreach (var template in Templates)
        {
            var htmlPath = Path.Combine(basePath, template.HtmlFileName);
            if (!File.Exists(htmlPath))
            {
                logger.LogWarning("Email template file not found: {Path}", htmlPath);
                continue;
            }

            var htmlBody = await File.ReadAllTextAsync(htmlPath, cancellationToken);
            var templateEntity = new EmailTemplate
            {
                Id = Guid.CreateVersion7(),
                Key = template.Key,
                Description = template.Description,
                IsActive = true,
                CreatedAt = now
            };

            templateEntities.Add(templateEntity);
            contentEntities.Add(new EmailTemplateContent
            {
                Id = Guid.CreateVersion7(),
                EmailTemplateId = templateEntity.Id,
                Subject = template.Subject,
                HtmlBody = htmlBody,
                TextBody = template.TextBody,
                CreatedAt = now
            });
        }

        if (templateEntities.Count == 0)
        {
            logger.LogWarning("No email templates were seeded because no template files were found.");
            return;
        }

        await context.Set<EmailTemplate>().AddRangeAsync(templateEntities, cancellationToken);
        await context.Set<EmailTemplateContent>().AddRangeAsync(contentEntities, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded {Count} email templates.", templateEntities.Count);
    }
}
