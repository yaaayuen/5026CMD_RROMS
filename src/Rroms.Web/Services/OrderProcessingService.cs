using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Kitchen queries and status changes, with the seating check enforced in the database update.</summary>
public class OrderProcessingService : IOrderProcessingService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;

    public OrderProcessingService(ApplicationDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public Task<List<Order>> GetKitchenOrdersAsync(OrderStatus? status = null)
    {
        var query = _context.Orders.AsNoTracking()
            .Include(o => o.Reservation!).ThenInclude(r => r.DiningTable)
            .Include(o => o.Items).ThenInclude(i => i.MenuItem)
            .Where(o => o.Reservation!.Status == ReservationStatus.Seated
                || (o.Reservation.Status == ReservationStatus.Completed && o.Status != OrderStatus.Placed));
        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }
        else
        {
            query = query.Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled);
        }
        return query.OrderBy(o => o.PlacedAtUtc).ToListAsync();
    }

    public Task<List<Order>> GetCustomerOrdersAsync(string customerId)
    {
        return _context.Orders.AsNoTracking().Include(o => o.Reservation)
            .Include(o => o.Items).ThenInclude(i => i.MenuItem)
            .Where(o => o.CustomerId == customerId && o.Reservation!.CustomerId == customerId)
            .OrderByDescending(o => o.PlacedAtUtc).ToListAsync();
    }

    public async Task<ServiceResult> ChangeStatusAsync(int id, OrderStatus expected, OrderStatus next)
    {
        if (!OrderStatusRules.CanMove(expected, next))
        {
            return ServiceResult.Failure("That order status change is not allowed.");
        }
        var order = await _context.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id);
        if (order == null) { return ServiceResult.Failure("Order not found."); }

        // Compare-and-update prevents two staff clicks from moving the same version twice.
        var query = _context.Orders.Where(o => o.Id == id && o.Status == expected);
        if (next == OrderStatus.Preparing)
        {
            query = query.Where(o => o.Reservation!.Status == ReservationStatus.Seated);
        }
        int changed = await query.ExecuteUpdateAsync(set => set
            .SetProperty(o => o.Status, next).SetProperty(o => o.UpdatedAtUtc, DateTime.UtcNow));
        if (changed == 0)
        {
            return ServiceResult.Failure("The order changed, or the customer is not seated. Refresh and try again.");
        }
        await _notifications.NotifyUserAsync(order.CustomerId, NotificationType.OrderUpdated,
            "Order updated", $"Order #{id} is now {next}.", "/Customer/Tracking");
        return ServiceResult.Success();
    }

    public async Task<int> CancelUnstartedOrdersForReservationAsync(int reservationId)
    {
        // FindAsync uses WL's pending tracked status when called before his SaveChanges.
        var reservation = await _context.Reservations.FindAsync(reservationId);
        if (reservation == null || (reservation.Status != ReservationStatus.Cancelled && reservation.Status != ReservationStatus.NoShow))
        {
            return 0;
        }
        var orders = await _context.Orders.Where(o => o.ReservationId == reservationId && o.Status == OrderStatus.Placed).ToListAsync();
        foreach (var order in orders)
        {
            order.Status = OrderStatus.Cancelled;
            order.UpdatedAtUtc = DateTime.UtcNow;
        }
        // One SaveChanges transaction writes the booking, released slots and cancelled orders together.
        await _context.SaveChangesAsync();
        foreach (var order in orders)
        {
            await _notifications.NotifyUserAsync(order.CustomerId, NotificationType.OrderUpdated,
                "Pre-order cancelled", $"Order #{order.Id} was cancelled because the booking is {reservation.Status}.", "/Customer/Tracking");
        }
        return orders.Count;
    }
}
