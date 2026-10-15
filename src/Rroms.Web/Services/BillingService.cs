using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>Records a completed order's saved total and protects against repeated payment requests.</summary>
public class BillingService : IBillingService
{
    public const string MissingOrderMessage = "Order not found.";
    public const string NotCompletedMessage = "Only completed orders can be paid.";
    public const string AlreadyPaidMessage = "This order has already been paid. Open its receipt instead.";
    public const string InvalidMethodMessage = "Choose a valid payment method.";
    public const string InvalidStaffMessage = "An active staff or administrator account is required.";
    public const string NotificationWarning = "Payment was saved, but its confirmation notification could not be sent. Do not record the payment again.";

    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;
    private readonly ILogger<BillingService> _logger;

    /// <summary>Receives the shared database, notification publisher and logger.</summary>
    public BillingService(ApplicationDbContext context, INotificationService notifications, ILogger<BillingService> logger)
    {
        _context = context;
        _notifications = notifications;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BillSummary>> GetCompletedOrdersAsync(bool includePaid = false)
    {
        IQueryable<Order> query = _context.Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatus.Completed);
        if (!includePaid)
        {
            query = query.Where(order => !_context.Payments.Any(payment =>
                payment.OrderId == order.Id && payment.Status == PaymentStatus.Paid));
        }
        return await query.OrderByDescending(order => order.PlacedAtUtc).ThenByDescending(order => order.Id)
            .Select(order => new BillSummary
            {
                OrderId = order.Id,
                CustomerName = order.Customer!.FullName,
                TableLabel = order.Reservation!.DiningTable!.Label,
                TotalAmount = order.TotalAmount,
                Status = order.Status,
                PlacedAtUtc = order.PlacedAtUtc,
                PaidPaymentId = _context.Payments.Where(payment =>
                    payment.OrderId == order.Id && payment.Status == PaymentStatus.Paid)
                    .Select(payment => (int?)payment.Id).FirstOrDefault()
            }).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<BillSummary?> GetBillAsync(int orderId)
    {
        Order? order = await OrdersWithDetails().SingleOrDefaultAsync(order => order.Id == orderId);
        if (order == null)
        {
            return null;
        }
        BillSummary bill = MapBill(order);
        bill.PaidPaymentId = await _context.Payments.AsNoTracking()
            .Where(payment => payment.OrderId == orderId && payment.Status == PaymentStatus.Paid)
            .Select(payment => (int?)payment.Id).SingleOrDefaultAsync();
        return bill;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PaymentRecordingResult>> RecordPaymentAsync(int orderId, PaymentMethod method, string staffId)
    {
        if (!Enum.IsDefined(method))
        {
            return ServiceResult<PaymentRecordingResult>.Failure(InvalidMethodMessage);
        }
        bool isStaff = await (from user in _context.Users
                             join userRole in _context.UserRoles on user.Id equals userRole.UserId
                             join role in _context.Roles on userRole.RoleId equals role.Id
                             where user.Id == staffId && user.IsActive
                                 && (role.Name == RoleNames.Staff || role.Name == RoleNames.Administrator)
                             select user.Id).AnyAsync();
        if (!isStaff)
        {
            return ServiceResult<PaymentRecordingResult>.Failure(InvalidStaffMessage);
        }
        Order? order = await _context.Orders.AsNoTracking().SingleOrDefaultAsync(order => order.Id == orderId);
        if (order == null)
        {
            return ServiceResult<PaymentRecordingResult>.Failure(MissingOrderMessage);
        }
        if (order.Status != OrderStatus.Completed)
        {
            return ServiceResult<PaymentRecordingResult>.Failure(NotCompletedMessage);
        }
        if (await _context.Payments.AnyAsync(payment => payment.OrderId == orderId && payment.Status == PaymentStatus.Paid))
        {
            return ServiceResult<PaymentRecordingResult>.Failure(AlreadyPaidMessage);
        }
        // Completed is terminal in Alex's workflow, and YJ fixes TotalAmount when placing the order.
        // One SaveChanges is atomic; no manual transaction (or retry wrapper) is needed here.
        var payment = new Payment
        {
            OrderId = order.Id,
            Amount = order.TotalAmount,
            Method = method,
            Status = PaymentStatus.Paid,
            StaffId = staffId,
            PaidAtUtc = DateTime.UtcNow
        };
        _context.Payments.Add(payment);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsDuplicatePaidOrder(exception))
        {
            // A second request may win after our pre-check. Detach the failed insert before any later save.
            _context.Entry(payment).State = EntityState.Detached;
            return ServiceResult<PaymentRecordingResult>.Failure(AlreadyPaidMessage);
        }

        bool notificationSent = true;
        try
        {
            string message = FormattableString.Invariant($"Payment of RM {payment.Amount:F2} for order #{order.Id} is recorded.");
            if (method == PaymentMethod.SimulatedOnline)
            {
                message += " Demo only - no real payment processed.";
            }
            // YY's publisher saves on this same DbContext, so it must run AFTER our payment is saved.
            await _notifications.NotifyUserAsync(order.CustomerId, NotificationType.PaymentConfirmed,
                "Payment confirmed", message, $"/Customer/Receipts/Details/{payment.Id}");
        }
        catch (Exception exception)
        {
            // Never ask staff to charge again merely because an already-paid order's notification failed.
            _logger.LogError(exception, "Payment {PaymentId} was saved but its confirmation failed.", payment.Id);
            notificationSent = false;
        }
        return ServiceResult<PaymentRecordingResult>.Success(new PaymentRecordingResult
        {
            PaymentId = payment.Id,
            NotificationSent = notificationSent
        });
    }

    /// <inheritdoc />
    public async Task<ReceiptSummary?> GetReceiptForStaffAsync(int paymentId)
    {
        return await ReadReceiptAsync(paymentId, null);
    }

    /// <inheritdoc />
    public async Task<ReceiptSummary?> GetReceiptForCustomerAsync(int paymentId, string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return null;
        }
        return await ReadReceiptAsync(paymentId, customerId);
    }

    private async Task<ReceiptSummary?> ReadReceiptAsync(int paymentId, string? customerId)
    {
        IQueryable<Payment> query = _context.Payments.AsNoTracking()
            .Where(payment => payment.Id == paymentId && payment.Status == PaymentStatus.Paid);
        if (customerId != null)
        {
            query = query.Where(payment => payment.Order!.CustomerId == customerId);
        }
        Payment? payment = await query.Include(payment => payment.Staff).SingleOrDefaultAsync();
        if (payment == null)
        {
            return null;
        }
        Order order = await OrdersWithDetails().SingleAsync(order => order.Id == payment.OrderId);
        BillSummary bill = MapBill(order);
        bill.PaidPaymentId = payment.Id;
        return new ReceiptSummary
        {
            PaymentId = payment.Id,
            Amount = payment.Amount,
            Method = payment.Method,
            PaidAtUtc = payment.PaidAtUtc,
            StaffName = payment.Staff!.FullName,
            Bill = bill
        };
    }

    private IQueryable<Order> OrdersWithDetails()
    {
        return _context.Orders.AsNoTracking().Include(order => order.Customer)
            .Include(order => order.Reservation!).ThenInclude(reservation => reservation.DiningTable)
            .Include(order => order.Items).ThenInclude(line => line.MenuItem);
    }

    private static BillSummary MapBill(Order order)
    {
        var bill = new BillSummary
        {
            OrderId = order.Id,
            CustomerName = order.Customer!.FullName,
            TableLabel = order.Reservation!.DiningTable!.Label,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            PlacedAtUtc = order.PlacedAtUtc
        };
        foreach (OrderItem line in order.Items.OrderBy(line => line.Id))
        {
            bill.Lines.Add(new BillLine
            {
                DishName = line.MenuItem!.Name,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice
            });
        }
        return bill;
    }

    private static bool IsDuplicatePaidOrder(DbUpdateException exception)
    {
        if (exception.InnerException is SqlException sqlException)
        {
            const int DuplicateIndex = 2601;
            const int DuplicateConstraint = 2627;
            return (sqlException.Number == DuplicateIndex || sqlException.Number == DuplicateConstraint)
                && sqlException.Message.Contains(PaymentConfiguration.PaidOrderIndex, StringComparison.Ordinal);
        }
        // The team's integration tests use SQLite; match only this unique constraint, not other DB failures.
        return exception.InnerException?.Message.Contains(
            "UNIQUE constraint failed: Payments.OrderId", StringComparison.Ordinal) == true;
    }
}
