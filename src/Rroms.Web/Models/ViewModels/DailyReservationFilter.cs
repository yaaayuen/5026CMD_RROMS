using Rroms.Web.Contracts;
using Rroms.Web.Common;
namespace Rroms.Web.Models.ViewModels;
/// <summary>Optional filters for one Malaysia calendar day of reservations.</summary>
public class DailyReservationFilter
{
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
    public ReservationStatus? Status { get; set; }
    public int? AreaId { get; set; }
}
