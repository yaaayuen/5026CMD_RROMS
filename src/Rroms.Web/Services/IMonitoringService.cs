using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
/// <summary>Read-only monitoring queries. DA-7 entry points are for staff/admin pages only.</summary>
public interface IMonitoringService
{
    /// <summary>Lists all areas, including inactive areas needed when viewing past reservations.</summary>
    Task<IReadOnlyList<ReservationAreaOption>> GetReservationAreasAsync();
    /// <summary>Validates filters and returns bookings starting on the chosen Malaysia day.</summary>
    Task<ServiceResult<IReadOnlyList<StaffReservationRow>>> GetDailyReservationsAsync(DailyReservationFilter filter);
    /// <summary>Finds a booking's customer and returns visits whose scheduled end has passed, or null if missing.</summary>
    Task<StaffReservationHistory?> GetCustomerHistoryAsync(int reservationId);
}
