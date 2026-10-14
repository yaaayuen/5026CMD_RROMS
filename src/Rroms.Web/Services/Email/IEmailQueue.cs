namespace Rroms.Web.Services.Email;

/// <summary>
/// A queue of emails to be sent in the background (YY-6).
/// Why a queue? Sending through Gmail can take a few seconds or fail. If a page sent email directly,
/// the customer would wait, and a Gmail outage could break a booking. With a queue, the page only
/// adds the email (microseconds) and a background service sends it later.
/// </summary>

public interface IEmailQueue
{
    //Adds an email to the queue. Returns false if the queue is full (the email is dropped and logged)
    bool TryEnqueue(EmailMessage message);

    //Waits for and returns queued emails, one by one, until the application stops
    IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken);
}
