using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
namespace Rroms.Web.Services;

/// <summary>Kitchen queries and status changes, with the seating check enforced in the database update.</summary>
public class OrderProcessingService : IOrderProcessingService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;
    private readonly int _slots;
    /// <summary>Most minutes staff can add to a countdown in one press.</summary>
    public const int MaxExtraMinutes = 30;

    public OrderProcessingService(ApplicationDbContext context, INotificationService notifications)
        : this(context, notifications, Microsoft.Extensions.Options.Options.Create(new KitchenOptions())) { }

    public OrderProcessingService(ApplicationDbContext context, INotificationService notifications, IOptions<KitchenOptions> kitchen)
    {
        _context = context;
        _notifications = notifications;
        _slots = Math.Max(1, kitchen.Value.CookingSlots);
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
        var order = await _context.Orders.AsNoTracking().Include(o => o.Reservation).SingleOrDefaultAsync(o => o.Id == id);
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
        if (next == OrderStatus.Preparing)
        {
            // A manual start still gets a countdown, so the board and the customer see the same timer.
            await AddTicketAsync(id, await CookingMinutesAsync(id), DateTime.UtcNow, InLineSince(order));
        }
        if (next == OrderStatus.Served)
        {
            DateTime served = DateTime.UtcNow;
            await _context.KitchenTickets.Where(t => t.OrderId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ServedAtUtc, served));
        }
        await _notifications.NotifyUserAsync(order.CustomerId, NotificationType.OrderUpdated,
            "Order updated", $"Order #{id} is now {OrderStatusRules.Label(next)}.", "/Customer/Tracking");
        return ServiceResult.Success();
    }

    public async Task<int> AdvanceKitchenAsync()
    {
        DateTime now = DateTime.UtcNow;
        int moved = 0;

        // 0. Orders already cooking before the automatic kitchen existed get a countdown from now, so none get stuck.
        List<int> untimed = await _context.Orders
            .Where(o => o.Status == OrderStatus.Preparing && !_context.KitchenTickets.Any(t => t.OrderId == o.Id))
            .Select(o => o.Id).ToListAsync();
        foreach (int id in untimed) { await AddTicketAsync(id, await CookingMinutesAsync(id), now, now); }

        // 1. Countdowns that have ended. The status check makes a repeated or parallel run harmless.
        var finished = await _context.KitchenTickets.AsNoTracking()
            .Where(t => t.ReadyAtUtc <= now && t.Order!.Status == OrderStatus.Preparing)
            .Select(t => new { t.OrderId, t.Order!.CustomerId }).ToListAsync();
        foreach (var item in finished)
        {
            if (await MoveAsync(item.OrderId, OrderStatus.Preparing, OrderStatus.ReadyToServe, now))
            {
                moved++;
                await _notifications.NotifyUserAsync(item.CustomerId, NotificationType.OrderUpdated,
                    "Your food is ready", $"Order #{item.OrderId} is ready. Staff will bring it to your table.", "/Customer/Tracking");
            }
        }

        // 2. Seated orders waiting for the kitchen, first in line first.
        List<Order> waiting = await WaitingOrdersAsync();
        int cooking = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Preparing);
        // While paused, food already cooking still finishes, but nothing new starts. Drinks need no kitchen.
        bool paused = await IsPausedAsync();
        foreach (Order order in waiting)
        {
            int minutes = LongestDish(order);
            if (minutes > 0 && (paused || cooking >= _slots)) { continue; }
            // Claim the order only if it is still Placed and the guests are still seated.
            bool claimed = await _context.Orders
                .Where(o => o.Id == order.Id && o.Status == OrderStatus.Placed && o.Reservation!.Status == ReservationStatus.Seated)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, minutes == 0 ? OrderStatus.ReadyToServe : OrderStatus.Preparing)
                    .SetProperty(o => o.UpdatedAtUtc, now)) == 1;
            if (!claimed) { continue; }
            await AddTicketAsync(order.Id, minutes, now, InLineSince(order));
            moved++;
            if (minutes == 0)
            {
                // Drinks need no cooking, so they never take a slot.
                await _notifications.NotifyUserAsync(order.CustomerId, NotificationType.OrderUpdated,
                    "Your order is ready", $"Order #{order.Id} is ready. Staff will bring it to your table.", "/Customer/Tracking");
                continue;
            }
            cooking++;
            await _notifications.NotifyUserAsync(order.CustomerId, NotificationType.OrderUpdated,
                "Now cooking", $"Order #{order.Id} is cooking and should be ready in about {minutes} min.", "/Customer/Tracking");
        }
        return moved;
    }

    public async Task<ServiceResult> FinishEarlyAsync(int orderId)
    {
        DateTime now = DateTime.UtcNow;
        var order = await _context.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId);
        if (order == null || !await MoveAsync(orderId, OrderStatus.Preparing, OrderStatus.ReadyToServe, now))
        {
            return ServiceResult.Failure("That order is no longer cooking. Refresh and try again.");
        }
        await _context.KitchenTickets.Where(t => t.OrderId == orderId).ExecuteUpdateAsync(s => s.SetProperty(t => t.ReadyAtUtc, now));
        await _notifications.NotifyUserAsync(order.CustomerId, NotificationType.OrderUpdated,
            "Your food is ready", $"Order #{orderId} is ready. Staff will bring it to your table.", "/Customer/Tracking");
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> AddCookingTimeAsync(int orderId, int minutes)
    {
        if (minutes < 1 || minutes > MaxExtraMinutes)
        {
            return ServiceResult.Failure($"Add between 1 and {MaxExtraMinutes} minutes.");
        }
        // Only a countdown that is still running can be extended.
        int changed = await _context.KitchenTickets
            .Where(t => t.OrderId == orderId && t.Order!.Status == OrderStatus.Preparing)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReadyAtUtc, t => t.ReadyAtUtc.AddMinutes(minutes))
                .SetProperty(t => t.CookingMinutes, t => t.CookingMinutes + minutes));
        if (changed == 0) { return ServiceResult.Failure("That order is no longer cooking. Refresh and try again."); }
        string customerId = await _context.Orders.Where(o => o.Id == orderId).Select(o => o.CustomerId).SingleAsync();
        await _notifications.NotifyUserAsync(customerId, NotificationType.OrderUpdated,
            "A little more time", $"Order #{orderId} needs about {minutes} more min. Thank you for waiting.", "/Customer/Tracking");
        return ServiceResult.Success();
    }

    public async Task<bool> IsPausedAsync() =>
        await _context.KitchenStatuses.Where(k => k.Id == KitchenStatus.SingletonId).Select(k => k.IsPaused).SingleOrDefaultAsync();

    public async Task SetPausedAsync(bool paused)
    {
        DateTime now = DateTime.UtcNow;
        int changed = await _context.KitchenStatuses.Where(k => k.Id == KitchenStatus.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.IsPaused, paused).SetProperty(k => k.ChangedAtUtc, now));
        if (changed == 0)
        {
            // Normally seeded by the migration; this covers a database created another way.
            _context.KitchenStatuses.Add(new KitchenStatus { Id = KitchenStatus.SingletonId, IsPaused = paused, ChangedAtUtc = now });
            await _context.SaveChangesAsync();
        }
    }

    public async Task<KitchenBoard> GetKitchenBoardAsync()
    {
        DateTime now = DateTime.UtcNow;
        var board = new KitchenBoard { NowUtc = now, CookingSlots = _slots, IsPaused = await IsPausedAsync() };
        List<Order> active = await _context.Orders.AsNoTracking()
            .Include(o => o.Reservation!).ThenInclude(r => r.DiningTable)
            .Include(o => o.Items).ThenInclude(i => i.MenuItem)
            .Where(o => (o.Status == OrderStatus.Placed && o.Reservation!.Status == ReservationStatus.Seated)
                || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.ReadyToServe || o.Status == OrderStatus.Served)
            .ToListAsync();
        List<int> ids = active.Select(o => o.Id).ToList();
        Dictionary<int, KitchenTicket> tickets = await _context.KitchenTickets.AsNoTracking()
            .Where(t => ids.Contains(t.OrderId)).ToDictionaryAsync(t => t.OrderId);

        foreach (Order order in active)
        {
            tickets.TryGetValue(order.Id, out KitchenTicket? ticket);
            var entry = new KitchenEntry
            {
                Order = order, CookingMinutes = ticket?.CookingMinutes ?? LongestDish(order), WaitingSinceUtc = InLineSince(order),
                StartUtc = ticket?.StartedAtUtc, ReadyAtUtc = ticket?.ReadyAtUtc
            };
            (order.Status switch
            {
                OrderStatus.Placed => board.Queue,
                OrderStatus.Preparing => board.Cooking,
                OrderStatus.ReadyToServe => board.Ready,
                _ => board.Served
            }).Add(entry);
        }
        board.Cooking = board.Cooking.OrderBy(e => e.ReadyAtUtc ?? now).ToList();
        board.Queue = board.Queue.OrderBy(e => e.WaitingSinceUtc).ThenBy(e => e.Order.Id).ToList();
        // Ready and served orders are grouped by table, oldest first, so staff can take one table's food together.
        board.Ready = board.Ready.OrderBy(e => e.TableLabel).ThenBy(e => e.Order.UpdatedAtUtc).ToList();
        board.Served = board.Served.OrderBy(e => e.TableLabel).ThenBy(e => e.Order.UpdatedAtUtc).ToList();

        // Estimate the queue by filling the slots in order: each order takes the slot that frees up first.
        var freeAt = board.Cooking.Select(e => e.ReadyAtUtc ?? now).Select(t => t < now ? now : t).ToList();
        while (freeAt.Count < _slots) { freeAt.Add(now); }
        int position = 1;
        foreach (KitchenEntry entry in board.Queue)
        {
            entry.QueuePosition = position++;
            if (entry.CookingMinutes == 0) { entry.StartUtc = now; entry.ReadyAtUtc = now; continue; }
            freeAt.Sort();
            DateTime start = freeAt[0];
            entry.StartUtc = start;
            entry.ReadyAtUtc = start.AddMinutes(entry.CookingMinutes);
            freeAt[0] = entry.ReadyAtUtc.Value;
        }
        return board;
    }

    /// <summary>Placed orders of seated guests, in kitchen order.</summary>
    private async Task<List<Order>> WaitingOrdersAsync()
    {
        List<Order> orders = await _context.Orders.AsNoTracking()
            .Include(o => o.Reservation).Include(o => o.Items).ThenInclude(i => i.MenuItem)
            .Where(o => o.Status == OrderStatus.Placed && o.Reservation!.Status == ReservationStatus.Seated)
            .ToListAsync();
        return orders.OrderBy(InLineSince).ThenBy(o => o.Id).ToList();
    }

    /// <summary>
    /// A pre-order joins the line when the visit starts (guests can only be seated during their visit),
    /// so a booking made days ago does not jump ahead of guests already waiting.
    /// </summary>
    private static DateTime InLineSince(Order order)
    {
        DateTime visit = order.Reservation?.StartUtc ?? order.PlacedAtUtc;
        return order.PlacedAtUtc > visit ? order.PlacedAtUtc : visit;
    }

    /// <summary>Dishes in one order cook side by side, so the order takes as long as its slowest dish.</summary>
    private static int LongestDish(Order order) =>
        order.Items.Select(i => i.MenuItem?.CookingMinutes ?? 0).DefaultIfEmpty(0).Max();

    private async Task<int> CookingMinutesAsync(int orderId) =>
        await _context.OrderItems.Where(i => i.OrderId == orderId).MaxAsync(i => i.MenuItem!.CookingMinutes) ?? 0;

    private async Task<bool> MoveAsync(int orderId, OrderStatus from, OrderStatus to, DateTime now)
    {
        return await _context.Orders.Where(o => o.Id == orderId && o.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, to).SetProperty(o => o.UpdatedAtUtc, now)) == 1;
    }

    private async Task AddTicketAsync(int orderId, int minutes, DateTime now, DateTime queuedSince)
    {
        // A ticket left from an earlier start is replaced, so the countdown always matches the current cooking.
        await _context.KitchenTickets.Where(t => t.OrderId == orderId).ExecuteDeleteAsync();
        _context.KitchenTickets.Add(new KitchenTicket { OrderId = orderId, CookingMinutes = minutes, StartedAtUtc = now,
            ReadyAtUtc = now.AddMinutes(minutes), QueuedSinceUtc = queuedSince < now ? queuedSince : now });
        await _context.SaveChangesAsync();
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
