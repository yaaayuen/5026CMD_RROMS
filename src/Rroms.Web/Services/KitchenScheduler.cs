namespace Rroms.Web.Services;

/// <summary>Every few seconds, runs the automatic kitchen so countdowns finish and queued orders start without anyone watching (Alex).</summary>
public class KitchenScheduler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<KitchenScheduler> _logger;

    public KitchenScheduler(IServiceScopeFactory scopes, ILogger<KitchenScheduler> logger)
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
                    using IServiceScope scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IOrderProcessingService>().AdvanceKitchenAsync();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A failed run is retried on the next tick; the kitchen pages also advance the board when they load.
                    _logger.LogError(exception, "Failed to advance the kitchen queue.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when the application is stopping.
        }
    }
}
