using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>
/// Publishes dated notices and notifies customers when a published promotion starts showing:
/// once for a one-off post, and on each chosen day for a recurring one.
/// </summary>
public class AnnouncementService : IAnnouncementService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;
    public AnnouncementService(ApplicationDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task<List<Announcement>> GetAsync(bool activeOnly)
    {
        IQueryable<Announcement> query = _context.Announcements.AsNoTracking().Include(a => a.MenuItem);
        if (!activeOnly) { return await query.OrderByDescending(a => a.StartUtc).ToListAsync(); }
        DateTime now = DateTime.UtcNow;
        var inDates = await query.Where(a => a.IsPublished && a.StartUtc <= now && now < a.EndUtc)
            .OrderByDescending(a => a.StartUtc).ToListAsync();
        // Days and daily hours of recurring posts are checked here, in Malaysia time.
        return inDates.Where(a => AnnouncementSchedule.IsShowing(a, now)).ToList();
    }

    public async Task<ServiceResult> SaveAsync(Announcement input)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, true))
        {
            return ServiceResult.Failure(errors.Select(e => e.ErrorMessage!).ToArray());
        }
        if (input.EndUtc <= input.StartUtc) { return ServiceResult.Failure("End must be after start."); }
        if (input.IsPublished && input.EndUtc <= DateTime.UtcNow) { return ServiceResult.Failure("A published notice must end in the future."); }
        var item = input.Id == 0 ? new Announcement() : await _context.Announcements.FindAsync(input.Id);
        if (item == null) { return ServiceResult.Failure("Announcement not found."); }
        if (input.MenuItemId.HasValue && !await _context.MenuItems.AnyAsync(m => m.Id == input.MenuItemId.Value))
        {
            return ServiceResult.Failure("The linked dish no longer exists.");
        }
        if (input.RepeatDays is < 0 or > 127) { return ServiceResult.Failure("Choose valid days to repeat on."); }
        if (input.RepeatDays == 0) { input.DailyFromLocal = null; input.DailyUntilLocal = null; }
        else
        {
            if (input.DailyFromLocal.HasValue != input.DailyUntilLocal.HasValue)
            {
                return ServiceResult.Failure("Enter both daily times, or leave both empty to show all day.");
            }
            if (input.DailyUntilLocal <= input.DailyFromLocal) { return ServiceResult.Failure("The daily end time must be after the daily start time."); }
            if (AnnouncementSchedule.NextStartUtc(input, input.StartUtc.AddTicks(-1)) == null)
            {
                return ServiceResult.Failure("The dates never include one of the chosen days.");
            }
        }
        item.Title = input.Title.Trim();
        item.Message = input.Message.Trim();
        item.StartUtc = input.StartUtc;
        item.EndUtc = input.EndUtc;
        item.IsPromotion = input.IsPromotion;
        item.IsPublished = input.IsPublished;
        item.MenuItemId = input.MenuItemId;
        item.RepeatDays = input.RepeatDays;
        item.DailyFromLocal = input.DailyFromLocal;
        item.DailyUntilLocal = input.DailyUntilLocal;
        item.UpdatedAtUtc = DateTime.UtcNow;
        if (item.Id == 0) { _context.Announcements.Add(item); }
        await _context.SaveChangesAsync();
        // A promotion that is showing now is announced now; a later one waits for AnnouncementScheduler.
        await NotifyDueAsync();
        return ServiceResult.Success();
    }

    public Task<List<MenuItem>> GetDishChoicesAsync()
    {
        return _context.MenuItems.AsNoTracking().OrderBy(m => m.Name).ToListAsync();
    }

    public async Task<int> NotifyDueAsync()
    {
        DateTime now = DateTime.UtcNow;
        var candidates = await _context.Announcements.AsNoTracking()
            .Where(a => a.IsPromotion && a.IsPublished && a.StartUtc <= now && now < a.EndUtc).ToListAsync();
        // Due when the current showing began after the last notice: once for a one-off post, once a day for a recurring one.
        var due = candidates.Select(a => (a.Id, Start: AnnouncementSchedule.CurrentStartUtc(a, now), a.NotifiedAtUtc))
            .Where(a => a.Start.HasValue && (a.NotifiedAtUtc == null || a.NotifiedAtUtc < a.Start)).ToList();
        int sent = 0;
        foreach (var (id, start, _) in due)
        {
            // Claim the row first, so two overlapping runs can never notify customers twice.
            DateTime showingStart = start!.Value;
            int claimed = await _context.Announcements
                .Where(a => a.Id == id && (a.NotifiedAtUtc == null || a.NotifiedAtUtc < showingStart))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.NotifiedAtUtc, now));
            if (claimed == 0) { continue; }
            var item = await _context.Announcements.AsNoTracking().Include(a => a.MenuItem).SingleAsync(a => a.Id == id);
            await _notifications.NotifyRoleAsync(RoleNames.Customer, NotificationType.Promotion,
                item.Title, item.Message, LinkFor(item));
            sent++;
        }
        return sent;
    }

    /// <summary>Opens the linked dish through the menu search, or the News and offers page.</summary>
    public static string LinkFor(Announcement item)
    {
        return item.MenuItem == null ? "/Announcements" : "/Menu?q=" + Uri.EscapeDataString(item.MenuItem.Name);
    }
}
