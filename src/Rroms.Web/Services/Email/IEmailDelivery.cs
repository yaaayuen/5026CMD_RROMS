namespace Rroms.Web.Services.Email;

/// <summary>
/// Actually sends one email (YY-6). Implemented with SMTP; replaceable in tests.
/// </summary>
public interface IEmailDelivery
{
    //Sends the message. Throws if the mail server rejects it
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
