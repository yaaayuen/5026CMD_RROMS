using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Publishes dated notices and notifies customers when a promotion is first published.</summary>
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
        var query = _context.Announcements.AsNoTracking();
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
        bool notify = input.IsPromotion && input.IsPublished && (!item.IsPublished || !item.IsPromotion);
        item.Title = input.Title.Trim();
        item.Message = input.Message.Trim();
        item.StartUtc = input.StartUtc;
        item.EndUtc = input.EndUtc;
        item.IsPromotion = input.IsPromotion;
        item.IsPublished = input.IsPublished;
        item.UpdatedAtUtc = DateTime.UtcNow;
        if (item.Id == 0) { _context.Announcements.Add(item); }
        await _context.SaveChangesAsync();
        if (notify)
        {
            await _notifications.NotifyRoleAsync(RoleNames.Customer, NotificationType.Promotion,
                item.Title, item.Message, "/Announcements");
        }
        return ServiceResult.Success();
    }
}
