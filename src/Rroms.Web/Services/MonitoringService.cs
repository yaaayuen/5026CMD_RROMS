using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
/// <summary>DA-7 composes WL's reservation queries with YY's current dining preferences.</summary>
public class MonitoringService : IMonitoringService
{
    private readonly ApplicationDbContext _context;
    private readonly IReservationService _reservations;

    /// <summary>Uses the existing scoped database and reservation service; no status rules are duplicated.</summary>
    public MonitoringService(ApplicationDbContext context, IReservationService reservations)
    {
        _context = context;
        _reservations = reservations;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReservationAreaOption>> GetReservationAreasAsync()
    {
        return await _context.DiningAreas.AsNoTracking().OrderBy(a => a.Name)
            .Select(a => new ReservationAreaOption { Id = a.Id, Name = a.Name }).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<StaffReservationRow>>> GetDailyReservationsAsync(DailyReservationFilter filter)
    {
        // The boundary dates cannot safely be converted to a UTC day and next-day boundary.
        if (filter.Date <= DateOnly.MinValue || filter.Date >= DateOnly.MaxValue)
        {
            return ServiceResult<IReadOnlyList<StaffReservationRow>>.Failure("Choose a valid reservation date.");
        }
        if (filter.Status.HasValue && !Enum.IsDefined(filter.Status.Value))
        {
            return ServiceResult<IReadOnlyList<StaffReservationRow>>.Failure("Choose a valid reservation status.");
        }
        if (filter.AreaId.HasValue && !await _context.DiningAreas.AnyAsync(a => a.Id == filter.AreaId.Value))
        {
            return ServiceResult<IReadOnlyList<StaffReservationRow>>.Failure("Choose an existing dining area.");
        }

        IReadOnlyList<ReservationDetails> daily = await _reservations.GetReservationsForDateAsync(filter.Date);
        IEnumerable<ReservationDetails> filtered = daily;
        if (filter.Status.HasValue)
        {
            filtered = filtered.Where(r => r.Status == filter.Status.Value);
        }
        if (filter.AreaId.HasValue)
        {
            // Use table/area IDs rather than editable display names.
            List<int> tableIds = await _context.DiningTables.AsNoTracking()
                .Where(t => t.DiningAreaId == filter.AreaId.Value).Select(t => t.Id).ToListAsync();
            filtered = filtered.Where(r => tableIds.Contains(r.DiningTableId));
        }
        List<ReservationDetails> bookings = filtered.OrderBy(r => r.StartUtc).ThenBy(r => r.Id).ToList();
        List<string> customerIds = bookings.Select(r => r.CustomerId).Distinct().ToList();
        // One batched lookup avoids a database query for every customer in the list.
        List<ApplicationUser> customers = await _context.Users.AsNoTracking()
            .Where(u => customerIds.Contains(u.Id)).Include(u => u.AvoidedDietaryTags).ToListAsync();
        Dictionary<string, string?> preferences = customers.ToDictionary(u => u.Id, FormatPreferences);
        var rows = new List<StaffReservationRow>();
        foreach (ReservationDetails booking in bookings)
        {
            preferences.TryGetValue(booking.CustomerId, out string? preference);
            rows.Add(new StaffReservationRow { Reservation = booking, DiningPreferences = preference });
        }
        return ServiceResult<IReadOnlyList<StaffReservationRow>>.Success(rows);
    }

    /// <inheritdoc />
    public async Task<StaffReservationHistory?> GetCustomerHistoryAsync(int reservationId)
    {
        // Staff select a booking, not a submitted customer identity. Area authorization protects this query.
        ReservationDetails? booking = await _reservations.GetReservationAsync(reservationId);
        if (booking == null)
        {
            return null;
        }
        IReadOnlyList<ReservationSummary> reservations = await _reservations.GetCustomerReservationsAsync(booking.CustomerId);
        DateTime nowUtc = DateTime.UtcNow;
        ApplicationUser? customer = await _context.Users.AsNoTracking()
            .Include(u => u.AvoidedDietaryTags).SingleOrDefaultAsync(u => u.Id == booking.CustomerId);
        string? preferences = FormatPreferences(customer);
        return new StaffReservationHistory
        {
            ReservationId = booking.Id,
            CustomerName = booking.CustomerName,
            DiningPreferences = preferences,
            // Include cancelled/no-show records, but exclude future and still-running scheduled visits.
            Reservations = reservations.Where(r => r.EndUtc <= nowUtc)
                .OrderByDescending(r => r.StartUtc).ThenByDescending(r => r.Id).ToList()
        };
    }

    // YY-9 stores avoided tags only with consent; do not expose stale tags after consent is withdrawn.
    private static string? FormatPreferences(ApplicationUser? customer)
    {
        if (customer == null || customer.DietaryConsentAtUtc == null || customer.AvoidedDietaryTags.Count == 0)
        {
            return null;
        }
        return "Avoids: " + string.Join(", ", customer.AvoidedDietaryTags
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Id).Select(t => t.Name));
    }
}
