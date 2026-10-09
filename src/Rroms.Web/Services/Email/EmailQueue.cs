using System.Threading.Channels;

namespace Rroms.Web.Services.Email;

/// <summary>
/// In-memory email queue built on <see cref="Channel{T}"/>, .NET's thread-safe producer/consumer queue.
/// Registered as a singleton so every request writes to the same queue.
/// </summary>
public class EmailQueue : IEmailQueue
{
    //Maximum emails waiting at once; protects memory if Gmail is down for a long time
    public const int Capacity = 1000;

    private readonly Channel<EmailMessage> _channel;
    private readonly ILogger<EmailQueue> _logger;

    //Creates the bounded channel
    public EmailQueue(ILogger<EmailQueue> logger)
    {
        _logger = logger;

        var options = new BoundedChannelOptions(Capacity)
        {
            // When full, TryWrite returns false instead of blocking the web request.
            FullMode = BoundedChannelFullMode.Wait,
            // Only the background service reads, which lets the channel optimise.
            SingleReader = true
        };

        _channel = Channel.CreateBounded<EmailMessage>(options);
    }

    public bool TryEnqueue(EmailMessage message)
    {
        bool queued = _channel.Writer.TryWrite(message);
        if (!queued)
        {
            _logger.LogWarning("Email queue is full; email to {Address} was dropped.", message.ToAddress);
        }

        return queued;
    }
    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
