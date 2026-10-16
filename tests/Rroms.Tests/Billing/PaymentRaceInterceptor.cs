using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rroms.Web.Models;

namespace Rroms.Tests.Billing;

/// <summary>Lets a second request save after the first request's duplicate check but before its insert.</summary>
public class PaymentRaceInterceptor : SaveChangesInterceptor
{
    public Func<Task>? BeforePaymentSave { get; set; }
    /// <summary>Runs the competing save once, only when a payment insert is pending.</summary>
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (BeforePaymentSave != null && eventData.Context!.ChangeTracker.Entries<Payment>()
            .Any(entry => entry.State == EntityState.Added))
        {
            Func<Task> callback = BeforePaymentSave;
            BeforePaymentSave = null;
            await callback();
        }
        return result;
    }
}
