using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;

/// <summary>Checks ownership and completion before accepting a single rating per visit.</summary>
public class FeedbackService : IFeedbackService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;
    private readonly FeedbackWordFilter _words;
    public FeedbackService(ApplicationDbContext context, INotificationService notifications, FeedbackWordFilter words)
    {
        _context = context;
        _notifications = notifications;
        _words = words;
    }

    public IReadOnlyList<string> BlockedWordsIn(string? comment, IEnumerable<string> customWords) => _words.Find(comment, customWords);

    public IReadOnlyList<string> FixedBlockedWords => _words.FixedWords;

    public Task<List<BlockedWord>> GetCustomBlockedWordsAsync()
    {
        return _context.BlockedWords.AsNoTracking().OrderBy(w => w.Word).ToListAsync();
    }

    public async Task<ServiceResult> AddBlockedWordAsync(string? word)
    {
        string text = string.Join(' ', (word ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        // Letters, digits, spaces, hyphens and apostrophes only, so a word can never become a regular expression.
        if (text.Length < 2 || text.Length > BlockedWord.MaxLength || !text.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '\''))
        {
            return ServiceResult.Failure($"Enter a word of 2 to {BlockedWord.MaxLength} letters.");
        }
        if (_words.FixedWords.Contains(text) || await _context.BlockedWords.AnyAsync(w => w.Word == text))
        {
            return ServiceResult.Failure($"\"{text}\" is already blocked.");
        }
        _context.BlockedWords.Add(new BlockedWord { Word = text, CreatedAtUtc = DateTime.UtcNow });
        await _context.SaveChangesAsync();
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RemoveBlockedWordAsync(int id)
    {
        // Reviews already flagged by this word stay hidden; the administrator can show them one by one.
        int removed = await _context.BlockedWords.Where(w => w.Id == id).ExecuteDeleteAsync();
        return removed == 1 ? ServiceResult.Success() : ServiceResult.Failure("Word not found.");
    }

    public async Task<int> RecheckAsync()
    {
        List<string> custom = await CustomWordsAsync();
        // Already-flagged reviews are skipped, so a review the administrator chose to show stays shown.
        var candidates = await _context.Feedback.Where(f => !f.IsFlagged && !f.CommentRemoved && f.Comment != "").ToListAsync();
        var matched = candidates.Where(f => _words.Find(f.Comment, custom).Count > 0).ToList();
        foreach (var feedback in matched)
        {
            feedback.IsHidden = true;
            feedback.IsFlagged = true;
        }
        await _context.SaveChangesAsync();
        return matched.Count;
    }

    private Task<List<string>> CustomWordsAsync() => _context.BlockedWords.Select(w => w.Word).ToListAsync();

    public Task<List<Reservation>> GetEligibleAsync(string customerId)
    {
        return _context.Reservations.AsNoTracking().Where(r => r.CustomerId == customerId
            && r.Status == ReservationStatus.Completed && !_context.Feedback.Any(f => f.ReservationId == r.Id))
            .OrderByDescending(r => r.StartUtc).ToListAsync();
    }

    public Task<List<MenuItem>> GetVisitDishesAsync(string customerId, int reservationId)
    {
        // Dishes from orders the customer actually received; cancelled orders are left out.
        var ordered = _context.OrderItems
            .Where(i => i.Order!.ReservationId == reservationId && i.Order.CustomerId == customerId && i.Order.Status != OrderStatus.Cancelled)
            .Select(i => i.MenuItemId);
        return _context.MenuItems.AsNoTracking().Where(m => ordered.Contains(m.Id)).OrderBy(m => m.Name).ToListAsync();
    }

    public async Task<ServiceResult> SubmitAsync(string customerId, int reservationId, int rating, string? comment,
        IReadOnlyDictionary<int, int>? dishRatings = null)
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
        var dishes = dishRatings ?? new Dictionary<int, int>();
        if (dishes.Values.Any(stars => stars < 1 || stars > 5)) { return ServiceResult.Failure("Rate each dish from 1 to 5 stars."); }
        if (dishes.Count > 0)
        {
            var eaten = (await GetVisitDishesAsync(customerId, reservationId)).Select(m => m.Id).ToHashSet();
            if (!dishes.Keys.All(eaten.Contains)) { return ServiceResult.Failure("Only dishes from this visit can be rated."); }
        }
        // A blocked word hides the review from the website until an administrator checks it. The customer is not told.
        bool flagged = _words.Find(comment, await CustomWordsAsync()).Count > 0;
        var feedback = new Feedback { ReservationId = reservationId, Rating = rating,
            Comment = comment?.Trim() ?? "", CreatedAtUtc = DateTime.UtcNow, IsHidden = flagged, IsFlagged = flagged,
            DishRatings = dishes.Select(d => new DishRating { MenuItemId = d.Key, Rating = d.Value }).ToList() };
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

    public Task<List<Feedback>> GetAsync(int? rating, DateTime? fromUtc, DateTime? untilUtc,
        FeedbackView view = FeedbackView.All, string? search = null)
    {
        var query = _context.Feedback.AsNoTracking().Include(f => f.Reservation).ThenInclude(r => r!.Customer)
            .Include(f => f.DishRatings).ThenInclude(d => d.MenuItem).AsQueryable();
        if (rating.HasValue) { query = query.Where(f => f.Rating == rating.Value); }
        if (fromUtc.HasValue) { query = query.Where(f => f.CreatedAtUtc >= fromUtc.Value); }
        if (untilUtc.HasValue) { query = query.Where(f => f.CreatedAtUtc < untilUtc.Value); }
        query = view switch
        {
            FeedbackView.Shown => query.Where(f => !f.IsHidden),
            FeedbackView.Hidden => query.Where(f => f.IsHidden),
            FeedbackView.Flagged => query.Where(f => f.IsFlagged),
            FeedbackView.AwaitingReply => query.Where(f => f.RepliedAtUtc == null),
            FeedbackView.Replied => query.Where(f => f.RepliedAtUtc != null),
            _ => query
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Lower-cased on both sides so SQLite matches like SQL Server's case-insensitive collation.
            string text = search.Trim().ToLower();
            query = query.Where(f => f.Comment.ToLower().Contains(text));
        }
        return query.OrderByDescending(f => f.CreatedAtUtc).ToListAsync();
    }

    public Task<List<Feedback>> GetMineAsync(string customerId)
    {
        return _context.Feedback.AsNoTracking().Include(f => f.Reservation)
            .Include(f => f.DishRatings).ThenInclude(d => d.MenuItem)
            .Where(f => f.Reservation!.CustomerId == customerId)
            .OrderByDescending(f => f.CreatedAtUtc).ToListAsync();
    }

    public Task<List<Feedback>> GetPublicAsync(int take)
    {
        return _context.Feedback.AsNoTracking().Include(f => f.Reservation).ThenInclude(r => r!.Customer)
            .Where(f => !f.IsHidden && !f.CommentRemoved && f.Comment != "")
            .OrderByDescending(f => f.CreatedAtUtc).Take(take).ToListAsync();
    }

    public async Task<FeedbackSummary> GetSummaryAsync(int weeks)
    {
        var all = await _context.Feedback.AsNoTracking()
            .Select(f => new { f.Rating, f.CreatedAtUtc, f.IsHidden, f.IsFlagged, f.RepliedAtUtc }).ToListAsync();
        var summary = new FeedbackSummary
        {
            Count = all.Count,
            Average = all.Count == 0 ? null : all.Average(f => f.Rating),
            HiddenCount = all.Count(f => f.IsHidden),
            FlaggedCount = all.Count(f => f.IsFlagged),
            AwaitingReplyCount = all.Count(f => f.RepliedAtUtc == null)
        };
        foreach (var item in all) { summary.StarCounts[item.Rating - 1]++; }
        // Weeks run Monday to Sunday in Malaysia time, ending with the current week.
        DateTime today = MalaysiaTime.FromUtc(DateTime.UtcNow).Date;
        DateTime monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        for (int n = weeks - 1; n >= 0; n--)
        {
            DateTime start = monday.AddDays(-7 * n);
            var inWeek = all.Where(f => MalaysiaTime.FromUtc(f.CreatedAtUtc) >= start
                && MalaysiaTime.FromUtc(f.CreatedAtUtc) < start.AddDays(7)).ToList();
            summary.Weeks.Add(new FeedbackWeek(start, inWeek.Count, inWeek.Count == 0 ? null : inWeek.Average(f => f.Rating)));
        }
        return summary;
    }

    public async Task<List<DishRatingSummary>> GetDishSummaryAsync()
    {
        // Every rating counts, including those on hidden reviews, like the overall average.
        var rows = await _context.DishRatings.AsNoTracking()
            .GroupBy(d => new { d.MenuItemId, d.MenuItem!.Name })
            .Select(g => new { g.Key.MenuItemId, g.Key.Name, Count = g.Count(), Average = g.Average(d => (double)d.Rating) })
            .ToListAsync();
        return rows.Select(r => new DishRatingSummary(r.MenuItemId, r.Name, r.Count, r.Average))
            .OrderByDescending(r => r.Average).ThenByDescending(r => r.Count).ThenBy(r => r.Name).ToList();
    }

    public async Task<ServiceResult> ReplyAsync(int id, string? reply)
    {
        string text = reply?.Trim() ?? "";
        if (text.Length == 0 || text.Length > 1000) { return ServiceResult.Failure("Write a reply of 1 to 1000 characters."); }
        var feedback = await _context.Feedback.Include(f => f.Reservation).SingleOrDefaultAsync(f => f.Id == id);
        if (feedback == null) { return ServiceResult.Failure("Feedback not found."); }
        bool first = feedback.RepliedAtUtc == null;
        feedback.Reply = text;
        feedback.RepliedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        if (first)
        {
            await _notifications.NotifyUserAsync(feedback.Reservation!.CustomerId, NotificationType.General,
                "Reply to your feedback", "Three Grills has replied to your review. Thank you for dining with us.", "/Customer/Feedback");
        }
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetHiddenAsync(int id, bool hidden)
    {
        int changed = await _context.Feedback.Where(f => f.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsHidden, hidden));
        return changed == 1 ? ServiceResult.Success() : ServiceResult.Failure("Feedback not found.");
    }

    public async Task<ServiceResult> RemoveCommentAsync(int id)
    {
        // The row stays, so the rating still counts and the customer cannot post a second review for the visit.
        int changed = await _context.Feedback.Where(f => f.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Comment, "").SetProperty(f => f.CommentRemoved, true));
        return changed == 1 ? ServiceResult.Success() : ServiceResult.Failure("Feedback not found.");
    }
}
