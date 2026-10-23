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
    public const string NothingToPayMessage = "Every order of this visit is already paid.";
    public const string VisitNotReadyMessage = "Every order of this visit must be completed by the kitchen before the visit can be paid.";
    public const string NotificationWarning = "Payment was saved, but its confirmation notification could not be sent. Do not record the payment again.";

    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;
    private readonly ILogger<BillingService> _logger;
    private readonly IReservationService? _reservations;

    /// <summary>Receives the shared database, notification publisher and logger.</summary>
    /// <param name="reservations">
    /// WL-3: completes the visit once its last order is paid. Optional so existing tests can build the service without it.
    /// </param>
    public BillingService(ApplicationDbContext context, INotificationService notifications, ILogger<BillingService> logger,
        IReservationService? reservations = null)
    {
        _context = context;
        _notifications = notifications;
        _logger = logger;
        _reservations = reservations;
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
                ReservationId = order.ReservationId,
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

        if (_reservations != null)
        {
            try
            {
                // WL-3: the last paid bill ends the visit, so staff do not have to press Complete.
                await _reservations.CompleteAfterPaymentAsync(order.ReservationId);
            }
            catch (Exception exception)
            {
                // The payment is already saved; staff can still complete the visit by hand.
                _logger.LogError(exception, "Payment {PaymentId} was saved but its visit could not be completed.", payment.Id);
            }
        }

        return ServiceResult<PaymentRecordingResult>.Success(new PaymentRecordingResult
        {
            PaymentId = payment.Id,
            NotificationSent = notificationSent
        });
    }

    /// <inheritdoc />
    public async Task<VisitBill?> GetVisitBillAsync(int reservationId)
    {
        var visit = await _context.Reservations.AsNoTracking()
            .Where(reservation => reservation.Id == reservationId)
            .Select(reservation => new
            {
                reservation.Id,
                CustomerName = reservation.Customer!.FullName,
                TableLabel = reservation.DiningTable!.Label
            }).SingleOrDefaultAsync();
        if (visit == null)
        {
            return null;
        }
        List<Order> orders = await OrdersWithDetails()
            .Where(order => order.ReservationId == reservationId && order.Status != OrderStatus.Cancelled
                && !_context.Payments.Any(payment => payment.OrderId == order.Id && payment.Status == PaymentStatus.Paid))
            .OrderBy(order => order.Id)
            .ToListAsync();
        return new VisitBill
        {
            ReservationId = visit.Id,
            CustomerName = visit.CustomerName,
            TableLabel = visit.TableLabel,
            UnpaidOrders = orders.Select(MapBill).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PaymentRecordingResult>> RecordVisitPaymentAsync(int reservationId, PaymentMethod method, string staffId)
    {
        if (!Enum.IsDefined(method))
        {
            return ServiceResult<PaymentRecordingResult>.Failure(InvalidMethodMessage);
        }
        if (!await IsActiveStaffAsync(staffId))
        {
            return ServiceResult<PaymentRecordingResult>.Failure(InvalidStaffMessage);
        }
        List<Order> unpaid = await _context.Orders.AsNoTracking()
            .Where(order => order.ReservationId == reservationId && order.Status != OrderStatus.Cancelled
                && !_context.Payments.Any(payment => payment.OrderId == order.Id && payment.Status == PaymentStatus.Paid))
            .OrderBy(order => order.Id)
            .ToListAsync();
        if (unpaid.Count == 0)
        {
            return ServiceResult<PaymentRecordingResult>.Failure(NothingToPayMessage);
        }
        if (unpaid.Any(order => order.Status != OrderStatus.Completed))
        {
            return ServiceResult<PaymentRecordingResult>.Failure(VisitNotReadyMessage);
        }

        // One Payment row per order keeps the existing rules (one Paid record per order, saved order total).
        // They share one timestamp, staff and method, which is how the receipt finds them together.
        DateTime paidAtUtc = DateTime.UtcNow;
        List<Payment> payments = unpaid.Select(order => new Payment
        {
            OrderId = order.Id,
            Amount = order.TotalAmount,
            Method = method,
            Status = PaymentStatus.Paid,
            StaffId = staffId,
            PaidAtUtc = paidAtUtc
        }).ToList();
        _context.Payments.AddRange(payments);
        try
        {
            // One SaveChanges = one transaction: either every order of the visit is paid, or none is.
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsDuplicatePaidOrder(exception))
        {
            foreach (Payment payment in payments)
            {
                _context.Entry(payment).State = EntityState.Detached;
            }
            return ServiceResult<PaymentRecordingResult>.Failure(AlreadyPaidMessage);
        }

        int receiptId = payments[0].Id;
        bool notificationSent = true;
        try
        {
            decimal total = payments.Sum(payment => payment.Amount);
            string message = FormattableString.Invariant(
                $"Payment of RM {total:F2} for {payments.Count} order(s) of your visit is recorded.");
            if (method == PaymentMethod.SimulatedOnline)
            {
                message += " Demo only - no real payment processed.";
            }
            await _notifications.NotifyUserAsync(unpaid[0].CustomerId, NotificationType.PaymentConfirmed,
                "Payment confirmed", message, $"/Customer/Receipts/Details/{receiptId}");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Visit payment {PaymentId} was saved but its confirmation failed.", receiptId);
            notificationSent = false;
        }

        if (_reservations != null)
        {
            try
            {
                // WL-3: everything is paid now, so the visit completes by itself.
                await _reservations.CompleteAfterPaymentAsync(reservationId);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Visit payment {PaymentId} was saved but its visit could not be completed.", receiptId);
            }
        }

        return ServiceResult<PaymentRecordingResult>.Success(new PaymentRecordingResult
        {
            PaymentId = receiptId,
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

        // A whole-visit payment saved one Payment per order with the same time, staff and method:
        // collect them so the customer sees one receipt for the visit.
        List<Payment> together = await _context.Payments.AsNoTracking()
            .Where(other => other.Status == PaymentStatus.Paid && other.PaidAtUtc == payment.PaidAtUtc
                && other.StaffId == payment.StaffId && other.Method == payment.Method
                && other.Order!.ReservationId == order.ReservationId)
            .OrderBy(other => other.OrderId)
            .ToListAsync();
        List<int> orderIds = together.Select(other => other.OrderId).ToList();
        List<Order> orders = await OrdersWithDetails().Where(other => orderIds.Contains(other.Id))
            .OrderBy(other => other.Id).ToListAsync();
        List<BillSummary> bills = orders.Select(MapBill).ToList();
        if (bills.Count == 0)
        {
            bills.Add(bill);
        }

        return new ReceiptSummary
        {
            PaymentId = payment.Id,
            Amount = payment.Amount,
            Method = payment.Method,
            PaidAtUtc = payment.PaidAtUtc,
            StaffName = payment.Staff!.FullName,
            Bill = bill,
            Bills = bills,
            TotalPaid = together.Count == 0 ? payment.Amount : together.Sum(other => other.Amount)
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
            ReservationId = order.ReservationId,
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

    /// <summary>True for an active Staff or Administrator account (same rule as RecordPaymentAsync).</summary>
    private Task<bool> IsActiveStaffAsync(string staffId)
    {
        return (from user in _context.Users
                join userRole in _context.UserRoles on user.Id equals userRole.UserId
                join role in _context.Roles on userRole.RoleId equals role.Id
                where user.Id == staffId && user.IsActive
                    && (role.Name == RoleNames.Staff || role.Name == RoleNames.Administrator)
                select user.Id).AnyAsync();
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
