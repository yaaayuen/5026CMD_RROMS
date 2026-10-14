namespace Rroms.Web.Services.Email;

/// <summary>
/// Long-running background task that sends queued emails one at a time (YY-6).
/// </summary>
public class EmailBackgroundService : BackgroundService
{
    private readonly IEmailQueue _queue;
    private readonly IEmailDelivery _delivery;
    private readonly ILogger<EmailBackgroundService> _logger;

    //Receives the queue, the sender and a logger from dependency injection
    public EmailBackgroundService(IEmailQueue queue, IEmailDelivery delivery, ILogger<EmailBackgroundService> logger)
    {
        _queue = queue;
        _delivery = delivery;
        _logger = logger;
    }

    //Reads the queue until the application shuts down
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (EmailMessage message in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await _delivery.SendAsync(message, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Catch everything except shutdown, so one bad email does not stop all the others.
                    _logger.LogError(exception, "Failed to send email to {Address}.", message.ToAddress);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when the application is stopping.
        }
    }
}
