using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rroms.Tests.Reservations;

/// <summary>
/// Lets a test run some code at the exact moment a DbContext is about to save, used to simulate
/// two customers booking at the same time.
/// </summary>
/// <remarks>
/// In real life the race looks like this: request A checks the table is free, request B checks the
/// table is free, B saves, then A saves. The service's own C# check cannot stop A, because A checked
/// before B saved. This interceptor reproduces that timing reliably: A's check has already passed,
/// and just before A's save we let B complete a booking for the same table and time.
/// </remarks>
public class BeforeSaveInterceptor : SaveChangesInterceptor
{
    private Func<Task>? _action;

    /// <summary>Runs <paramref name="action"/> once, just before the next save of the intercepted context.</summary>
    /// <param name="action">What the "other customer" does in the gap between check and save.</param>
    public void RunOnceBeforeNextSave(Func<Task> action)
    {
        _action = action;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_action != null)
        {
            // Clear first so the action only ever runs once.
            Func<Task> action = _action;
            _action = null;
            await action();
        }

        return result;
    }
}
