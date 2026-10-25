using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;

namespace Rroms.Web.Services;

/// <summary>
/// Implements <see cref="IOrderService"/>: the business rules for placing orders (YJ-3).
/// </summary>
/// <remarks>
/// The server never trusts the browser: the request carries no prices, so every price and every
/// availability decision is read from the database here. The order and its lines are added to the context
/// and saved with ONE SaveChanges call, which EF Core runs as a single database transaction: either the
/// whole order is saved, or nothing is.
/// </remarks>
public class OrderService : IOrderService
{
    /// <summary>Error shown for a reservation that does not exist or belongs to someone else.</summary>
    public const string ReservationNotFoundMessage = "Reservation not found.";

    /// <summary>Error shown when the customer's own reservation is not Confirmed or Seated.</summary>
    public const string ReservationNotOrderableMessage = "Orders can only be placed for a confirmed reservation, or once you are seated.";

    /// <summary>Error shown when the order has no dishes.</summary>
    public const string EmptyOrderMessage = "Choose at least one dish.";

    /// <summary>Error shown when a requested dish does not exist any more.</summary>
    public const string UnknownDishMessage = "One of the dishes is no longer on the menu.";

    /// <summary>Error shown when a note is longer than the database allows.</summary>
    public const string NotesTooLongMessage = "A note is too long. Please shorten it.";

    private const string NoteSeparator = "; ";

    /// <summary>The reservation statuses that can take an order. ONE rule, used by every method below.</summary>
    private static readonly ReservationStatus[] OrderableStatuses = { ReservationStatus.Confirmed, ReservationStatus.Seated };

    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly OrderRulesOptions _rules;
    private readonly BillingCalculator _billing;

    /// <summary>Receives the database, the notification channel and the ordering limits.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="notificationService">Tells the customer and the staff that an order was placed.</param>
    /// <param name="options">The ordering limits from appsettings.json (or their defaults).</param>
    /// <param name="billing">Configured calculator for new order charge snapshots.</param>
    public OrderService(ApplicationDbContext context, INotificationService notificationService, IOptions<OrderRulesOptions> options, BillingCalculator? billing = null)
    {
        _context = context;
        _notificationService = notificationService;
        _rules = options.Value;
        _billing = billing ?? new BillingCalculator(Microsoft.Extensions.Options.Options.Create(new BillingOptions()));
    }

    /// <summary>The error shown when a quantity is outside 1 to the limit.</summary>
    /// <param name="maxQuantity">The largest allowed quantity.</param>
    public static string QuantityOutOfRangeMessage(int maxQuantity)
    {
        return $"Each dish must be ordered in a quantity from 1 to {maxQuantity}.";
    }

    /// <summary>The error shown when a dish cannot be ordered right now.</summary>
    /// <param name="dishName">The dish's name.</param>
    public static string DishUnavailableMessage(string dishName)
    {
        return $"{dishName} is not available right now. Remove it from your order.";
    }

    /// <inheritdoc />
    public async Task<ServiceResult<OrderableReservation>> GetOrderableReservationAsync(string customerId, int reservationId)
    {
        // Filtering by customer in the query means a booking that belongs to someone else is simply "not found".
        Reservation? reservation = await _context.Set<Reservation>()
            .Include(r => r.DiningTable)
                .ThenInclude(table => table!.DiningArea)
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.CustomerId == customerId);

        if (reservation == null)
        {
            return ServiceResult<OrderableReservation>.Failure(ReservationNotFoundMessage);
        }

        // TODO(YJ): when WL's CanOrderForReservationAsync is on main, this one rule can move there.
        bool canOrder = OrderableStatuses.Contains(reservation.Status);

        if (!canOrder)
        {
            return ServiceResult<OrderableReservation>.Failure(ReservationNotOrderableMessage);
        }

        OrderableReservation orderable = new OrderableReservation
        {
            Id = reservation.Id,
            TableLabel = reservation.DiningTable?.Label ?? string.Empty,
            AreaName = reservation.DiningTable?.DiningArea?.Name ?? string.Empty,
            StartUtc = reservation.StartUtc,
            PartySize = reservation.PartySize,
            Status = reservation.Status
        };

        return ServiceResult<OrderableReservation>.Success(orderable);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderableReservation>> GetOrderableReservationsAsync(string customerId)
    {
        List<Reservation> reservations = await _context.Set<Reservation>()
            .Include(r => r.DiningTable)
                .ThenInclude(table => table!.DiningArea)
            .Where(r => r.CustomerId == customerId && OrderableStatuses.Contains(r.Status))
            .ToListAsync();

        // Seated first (they are eating now), then by visit date.
        return reservations
            .OrderBy(r => r.Status == ReservationStatus.Seated ? 0 : 1)
            .ThenBy(r => r.StartUtc)
            .Select(r => new OrderableReservation
            {
                Id = r.Id,
                TableLabel = r.DiningTable?.Label ?? string.Empty,
                AreaName = r.DiningTable?.DiningArea?.Name ?? string.Empty,
                StartUtc = r.StartUtc,
                PartySize = r.PartySize,
                Status = r.Status
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>>> GetOrderedQuantitiesAsync(string customerId)
    {
        var rows = await _context.Orders
            .Where(o => o.CustomerId == customerId && o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items.Select(i => new { o.ReservationId, i.MenuItemId, i.Quantity }))
            .ToListAsync();

        Dictionary<int, IReadOnlyDictionary<int, int>> result = new Dictionary<int, IReadOnlyDictionary<int, int>>();

        foreach (var reservationRows in rows.GroupBy(row => row.ReservationId))
        {
            result[reservationRows.Key] = reservationRows
                .GroupBy(row => row.MenuItemId)
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Quantity));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<int>> PlaceOrderAsync(string customerId, PlaceOrderRequest request)
    {
        // 1. Whose reservation, and may it order?
        ServiceResult<OrderableReservation> reservationResult =
            await GetOrderableReservationAsync(customerId, request.ReservationId);
        OrderableReservation? reservation = reservationResult.Value;

        if (!reservationResult.Succeeded || reservation == null)
        {
            return ServiceResult<int>.Failure(reservationResult.Errors.ToArray());
        }

        // 2. Is the order itself sensible? (No dishes, quantities, notes.)
        List<string> errors = new List<string>();

        if (request.Lines.Count == 0)
        {
            errors.Add(EmptyOrderMessage);
        }

        bool quantityOutOfRange = request.Lines.Any(
            line => line.Quantity < 1 || line.Quantity > _rules.MaxQuantityPerLine);

        if (quantityOutOfRange)
        {
            errors.Add(QuantityOutOfRangeMessage(_rules.MaxQuantityPerLine));
        }

        string? orderNotes = CleanNotes(request.Notes);
        bool orderNotesTooLong = orderNotes != null && orderNotes.Length > Order.NotesMaxLength;

        if (orderNotesTooLong)
        {
            errors.Add(NotesTooLongMessage);
        }

        if (errors.Count > 0)
        {
            return ServiceResult<int>.Failure(errors.ToArray());
        }

        // 3. The same dish on two lines becomes one line; check the merged quantities and notes again.
        List<MergedLine> lines = MergeLines(request.Lines);

        if (lines.Any(line => line.Quantity > _rules.MaxQuantityPerLine))
        {
            errors.Add(QuantityOutOfRangeMessage(_rules.MaxQuantityPerLine));
        }

        if (lines.Any(line => line.Notes != null && line.Notes.Length > OrderItem.NotesMaxLength))
        {
            errors.Add(NotesTooLongMessage);
        }

        if (errors.Count > 0)
        {
            return ServiceResult<int>.Failure(errors.ToArray());
        }

        // 4. Read the dishes from the database. Nothing about a dish is taken from the browser except its Id.
        List<int> dishIds = lines.Select(line => line.MenuItemId).ToList();
        List<MenuItem> dishes = await _context.MenuItems
            .Include(dish => dish.MenuCategory)
            .Where(dish => dishIds.Contains(dish.Id))
            .ToListAsync();

        foreach (MergedLine line in lines)
        {
            MenuItem? dish = dishes.FirstOrDefault(d => d.Id == line.MenuItemId);

            if (dish == null)
            {
                errors.Add(UnknownDishMessage);
            }
            else if (!dish.IsAvailable || dish.MenuCategory == null || !dish.MenuCategory.IsActive)
            {
                errors.Add(DishUnavailableMessage(dish.Name));
            }
        }

        if (errors.Count > 0)
        {
            // One bad dish refuses the whole order: nothing has been added to the database yet.
            return ServiceResult<int>.Failure(errors.Distinct().ToArray());
        }

        // 5. Build the order. Each line copies the price the customer pays RIGHT NOW (an offer price while an
        //    offer runs); the total is added up here, once, by the server.
        DateTime now = DateTime.UtcNow;
        DiscountSet discounts = await DiscountSet.LoadAsync(_context);

        Order order = new Order
        {
            ReservationId = reservation.Id,
            CustomerId = customerId,
            Status = OrderStatus.Placed,
            Notes = orderNotes,
            PlacedAtUtc = now,
            UpdatedAtUtc = now
        };

        decimal total = 0m;
        int portions = 0;

        foreach (MergedLine line in lines)
        {
            MenuItem dish = dishes.Single(d => d.Id == line.MenuItemId);
            decimal unitPrice = MenuItemPricing.Quote(dish, now, discounts).Price;

            order.Items.Add(new OrderItem
            {
                MenuItemId = dish.Id,
                Quantity = line.Quantity,
                UnitPrice = unitPrice,
                Notes = line.Notes
            });

            total += unitPrice * line.Quantity;
            portions += line.Quantity;
        }

        var charges = _billing.Calculate(total);
        if (charges.Total > 99999999.99m) return ServiceResult<int>.Failure("Order total is too large.");
        order.TotalAmount = charges.Total;
        order.Subtotal = charges.Subtotal;
        order.ServiceChargePercent = charges.ServiceChargePercent;
        order.ServiceCharge = charges.ServiceCharge;
        order.ServiceTaxPercent = charges.ServiceTaxPercent;
        order.ServiceTax = charges.ServiceTax;
        order.TaxIncludesServiceCharge = charges.TaxIncludesServiceCharge;


        // 6. Save the order AND its lines together. One SaveChanges = one transaction: all or nothing.
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        // 7. Tell people only after the order is safely saved.
        await NotifyAsync(order, reservation, portions);

        return ServiceResult<int>.Success(order.Id);
    }

    /// <inheritdoc />
    public async Task<OrderSummary?> GetOrderForCustomerAsync(string customerId, int orderId)
    {
        // Filtering by customer means another customer's order is simply "not found".
        Order? order = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(item => item.MenuItem)
            .Include(o => o.Reservation)
                .ThenInclude(reservation => reservation!.DiningTable)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId);

        if (order == null)
        {
            return null;
        }

        OrderSummary summary = new OrderSummary
        {
            Id = order.Id,
            ReservationId = order.ReservationId,
            TableLabel = order.Reservation?.DiningTable?.Label ?? string.Empty,
            Status = order.Status,
            Breakdown = BillingCalculator.FromOrder(order),
            TotalAmount = order.TotalAmount,
            Notes = order.Notes,
            PlacedAtUtc = order.PlacedAtUtc
        };

        foreach (OrderItem item in order.Items.OrderBy(i => i.Id))
        {
            summary.Lines.Add(new OrderSummaryLine
            {
                MenuItemId = item.MenuItemId,
                DishName = item.MenuItem?.Name ?? string.Empty,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = item.LineTotal,
                Notes = item.Notes
            });
        }

        return summary;
    }

    /// <summary>Tells the customer their order was placed and the staff that a new order arrived.</summary>
    private async Task NotifyAsync(Order order, OrderableReservation reservation, int portions)
    {
        string whenPrepared = "It will be prepared once you are seated.";

        if (reservation.Status == ReservationStatus.Seated)
        {
            whenPrepared = "The kitchen will start on it shortly.";
        }

        string customerMessage = $"Your order of RM {order.TotalAmount:N2} for table {reservation.TableLabel} has been placed. {whenPrepared}";
        string staffMessage = $"New order for table {reservation.TableLabel}: {portions} portion(s), RM {order.TotalAmount:N2}.";

        await _notificationService.NotifyUserAsync(
            order.CustomerId,
            NotificationType.OrderUpdated,
            "Order placed",
            customerMessage,
            $"/Customer/Orders/Details/{order.Id}");

        await _notificationService.NotifyRoleAsync(
            RoleNames.Staff,
            NotificationType.OrderUpdated,
            "New order",
            staffMessage);
    }

    /// <summary>Trims a note and turns an empty one into null.</summary>
    private static string? CleanNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        return notes.Trim();
    }

    /// <summary>Joins two notes for the same dish, ignoring empty ones.</summary>
    private static string? JoinNotes(string? first, string? second)
    {
        if (first == null)
        {
            return second;
        }

        if (second == null)
        {
            return first;
        }

        return first + NoteSeparator + second;
    }

    /// <summary>Turns the requested lines into one line per dish, in the order the dishes first appear.</summary>
    private static List<MergedLine> MergeLines(IEnumerable<PlaceOrderLine> requested)
    {
        List<MergedLine> merged = new List<MergedLine>();

        foreach (PlaceOrderLine line in requested)
        {
            string? note = CleanNotes(line.Notes);
            MergedLine? existing = merged.FirstOrDefault(m => m.MenuItemId == line.MenuItemId);

            if (existing == null)
            {
                merged.Add(new MergedLine { MenuItemId = line.MenuItemId, Quantity = line.Quantity, Notes = note });
            }
            else
            {
                existing.Quantity += line.Quantity;
                existing.Notes = JoinNotes(existing.Notes, note);
            }
        }

        return merged;
    }

    /// <summary>One dish after duplicate lines were merged.</summary>
    private class MergedLine
    {
        public int MenuItemId { get; set; }

        public int Quantity { get; set; }

        public string? Notes { get; set; }
    }
}