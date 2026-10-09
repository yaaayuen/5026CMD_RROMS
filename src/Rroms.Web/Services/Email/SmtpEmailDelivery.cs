using System.Net;
using System.Net.Mail;

using Microsoft.Extensions.Options;

using Rroms.Web.Options;

namespace Rroms.Web.Services.Email;

/// <summary>
/// Sends email through an SMTP server such as Gmail (YY-6).
/// </summary>

public class SmtpEmailDelivery : IEmailDelivery
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailDelivery> _logger;

    //Receives the SMTP settings and a logger from dependency injection
    public SmtpEmailDelivery(IOptions<SmtpOptions> options, ILogger<SmtpEmailDelivery> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            // Not an error: a developer without Gmail secrets still gets in-app notifications.
            _logger.LogInformation("SMTP is not configured; skipped email to {Address}: {Subject}", message.ToAddress, message.Subject);
            return;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true
        };
        mail.To.Add(message.ToAddress);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Credentials = new NetworkCredential(_options.Username, _options.Password)
        };

        await client.SendMailAsync(mail, cancellationToken);
        _logger.LogInformation("Email sent to {Address}: {Subject}", message.ToAddress, message.Subject);
    }
}