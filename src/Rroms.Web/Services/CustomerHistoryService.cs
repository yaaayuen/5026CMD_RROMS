using Microsoft.EntityFrameworkCore;

using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Implements <see cref="ICustomerHistoryService"/>: the customer's own reservations, orders and payments. Owner: YJ (YJ-5).
/// </summary>
/// <remarks>
/// <para>
/// Every query starts with the customer's Id, so only that customer's records can come back. The page never
/// filters anything itself.
/// </para>
/// <para>
/// A payment has no customer of its own; it belongs to an order, and the order belongs to the customer. So
/// payments are found through <c>Payment.Order.CustomerId</c>. Only Paid payments are listed: an unpaid or
/// voided record is not money the customer paid. Darren's billing code is only read, never changed.
/// </para>
/// <para>
/// The sort puts the newest first; when two rows share a time the higher Id (the later one saved) comes first,
/// so the order is always the same. Each section is cut to <c>take</c> rows in the database query.
/// </para>
/// </remarks>
public class CustomerHistoryService : ICustomerHistoryService
{
    private readonly ApplicationDbContext _context;

    /// <summary>Receives the database from dependency injection.</summary>
    /// <param name="context">The database context.</param>
    public CustomerHistoryService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<CustomerHistory> GetHistoryAsync(string customerId, int take = ICustomerHistoryService.DefaultTake)
    {
        if (take <= 0 || string.IsNullOrEmpty(customerId))
        {
            return new CustomerHistory();
        }

        List<HistoryReservationRow> reservations = await _context.Set<Reservation>()
            .AsNoTracking()
            .Where(reservation => reservation.CustomerId == customerId)
            .OrderByDescending(reservation => reservation.StartUtc)
            .ThenByDescending(reservation => reservation.Id)
            .Take(take)
            .Select(reservation => new HistoryReservationRow
            {
                Id = reservation.Id,
                StartUtc = reservation.StartUtc,
                TableLabel = reservation.DiningTable!.Label,
                AreaName = reservation.DiningTable!.DiningArea!.Name,
                PartySize = reservation.PartySize,
                Status = reservation.Status
            })
            .ToListAsync();

        List<HistoryOrderRow> orders = await _context.Orders
            .AsNoTracking()
            .Where(order => order.CustomerId == customerId)
            .OrderByDescending(order => order.PlacedAtUtc)
            .ThenByDescending(order => order.Id)
            .Take(take)
            .Select(order => new HistoryOrderRow
            {
                Id = order.Id,
                PlacedAtUtc = order.PlacedAtUtc,
                TotalAmount = order.TotalAmount,
                Status = order.Status
            })
            .ToListAsync();

        List<HistoryPaymentRow> payments = await _context.Set<Payment>()
            .AsNoTracking()
            .Where(payment => payment.Order!.CustomerId == customerId && payment.Status == PaymentStatus.Paid)
            .OrderByDescending(payment => payment.PaidAtUtc)
            .ThenByDescending(payment => payment.Id)
            .Take(take)
            .Select(payment => new HistoryPaymentRow
            {
                ReceiptNumber = payment.Id,
                PaidAtUtc = payment.PaidAtUtc,
                Amount = payment.Amount,
                Method = payment.Method,
                OrderId = payment.OrderId
            })
            .ToListAsync();

        return new CustomerHistory
        {
            Reservations = reservations,
            Orders = orders,
            Payments = payments
        };
    }
}
