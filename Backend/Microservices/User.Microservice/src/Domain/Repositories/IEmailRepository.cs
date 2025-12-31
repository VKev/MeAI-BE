using Domain.Entities;

namespace Domain.Repositories;

public interface IEmailRepository
{
    Task<EmailTemplateContent?> GetTemplateContentAsync(string templateKey,
        CancellationToken cancellationToken = default);

    Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null,
        CancellationToken cancellationToken = default);

    Task SendEmailByKeyAsync(string to, string templateKey,
        IDictionary<string, string>? tokens = null, CancellationToken cancellationToken = default);
}
