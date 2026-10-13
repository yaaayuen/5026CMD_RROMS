using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using System.Linq.Expressions;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;

namespace Rroms.Web.Services;

/// <summary>
/// Table availability search and booking (WL-2). See <see cref="IReservationService"/> for what each method does.
/// </summary>
/// <remarks>
/// Opening hours, closures and bookable tables come from <see cref="IRestaurantService"/> (WL-1),
/// and every time calculation comes from <see cref="ReservationTimeRules"/>, so no rule is written twice.
/// Double booking is checked twice: once here in C# (for a clear message in the normal case) and once by
/// the unique index on ReservationSlots (the guarantee when two requests at the same moment).
/// </remarks>
public class ReservationService : IReservationService
{
    /// <summary>Shown when the party is smaller or larger than the configured limits. {0} = min, {1} = max.</summary>
    public const string PartySizeMessageFormat = "Party size must be between {0} and {1} guests.";

    /// <summary>Shown when the start time is not on the booking grid, e.g. 19:15.</summary>
    public const string OffGridMessage = "Please choose one of the listed start times.";

    /// <summary>Shown when the chosen time has already passed.</summary>
    public const string PastTimeMessage = "Please choose a time in the future.";

    /// <summary>Shown when the date is beyond the booking window. {0} = number of days.</summary>
    public const string TooFarAheadMessageFormat = "Bookings can be made up to {0} days ahead.";

    /// <summary>Shown when the restaurant is closed that day (weekly day off or special closure).</summary>
    public const string ClosedMessage = "The restaurant is closed on that day.";

    /// <summary>Shown when the visit would start before opening or finish after closing.</summary>
    public const string OutsideHoursMessage = "Your visit must start and finish within opening hours.";

    /// <summary>Shown when the chosen table is inactive, missing or too small.</summary>
    public const string TableUnavailableMessage = "That table cannot take this booking. Please search again.";

    /// <summary>Shown when another booking holds the table first, including the race the database catches.</summary>
    public const string TableJustTakenMessage = "Sorry, that table has just been booked. Please choose another table or time.";

    /// <summary>Shown when a booking does not exist, and also when it belongs to someone else (never reveal other bookings).</summary>
    public const string ReservationNotFoundMessage = "Reservation not found.";

    /// <summary>Shown when the transition table does not allow a change. {0} = current status, {1} = requested status.</summary>
    public const string StatusChangeNotAllowedMessageFormat = "A {0} booking cannot be changed to {1}.";

    /// <summary>Shown when staff mark a no-show too early. {0} = grace minutes.</summary>
    public const string TooEarlyForNoShowMessageFormat = "A booking can be marked as no-show {0} minutes after its start time.";

    /// <summary>How long staff wait for late guests before a booking may be marked as no-show.</summary>
    public const int NoShowGraceMinutes = 15;


    /// <summary>Shown when a booking has started or is no longer Pending or Confirmed.</summary>
    public const string BookingLockedMessage = "This booking can no longer be changed online. Please contact the restaurant.";

    /// <summary>Shown to YJ's order page when the booking is still waiting for staff.</summary>
    public const string NotConfirmedYetMessage = "You can order once your reservation is confirmed.";

    /// <summary>Shown to YJ's order page when the booking is finished, cancelled or a no-show.</summary>
    public const string ClosedForOrdersMessage = "This reservation is no longer open for orders.";


    /// <summary>One full day, the limit when listing start times.</summary>
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    private readonly ApplicationDbContext _context;
    private readonly IRestaurantService _restaurantService;
    private readonly ReservationTimeRules _timeRules;
    private readonly ReservationRulesOptions _options;
    private readonly INotificationService _notificationService;

    /// <summary>Receives its dependencies from dependency injection.</summary>
    /// <param name="context">The database.</param>
    /// <param name="restaurantService">Opening hours, closures and bookable tables (WL-1).</param>
    /// <param name="timeRules">Visit length, slot grid and slot calculation.</param>
    /// <param name="options">Party size limits and booking window.</param>
    /// <param name="notificationService">Tells staff about new bookings (YY-6).</param>
    public ReservationService(
        ApplicationDbContext context,
        IRestaurantService restaurantService,
        ReservationTimeRules timeRules,
        IOptions<ReservationRulesOptions> options,
        INotificationService notificationService)
    {
        _context = context;
        _restaurantService = restaurantService;
        _timeRules = timeRules;
        _options = options.Value;
        _notificationService = notificationService;
    }

    // ============================== 1. Start times ==============================//

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimeOnly>> GetStartTimesAsync(DateOnly date)
    {
        List<TimeOnly> times = new List<TimeOnly>();

        OperatingHour? hours = await GetOpeningHoursAsync(date);
        if (hours == null)
        {
            return times;
        }

        DateTime nowUtc = DateTime.UtcNow;
        for (TimeSpan offset = TimeSpan.Zero; offset < OneDay; offset += _timeRules.SlotLength)
        {
            TimeOnly start = TimeOnly.FromTimeSpan(offset);
            if (VisitFitsInHours(hours, start) && ToUtc(date, start) > nowUtc)
            {
                times.Add(start);
            }
        }

        return times;
    }

    // ============================== 2. Search ==============================//

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<AvailableTable>>> SearchAvailableTablesAsync(
        AvailabilitySearchInput input, int? ignoreReservationId = null)
    {
        string? problem = await CheckRequestAsync(input);
        if (problem != null)
        {
            return ServiceResult<IReadOnlyList<AvailableTable>>.Failure(problem);
        }

        List<DateTime> slotStarts = _timeRules.GetSlotStartsUtc(ToUtc(input.Date, input.Time)).ToList();

        // Tables that already have a booking in any of the slots this visit needs.
        List<int> busyTableIds = await _context.ReservationSlots
            .Where(s => slotStarts.Contains(s.SlotStartUtc))
            .Where(s => ignoreReservationId == null || s.ReservationId != ignoreReservationId)
            .Select(s => s.DiningTableId)
            .Distinct()
            .ToListAsync();

        // Bookable tables come smallest first, so a couple is offered a 2-seat table before a 6-seat one.
        IReadOnlyList<DiningTable> bookable = await _restaurantService.GetBookableTablesAsync();

        List<AvailableTable> free = new List<AvailableTable>();
        foreach (DiningTable table in bookable)
        {
            if (table.Capacity >= input.PartySize && !busyTableIds.Contains(table.Id))
            {
                free.Add(new AvailableTable
                {
                    DiningTableId = table.Id,
                    Label = table.Label,
                    AreaName = table.DiningArea?.Name ?? string.Empty,
                    Capacity = table.Capacity
                });
            }
        }

        return ServiceResult<IReadOnlyList<AvailableTable>>.Success(free);
    }

    // ============================== 3. Booking ==============================//

    /// <inheritdoc />
    public async Task<ServiceResult<int>> CreateReservationAsync(string customerId, BookingInput input)
    {
        List<string> errors = ValidateAnnotations(input);
        if (errors.Count > 0)
        {
            return ServiceResult<int>.Failure(errors.ToArray());
        }

        // Check every rule again: the search may have been minutes ago, or the form may have been edited.
        string? problem = await CheckRequestAsync(input);
        if (problem != null)
        {
            return ServiceResult<int>.Failure(problem);
        }

        IReadOnlyList<DiningTable> bookable = await _restaurantService.GetBookableTablesAsync();
        DiningTable? table = bookable.SingleOrDefault(t => t.Id == input.DiningTableId);
        if (table == null || table.Capacity < input.PartySize)
        {
            return ServiceResult<int>.Failure(TableUnavailableMessage);
        }

        DateTime startUtc = ToUtc(input.Date, input.Time);
        List<DateTime> slotStarts = _timeRules.GetSlotStartsUtc(startUtc).ToList();

        // First check, in C#: catches the normal case where someone booked a while ago.
        bool alreadyHeld = await _context.ReservationSlots
            .AnyAsync(s => s.DiningTableId == table.Id && slotStarts.Contains(s.SlotStartUtc));
        if (alreadyHeld)
        {
            return ServiceResult<int>.Failure(TableJustTakenMessage);
        }

        DateTime nowUtc = DateTime.UtcNow;
        Reservation reservation = new Reservation
        {
            CustomerId = customerId,
            DiningTableId = table.Id,
            PartySize = input.PartySize,
            StartUtc = startUtc,
            EndUtc = _timeRules.GetEndUtc(startUtc),
            Status = ReservationStatus.Pending,
            SpecialRequests = CleanOptional(input.SpecialRequests),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        foreach (DateTime slotStart in slotStarts)
        {
            reservation.Slots.Add(new ReservationSlot
            {
                DiningTableId = table.Id,
                SlotStartUtc = slotStart
            });
        }

        _context.Reservations.Add(reservation);

        // Second check, in the database: one SaveChanges writes the reservation and all its slots in a
        // single transaction. If another request inserted one of the same slots a moment earlier,
        // the unique index rejects this whole save, so nothing half-written is left behind.
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsSlotConflict(exception))
        {
            _context.ChangeTracker.Clear();
            return ServiceResult<int>.Failure(TableJustTakenMessage);
        }

        // Called after our own SaveChanges, as shared-contracts.md section 5 requires.
        await _notificationService.NotifyRoleAsync(
            RoleNames.Staff,
            NotificationType.General,
            "New reservation to confirm",
            $"Table {table.Label}, {input.PartySize} guests, {input.Date:d MMM yyyy} at {input.Time:HH\\:mm}.");

        return ServiceResult<int>.Success(reservation.Id);
    }

    // ============================== 4. My reservations ==============================//

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReservationSummary>> GetCustomerReservationsAsync(string customerId)
    {
        return await _context.Reservations
            .AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.StartUtc)
            .Select(r => new ReservationSummary
            {
                Id = r.Id,
                StartUtc = r.StartUtc,
                EndUtc = r.EndUtc,
                PartySize = r.PartySize,
                TableLabel = r.DiningTable!.Label,
                AreaName = r.DiningTable.DiningArea!.Name,
                Status = r.Status,
                SpecialRequests = r.SpecialRequests
            })
            .ToListAsync();
    }

    // ============================== 5. Staff: daily view and status changes (WL-3) ==============================//

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReservationDetails>> GetReservationsForDateAsync(DateOnly date)
    {
        DateTime dayStartUtc = ToUtc(date, TimeOnly.MinValue);
        DateTime nextDayStartUtc = ToUtc(date.AddDays(1), TimeOnly.MinValue);

        return await _context.Reservations
            .AsNoTracking()
            .Where(r => r.StartUtc >= dayStartUtc && r.StartUtc < nextDayStartUtc)
            .OrderBy(r => r.StartUtc)
            .ThenBy(r => r.DiningTable!.Label)
            .Select(ToDetails)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<ReservationDetails?> GetReservationAsync(int reservationId)
    {
        return await _context.Reservations
            .AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(ToDetails)
            .SingleOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> ConfirmAsync(int reservationId)
    {
        ServiceResult<Reservation> moved = await MoveStatusAsync(reservationId, ReservationStatus.Confirmed);
        if (!moved.Succeeded)
        {
            return moved;
        }

        Reservation reservation = moved.Value!;
        await _context.SaveChangesAsync();

        await NotifyCustomerAsync(reservation, NotificationType.ReservationConfirmed,
            "Reservation confirmed",
            $"Your table for {reservation.PartySize} on {DescribeVisit(reservation)} is confirmed.");
        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> SeatAsync(int reservationId)
    {
        ServiceResult<Reservation> moved = await MoveStatusAsync(reservationId, ReservationStatus.Seated);
        if (!moved.Succeeded)
        {
            return moved;
        }

        Reservation reservation = moved.Value!;
        await _context.SaveChangesAsync();

        // From now on the kitchen may start cooking this booking's orders (Alex's rule).
        await NotifyCustomerAsync(reservation, NotificationType.General,
            "Welcome to Three Grills",
            $"You are seated at table {reservation.DiningTable!.Label}. Enjoy your meal!");
        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> CompleteAsync(int reservationId)
    {
        ServiceResult<Reservation> moved = await MoveStatusAsync(reservationId, ReservationStatus.Completed);
        if (!moved.Succeeded)
        {
            return moved;
        }

        Reservation reservation = moved.Value!;

        // Guests who leave early free the rest of their time, so the table can be booked again straight away.
        ReleaseSlots(reservation, DateTime.UtcNow);
        await _context.SaveChangesAsync();

        await NotifyCustomerAsync(reservation, NotificationType.General,
            "Thank you for dining with us",
            "We hope you enjoyed your visit. We would love to hear your feedback.");
        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> CancelByStaffAsync(int reservationId)
    {
        ServiceResult<Reservation> moved = await MoveStatusAsync(reservationId, ReservationStatus.Cancelled);
        if (!moved.Succeeded)
        {
            return moved;
        }

        Reservation reservation = moved.Value!;
        ReleaseSlots(reservation, DateTime.MinValue);
        await CancelUnstartedOrdersAsync(reservation.Id);
        await _context.SaveChangesAsync();

        await NotifyCustomerAsync(reservation, NotificationType.ReservationCancelled,
            "Reservation cancelled",
            $"Your booking on {DescribeVisit(reservation)} has been cancelled by the restaurant. Please contact us if this is unexpected.");
        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> MarkNoShowAsync(int reservationId)
    {
        Reservation? existing = await FindReservationAsync(reservationId);
        if (existing == null)
        {
            return ServiceResult.Failure(ReservationNotFoundMessage);
        }

        // Give late guests a fair chance before the table is given away.
        if (DateTime.UtcNow < existing.StartUtc.AddMinutes(NoShowGraceMinutes))
        {
            return ServiceResult.Failure(string.Format(TooEarlyForNoShowMessageFormat, NoShowGraceMinutes));
        }

        ServiceResult<Reservation> moved = await MoveStatusAsync(reservationId, ReservationStatus.NoShow);
        if (!moved.Succeeded)
        {
            return moved;
        }

        Reservation reservation = moved.Value!;
        ReleaseSlots(reservation, DateTime.MinValue);
        await CancelUnstartedOrdersAsync(reservation.Id);
        await _context.SaveChangesAsync();

        await NotifyCustomerAsync(reservation, NotificationType.ReservationCancelled,
            "We missed you",
            $"Your booking on {DescribeVisit(reservation)} was marked as a no-show and the table has been released.");
        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> ModifyByStaffAsync(int reservationId, BookingInput input)
    {
        Reservation? reservation = await FindReservationAsync(reservationId);
        if (reservation == null)
        {
            return ServiceResult.Failure(ReservationNotFoundMessage);
        }

        if (!ReservationStatusRules.IsChangeable(reservation.Status))
        {
            return ServiceResult.Failure(BookingLockedMessage);
        }

        // Staff speak for the restaurant, so the booking keeps its status.
        ServiceResult changed = await ApplyBookingChangesAsync(reservation, input);
        if (!changed.Succeeded)
        {
            return changed;
        }

        await NotifyCustomerAsync(reservation, NotificationType.ReservationUpdated,
            "Reservation updated",
            $"The restaurant has updated your booking: table for {reservation.PartySize} on {DescribeVisit(reservation)}.");
        return ServiceResult.Success();
    }

    // ============================== 6. Customer changes and orders (WL-3) ==============================//

    /// <inheritdoc />
    public async Task<ReservationDetails?> GetCustomerReservationAsync(string customerId, int reservationId)
    {
        // Filtering by the customer as well as the ID means someone else's booking is simply "not found".
        return await _context.Reservations
            .AsNoTracking()
            .Where(r => r.Id == reservationId && r.CustomerId == customerId)
            .Select(ToDetails)
            .SingleOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> ModifyByCustomerAsync(string customerId, int reservationId, BookingInput input)
    {
        Reservation? reservation = await FindReservationAsync(reservationId);
        if (reservation == null || reservation.CustomerId != customerId)
        {
            return ServiceResult.Failure(ReservationNotFoundMessage);
        }

        if (!ReservationStatusRules.IsChangeable(reservation.Status) || reservation.StartUtc <= DateTime.UtcNow)
        {
            return ServiceResult.Failure(BookingLockedMessage);
        }

        ReservationStatus statusBefore = reservation.Status;

        // A confirmed booking that changes needs the restaurant to agree again.
        reservation.Status = ReservationStatus.Pending;

        ServiceResult changed = await ApplyBookingChangesAsync(reservation, input);
        if (!changed.Succeeded)
        {
            return changed;
        }

        string again = statusBefore == ReservationStatus.Confirmed ? " Please confirm it again." : string.Empty;
        await _notificationService.NotifyRoleAsync(
            RoleNames.Staff,
            NotificationType.ReservationUpdated,
            "Reservation changed by customer",
            $"Table {reservation.DiningTable!.Label}, {reservation.PartySize} guests, {DescribeVisit(reservation)}.{again}");
        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> CancelByCustomerAsync(string customerId, int reservationId)
    {
        Reservation? existing = await FindReservationAsync(reservationId);
        if (existing == null || existing.CustomerId != customerId)
        {
            return ServiceResult.Failure(ReservationNotFoundMessage);
        }

        // After the start time a missing customer is handled by staff as a no-show.
        if (existing.StartUtc <= DateTime.UtcNow)
        {
            return ServiceResult.Failure(BookingLockedMessage);
        }

        ServiceResult<Reservation> moved = await MoveStatusAsync(reservationId, ReservationStatus.Cancelled);
        if (!moved.Succeeded)
        {
            return moved;
        }

        Reservation reservation = moved.Value!;
        ReleaseSlots(reservation, DateTime.MinValue);
        await CancelUnstartedOrdersAsync(reservation.Id);
        await _context.SaveChangesAsync();

        await _notificationService.NotifyRoleAsync(
            RoleNames.Staff,
            NotificationType.ReservationCancelled,
            "Reservation cancelled by customer",
            $"Table {reservation.DiningTable!.Label}, {reservation.PartySize} guests, {DescribeVisit(reservation)}. The table is free again.");
        return ServiceResult.Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> CanOrderForReservationAsync(string customerId, int reservationId)
    {
        ReservationStatus? status = await _context.Reservations
            .Where(r => r.Id == reservationId && r.CustomerId == customerId)
            .Select(r => (ReservationStatus?)r.Status)
            .SingleOrDefaultAsync();

        if (status == null)
        {
            return ServiceResult.Failure(ReservationNotFoundMessage);
        }

        if (status == ReservationStatus.Confirmed || status == ReservationStatus.Seated)
        {
            return ServiceResult.Success();
        }

        if (status == ReservationStatus.Pending)
        {
            return ServiceResult.Failure(NotConfirmedYetMessage);
        }

        return ServiceResult.Failure(ClosedForOrdersMessage);
    }

    // ============================== 7. Helpers (private) ==============================//

    /// <summary>
    /// Checks a date, time and party size against every booking rule.
    /// Returns the first problem found, or null when the request is acceptable.
    /// </summary>
    /// <param name="input">The search or booking values.</param>
    private async Task<string?> CheckRequestAsync(AvailabilitySearchInput input)
    {
        if (input.PartySize < _options.MinPartySize || input.PartySize > _options.MaxPartySize)
        {
            return string.Format(PartySizeMessageFormat, _options.MinPartySize, _options.MaxPartySize);
        }

        if (!_timeRules.IsOnSlotGrid(input.Time))
        {
            return OffGridMessage;
        }

        if (ToUtc(input.Date, input.Time) <= DateTime.UtcNow)
        {
            return PastTimeMessage;
        }

        DateOnly todayMalaysia = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        if (input.Date > todayMalaysia.AddDays(_options.MaxDaysAhead))
        {
            return string.Format(TooFarAheadMessageFormat, _options.MaxDaysAhead);
        }

        OperatingHour? hours = await GetOpeningHoursAsync(input.Date);
        if (hours == null)
        {
            return ClosedMessage;
        }

        if (!VisitFitsInHours(hours, input.Time))
        {
            return OutsideHoursMessage;
        }

        return null;
    }

    /// <summary>
    /// The opening hours for a date, or null when the restaurant does not open that day
    /// (weekly day off, special closure, or no row for that weekday).
    /// </summary>
    /// <param name="date">A Malaysia calendar date.</param>
    private async Task<OperatingHour?> GetOpeningHoursAsync(DateOnly date)
    {
        if (await _restaurantService.IsClosedOnAsync(date))
        {
            return null;
        }

        IReadOnlyList<OperatingHour> week = await _restaurantService.GetOperatingHoursAsync();
        return week.SingleOrDefault(h => h.DayOfWeek == date.DayOfWeek && !h.IsClosed);
    }

    /// <summary>True when a visit starting at this time begins after opening and ends by closing.</summary>
    /// <param name="hours">That day's opening hours.</param>
    /// <param name="start">Visit start (Malaysia).</param>
    private bool VisitFitsInHours(OperatingHour hours, TimeOnly start)
    {
        TimeSpan startTime = start.ToTimeSpan();
        return startTime >= hours.OpensAt.ToTimeSpan()
            && startTime + _timeRules.VisitLength <= hours.ClosesAt.ToTimeSpan();
    }

    /// <summary>Turns a Malaysia date and time from the form into the UTC value stored in the database.</summary>
    /// <param name="date">Malaysia date.</param>
    /// <param name="time">Malaysia time.</param>
    private static DateTime ToUtc(DateOnly date, TimeOnly time)
    {
        return MalaysiaTime.ToUtc(date.ToDateTime(time));
    }

    /// <summary>
    /// True when a failed save was caused by the double-booking unique index.
    /// SQL Server names the index in its message; SQLite (used by the tests) names the columns.
    /// Any other database error is not caught, so it reaches the error page as an unexpected failure.
    /// </summary>
    /// <param name="exception">The error from SaveChanges.</param>
    private static bool IsSlotConflict(DbUpdateException exception)
    {
        string message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains(ReservationConfiguration.SlotUniqueIndexName)
            || message.Contains("ReservationSlots.DiningTableId");
    }

    /// <summary>Runs the [Required], [StringLength] and [Range] checks on an input object.</summary>
    /// <param name="input">The object to check.</param>
    private static List<string> ValidateAnnotations(object input)
    {
        ValidationContext validationContext = new ValidationContext(input);
        List<ValidationResult> results = new List<ValidationResult>();
        Validator.TryValidateObject(input, validationContext, results, validateAllProperties: true);

        List<string> messages = new List<string>();
        foreach (ValidationResult result in results)
        {
            messages.Add(result.ErrorMessage ?? "A value is not valid.");
        }

        return messages;
    }

    /// <summary>Turns blank optional text into null and trims the rest.</summary>
    /// <param name="value">Text from the form.</param>
    private static string? CleanOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }


    /// <summary>
    /// A reusable "Select" recipe that turns a Reservation row into ReservationDetails.
    /// EF translates it into SQL, joining the table, area and customer, so only the needed columns are read.
    /// </summary>
    private static readonly Expression<Func<Reservation, ReservationDetails>> ToDetails = r => new ReservationDetails
    {
        Id = r.Id,
        CustomerId = r.CustomerId,
        CustomerName = r.Customer!.FullName,
        CustomerPhone = r.Customer.PhoneNumber,
        StartUtc = r.StartUtc,
        EndUtc = r.EndUtc,
        PartySize = r.PartySize,
        DiningTableId = r.DiningTableId,
        TableLabel = r.DiningTable!.Label,
        AreaName = r.DiningTable.DiningArea!.Name,
        Status = r.Status,
        SpecialRequests = r.SpecialRequests,
        CreatedAtUtc = r.CreatedAtUtc
    };

    /// <summary>Loads a booking with its slots and table, tracked so it can be changed and saved.</summary>
    /// <param name="reservationId">The booking to load.</param>
    private async Task<Reservation?> FindReservationAsync(int reservationId)
    {
        return await _context.Reservations
            .Include(r => r.Slots)
            .Include(r => r.DiningTable)
            .SingleOrDefaultAsync(r => r.Id == reservationId);
    }

    /// <summary>
    /// The one path every status change takes: find the booking, ask the transition table, then set the new status.
    /// Nothing is saved here, so the caller can add its own changes (releasing slots) to the same save.
    /// </summary>
    /// <param name="reservationId">The booking to change.</param>
    /// <param name="newStatus">The requested status.</param>
    private async Task<ServiceResult<Reservation>> MoveStatusAsync(int reservationId, ReservationStatus newStatus)
    {
        Reservation? reservation = await FindReservationAsync(reservationId);
        if (reservation == null)
        {
            return ServiceResult<Reservation>.Failure(ReservationNotFoundMessage);
        }

        if (!ReservationStatusRules.CanMove(reservation.Status, newStatus))
        {
            return ServiceResult<Reservation>.Failure(
                string.Format(StatusChangeNotAllowedMessageFormat, reservation.Status, newStatus));
        }

        reservation.Status = newStatus;
        reservation.UpdatedAtUtc = DateTime.UtcNow;
        return ServiceResult<Reservation>.Success(reservation);
    }

    /// <summary>Gives a booking's table time back: removes its slots that start at or after a moment.</summary>
    /// <param name="reservation">A booking loaded with its slots.</param>
    /// <param name="fromUtc">DateTime.MinValue releases every slot; "now" releases only the unused ones.</param>
    private void ReleaseSlots(Reservation reservation, DateTime fromUtc)
    {
        List<ReservationSlot> released = reservation.Slots.Where(s => s.SlotStartUtc >= fromUtc).ToList();
        _context.ReservationSlots.RemoveRange(released);
    }


    /// <summary>
    /// Moves a booking to new values (date, time, party size, table, note) after checking every booking rule again.
    /// Shared by the customer's and the staff's change. Saves the change and returns the result.
    /// </summary>
    /// <remarks>
    /// Only the slots that really change are touched: moving 19:00 to 19:30 on the same table keeps the
    /// 19:30, 20:00 and 20:30 rows, removes 19:00 and adds 21:00. So the booking never collides with itself,
    /// while the unique index still stops it taking a slot another booking holds.
    /// </remarks>
    /// <param name="reservation">The tracked booking, loaded with its slots.</param>
    /// <param name="input">The new values.</param>
    private async Task<ServiceResult> ApplyBookingChangesAsync(Reservation reservation, BookingInput input)
    {
        List<string> errors = ValidateAnnotations(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Failure(errors.ToArray());
        }

        string? problem = await CheckRequestAsync(input);
        if (problem != null)
        {
            return ServiceResult.Failure(problem);
        }

        IReadOnlyList<DiningTable> bookable = await _restaurantService.GetBookableTablesAsync();
        DiningTable? table = bookable.SingleOrDefault(t => t.Id == input.DiningTableId);
        if (table == null || table.Capacity < input.PartySize)
        {
            return ServiceResult.Failure(TableUnavailableMessage);
        }

        DateTime startUtc = ToUtc(input.Date, input.Time);
        List<DateTime> newSlotStarts = _timeRules.GetSlotStartsUtc(startUtc).ToList();

        // Is another booking holding any of the new slots? This booking's own slots do not count.
        bool heldByOthers = await _context.ReservationSlots.AnyAsync(s =>
            s.DiningTableId == table.Id
            && newSlotStarts.Contains(s.SlotStartUtc)
            && s.ReservationId != reservation.Id);
        if (heldByOthers)
        {
            return ServiceResult.Failure(TableJustTakenMessage);
        }

        // Remove the slots the booking no longer needs, then add only the ones it does not hold yet.
        List<ReservationSlot> noLongerNeeded = reservation.Slots
            .Where(s => s.DiningTableId != table.Id || !newSlotStarts.Contains(s.SlotStartUtc))
            .ToList();
        _context.ReservationSlots.RemoveRange(noLongerNeeded);

        foreach (DateTime slotStart in newSlotStarts)
        {
            bool alreadyHeld = reservation.Slots.Any(s => s.DiningTableId == table.Id && s.SlotStartUtc == slotStart);
            if (!alreadyHeld)
            {
                reservation.Slots.Add(new ReservationSlot { DiningTableId = table.Id, SlotStartUtc = slotStart });
            }
        }

        reservation.DiningTableId = table.Id;
        // Point at the tracked table row, not the read-only copy from the restaurant service,
        // so EF does not try to insert that table again. Messages after the save use its label.
        reservation.DiningTable = await _context.DiningTables.FindAsync(table.Id);
        reservation.PartySize = input.PartySize;
        reservation.StartUtc = startUtc;
        reservation.EndUtc = _timeRules.GetEndUtc(startUtc);
        reservation.SpecialRequests = CleanOptional(input.SpecialRequests);
        reservation.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsSlotConflict(exception))
        {
            _context.ChangeTracker.Clear();
            return ServiceResult.Failure(TableJustTakenMessage);
        }

        return ServiceResult.Success();
    }

    /// <summary>
    /// Cancels the booking's orders that the kitchen has not started (still Placed).
    /// Order status changes belong to Alex, so this will call his CancelUnstartedOrdersForReservationAsync
    /// using the existing context, so reservation and order cancellation are saved together.
    /// </summary>
    /// <param name="reservationId">The cancelled or no-show booking.</param>
    private Task CancelUnstartedOrdersAsync(int reservationId)
    {
        return new OrderProcessingService(_context, _notificationService)
            .CancelUnstartedOrdersForReservationAsync(reservationId);
    }

    /// <summary>Sends a notification to the booking's customer, linking to its details page.</summary>
    private Task NotifyCustomerAsync(Reservation reservation, NotificationType type, string title, string message)
    {
        return _notificationService.NotifyUserAsync(
            reservation.CustomerId, type, title, message, $"/Customer/Reservations/Details/{reservation.Id}");
    }

    /// <summary>A booking's start in Malaysia time for messages, e.g. "Tue 20 Oct at 7:00 PM".</summary>
    private static string DescribeVisit(Reservation reservation)
    {
        return MalaysiaTime.FromUtc(reservation.StartUtc).ToString("ddd d MMM 'at' h:mm tt");
    }
}