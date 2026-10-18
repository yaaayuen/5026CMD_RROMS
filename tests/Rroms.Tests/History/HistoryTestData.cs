using Rroms.Tests.Menu;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.History;

/// <summary>
/// Builds reservations, orders and payments with chosen times and statuses for the customer history tests.
/// Works on top of <see cref="OrderTestData"/> and changes nothing in it. Owner: YJ.
/// </summary>
public static class HistoryTestData
{
    private const int PartySize = 4;
    private const int VisitMinutes = 120;

    /// <summary>Saves a staff user, who records the payments (a payment must name who recorded it).</summary>
    /// <param name="context">The test database.</param>
    public static async Task<ApplicationUser> AddStaffAsync(ApplicationDbContext context)
    {
        string email = $"staff-{Guid.NewGuid():N}@threegrills.test";

        var staff = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Test Staff"
        };

        context.Users.Add(staff);
        await context.SaveChangesAsync();

        return staff;
    }

    /// <summary>Saves a reservation for a customer, starting at the given time, at the seeded table T01.</summary>
    /// <param name="context">The test database.</param>
    /// <param name="customerId">The customer who booked.</param>
    /// <param name="startUtc">When the visit starts.</param>
    /// <param name="status">The reservation's status.</param>
    public static async Task<Reservation> AddReservationAsync(
        ApplicationDbContext context,
        string customerId,
        DateTime startUtc,
        ReservationStatus status = ReservationStatus.Confirmed)
    {
        var reservation = new Reservation
        {
            CustomerId = customerId,
            DiningTableId = OrderTestData.SeededTableId,
            PartySize = PartySize,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(VisitMinutes),
            Status = status,
            CreatedAtUtc = startUtc,
            UpdatedAtUtc = startUtc
        };

        context.Set<Reservation>().Add(reservation);
        await context.SaveChangesAsync();

        return reservation;
    }

    /// <summary>Saves an order (on its own new reservation, booked by a throw-away customer) with a chosen time, status and total.</summary>
    /// <param name="context">The test database.</param>
    /// <param name="customerId">The customer who ordered.</param>
    /// <param name="placedAtUtc">When the order was placed.</param>
    /// <param name="status">The order's status.</param>
    /// <param name="total">The saved total in RM.</param>
    public static async Task<Order> AddOrderAsync(
        ApplicationDbContext context,
        string customerId,
        DateTime placedAtUtc,
        OrderStatus status = OrderStatus.Placed,
        decimal total = 40.00m)
    {
        // An order needs a reservation to hang on. It is booked under a throw-away customer, so these helper
        // reservations never show up in the real customer's "My reservations" list.
        ApplicationUser host = await OrderTestData.AddCustomerAsync(context);
        Reservation reservation = await OrderTestData.AddReservationAsync(context, host.Id);

        var order = new Order
        {
            ReservationId = reservation.Id,
            CustomerId = customerId,
            Status = status,
            TotalAmount = total,
            PlacedAtUtc = placedAtUtc,
            UpdatedAtUtc = placedAtUtc
        };

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order;
    }

    /// <summary>Saves a payment for an order, recorded by a staff member at the given time.</summary>
    /// <param name="context">The test database.</param>
    /// <param name="order">The order that was paid.</param>
    /// <param name="staffId">The staff member who recorded it.</param>
    /// <param name="paidAtUtc">When it was recorded.</param>
    /// <param name="status">The payment's status. Only Paid payments belong in a customer's history.</param>
    /// <param name="method">How it was paid.</param>
    public static async Task<Payment> AddPaymentAsync(
        ApplicationDbContext context,
        Order order,
        string staffId,
        DateTime paidAtUtc,
        PaymentStatus status = PaymentStatus.Paid,
        PaymentMethod method = PaymentMethod.Cash)
    {
        var payment = new Payment
        {
            OrderId = order.Id,
            Amount = order.TotalAmount,
            Method = method,
            Status = status,
            StaffId = staffId,
            PaidAtUtc = paidAtUtc
        };

        context.Set<Payment>().Add(payment);
        await context.SaveChangesAsync();

        return payment;
    }
}
