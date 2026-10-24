using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Publishes dated notices and notifies customers once, when a published promotion reaches its start time.</summary>
public class AnnouncementService : IAnnouncementService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;
    public AnnouncementService(ApplicationDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public Task<List<Announcement>> GetAsync(bool activeOnly)
    {
        IQueryable<Announcement> query = _context.Announcements.AsNoTracking().Include(a => a.MenuItem);
        if (activeOnly)
        {
            DateTime now = DateTime.UtcNow;
            query = query.Where(a => a.IsPublished && a.StartUtc <= now && now < a.EndUtc);
        }
        return query.OrderByDescending(a => a.StartUtc).ToListAsync();
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
        item.Title = input.Title.Trim();
        item.Message = input.Message.Trim();
        item.StartUtc = input.StartUtc;
        item.EndUtc = input.EndUtc;
        item.IsPromotion = input.IsPromotion;
        item.IsPublished = input.IsPublished;
        item.MenuItemId = input.MenuItemId;
        item.UpdatedAtUtc = DateTime.UtcNow;
        if (item.Id == 0) { _context.Announcements.Add(item); }
        await _context.SaveChangesAsync();
        // A promotion that already started is announced now; a future one waits for AnnouncementScheduler.
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
        List<int> due = await _context.Announcements
            .Where(a => a.IsPromotion && a.IsPublished && a.NotifiedAtUtc == null && a.StartUtc <= now && now < a.EndUtc)
            .Select(a => a.Id).ToListAsync();
        int sent = 0;
        foreach (int id in due)
        {
            // Claim the row first, so two overlapping runs can never notify customers twice.
            int claimed = await _context.Announcements.Where(a => a.Id == id && a.NotifiedAtUtc == null)
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
