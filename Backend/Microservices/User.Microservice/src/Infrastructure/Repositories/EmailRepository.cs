using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Configs;
using Infrastructure.Persistence.Context;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Infrastructure.Repositories;

public sealed class EmailRepository(MyDbContext context, IOptions<EmailOptions> options) : IEmailRepository
{
    private readonly EmailOptions _options = options.Value;

    public async Task<EmailTemplateContent?> GetTemplateContentAsync(string templateKey, string language,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateKey) || string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var template = await context.Set<EmailTemplate>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Key == templateKey && t.IsActive, cancellationToken);

        if (template == null)
        {
            return null;
        }

        var content = await context.Set<EmailTemplateContent>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmailTemplateId == template.Id && c.Language == language, cancellationToken);

        return content;
    }

    public async Task SendEmailByKeyAsync(string to, string templateKey, string language,
        IDictionary<string, string>? tokens = null, CancellationToken cancellationToken = default)
    {
        var content = await GetTemplateContentAsync(templateKey, language, cancellationToken);
        if (content == null)
        {
            throw new InvalidOperationException("Email template content not found.");
        }

        var subject = ApplyTokens(content.Subject, tokens);
        var htmlBody = ApplyTokens(content.HtmlBody, tokens);
        var textBody = content.TextBody != null ? ApplyTokens(content.TextBody, tokens) : null;

        await SendEmailAsync(to, subject, htmlBody, textBody, cancellationToken);
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        var fromEmail = string.IsNullOrWhiteSpace(_options.FromEmail) ? _options.Username : _options.FromEmail;

        message.From.Add(new MailboxAddress(_options.FromName ?? string.Empty, fromEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = textBody
        };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        var secureSocket = ResolveSecureSocketOptions();

        await client.ConnectAsync(_options.Host, _options.Port, secureSocket, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private SecureSocketOptions ResolveSecureSocketOptions()
    {
        if (_options.UseSsl)
        {
            return SecureSocketOptions.SslOnConnect;
        }

        return _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
    }

    private static string ApplyTokens(string input, IDictionary<string, string>? tokens)
    {
        if (string.IsNullOrEmpty(input) || tokens == null || tokens.Count == 0)
        {
            return input;
        }

        var output = input;
        foreach (var (key, value) in tokens)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            output = output.Replace($"{{{{{key}}}}}", value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return output;
    }
}
