using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Checks ownership and completion before accepting a single rating per visit.</summary>
public class FeedbackService : IFeedbackService
{
    private readonly ApplicationDbContext _context;
    public FeedbackService(ApplicationDbContext context) { _context = context; }

    public Task<List<Reservation>> GetEligibleAsync(string customerId)
    {
        return _context.Reservations.AsNoTracking().Where(r => r.CustomerId == customerId
            && r.Status == ReservationStatus.Completed && !_context.Feedback.Any(f => f.ReservationId == r.Id))
            .OrderByDescending(r => r.StartUtc).ToListAsync();
    }

    public async Task<ServiceResult> SubmitAsync(string customerId, int reservationId, int rating, string? comment)
    {
        var reservation = await _context.Reservations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == reservationId && r.CustomerId == customerId);
        if (reservation == null) { return ServiceResult.Failure("Reservation not found."); }
        if (reservation.Status != ReservationStatus.Completed) { return ServiceResult.Failure("Only completed visits can be rated."); }
        if (rating < 1 || rating > 5 || (comment?.Length ?? 0) > 1000)
        {
            return ServiceResult.Failure("Choose 1 to 5 stars and a comment of at most 1000 characters.");
        }
        if (await _context.Feedback.AnyAsync(f => f.ReservationId == reservationId))
        {
            return ServiceResult.Failure("This visit already has feedback.");
        }
        var feedback = new Feedback { ReservationId = reservationId, Rating = rating,
            Comment = comment?.Trim() ?? "", CreatedAtUtc = DateTime.UtcNow };
        _context.Feedback.Add(feedback);
        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException ex) when ((ex.InnerException?.Message ?? "").Contains("IX_Feedback_ReservationId")
            || (ex.InnerException?.Message ?? "").Contains("UNIQUE constraint failed: Feedback.ReservationId"))
        {
            _context.Entry(feedback).State = EntityState.Detached;
            return ServiceResult.Failure("This visit already has feedback.");
        }
        return ServiceResult.Success();
    }

    public Task<List<Feedback>> GetAsync(int? rating, DateTime? fromUtc, DateTime? untilUtc)
    {
        var query = _context.Feedback.AsNoTracking().Include(f => f.Reservation).AsQueryable();
        if (rating.HasValue) { query = query.Where(f => f.Rating == rating.Value); }
        if (fromUtc.HasValue) { query = query.Where(f => f.CreatedAtUtc >= fromUtc.Value); }
        if (untilUtc.HasValue) { query = query.Where(f => f.CreatedAtUtc < untilUtc.Value); }
        return query.OrderByDescending(f => f.CreatedAtUtc).ToListAsync();
    }
}
