using Microsoft.EntityFrameworkCore;
using Rroms.Web.Constants;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Accepts requests only from the signed-in customer's seated visit and notifies every staff member.</summary>
public class TableRequestService : ITableRequestService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;
    public TableRequestService(ApplicationDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task<ServiceResult> RequestAsync(string customerId, int reservationId, TableRequestKind kind)
    {
        if (!Enum.IsDefined(kind)) { return ServiceResult.Failure("Choose what you need."); }
        var visit = await _context.Reservations.AsNoTracking().Include(r => r.DiningTable)
            .SingleOrDefaultAsync(r => r.Id == reservationId && r.CustomerId == customerId);
        if (visit == null) { return ServiceResult.Failure("Reservation not found."); }
        if (visit.Status != ReservationStatus.Seated) { return ServiceResult.Failure("You can call staff only while you are seated."); }
        if (await _context.TableRequests.AnyAsync(r => r.ReservationId == reservationId && r.Kind == kind && r.HandledAtUtc == null))
        {
            return ServiceResult.Success();
        }
        _context.TableRequests.Add(new TableRequest { ReservationId = reservationId, Kind = kind, CreatedAtUtc = DateTime.UtcNow });
        await _context.SaveChangesAsync();
        string table = "Table " + (visit.DiningTable?.Label ?? "#" + visit.DiningTableId);
        if (kind == TableRequestKind.Bill)
        {
            await _notifications.NotifyRoleAsync(RoleNames.Staff, NotificationType.General, $"{table} asked for the bill",
                $"The guests at {table} would like to pay.", $"/Staff/Billing/Visit/{reservationId}");
        }
        else
        {
            await _notifications.NotifyRoleAsync(RoleNames.Staff, NotificationType.General, $"{table} needs a staff member",
                $"The guests at {table} asked for someone to come to their table.", "/Staff/Orders");
        }
        return ServiceResult.Success();
    }

    public Task<List<TableRequest>> GetOpenAsync()
    {
        return _context.TableRequests.AsNoTracking()
            .Include(r => r.Reservation).ThenInclude(v => v!.DiningTable)
            .Include(r => r.Reservation).ThenInclude(v => v!.Customer)
            .Where(r => r.HandledAtUtc == null && r.Reservation!.Status == ReservationStatus.Seated)
            .OrderBy(r => r.CreatedAtUtc).ToListAsync();
    }

    public Task<List<TableRequest>> GetOpenForCustomerAsync(string customerId)
    {
        return _context.TableRequests.AsNoTracking()
            .Where(r => r.HandledAtUtc == null && r.Reservation!.CustomerId == customerId && r.Reservation.Status == ReservationStatus.Seated)
            .ToListAsync();
    }

    public async Task<ServiceResult> HandleAsync(int id)
    {
        // Only the first press counts, so two staff members cannot both close the same request.
        int changed = await _context.TableRequests.Where(r => r.Id == id && r.HandledAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.HandledAtUtc, DateTime.UtcNow));
        return changed == 1 ? ServiceResult.Success() : ServiceResult.Failure("This request was already handled.");
    }
}
