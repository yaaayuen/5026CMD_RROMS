using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Rroms.Web.Services.Email;

namespace Rroms.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IEmailQueue"/>: keeps queued emails in a list and never sends them,
/// so tests can check emails were queued without contacting Gmail.
/// </summary>
public class RecordingEmailQueue : IEmailQueue
{
    /// <summary>Every email that was queued.</summary>
    public ConcurrentQueue<EmailMessage> Queued { get; } = new ConcurrentQueue<EmailMessage>();

    /// <inheritdoc />
    public bool TryEnqueue(EmailMessage message)
    {
        Queued.Enqueue(message);
        return true;
    }

    /// <summary>
    /// The background email service reads from here. This fake never returns anything;
    /// it simply waits until the test host shuts down.
    /// </summary>
    public async IAsyncEnumerable<EmailMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Normal at shutdown.
        }

        yield break;
    }
}
