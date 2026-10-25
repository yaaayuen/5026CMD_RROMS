namespace Rroms.Web.Services;

/// <summary>Once a minute, tells customers about scheduled and recurring promotions that have started showing (Alex).</summary>
public class AnnouncementScheduler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AnnouncementScheduler> _logger;

    public AnnouncementScheduler(IServiceScopeFactory scopes, ILogger<AnnouncementScheduler> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // The database context is scoped, so each run gets its own.
                    using IServiceScope scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IAnnouncementService>().NotifyDueAsync();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One failed run must not stop later ones; the promotion stays due and is retried.
                    _logger.LogError(exception, "Failed to send scheduled promotion notifications.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when the application is stopping.
        }
    }
}
