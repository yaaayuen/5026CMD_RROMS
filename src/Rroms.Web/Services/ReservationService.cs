using System.ComponentModel.DataAnnotations;

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
    public async Task<ServiceResult<IReadOnlyList<AvailableTable>>> SearchAvailableTablesAsync(AvailabilitySearchInput input)
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

    // ============================== 5. Helpers (private) ==============================//

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
}