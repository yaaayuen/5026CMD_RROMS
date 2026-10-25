using Rroms.Web.Common;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Admin publishing and public active announcements.</summary>
public interface IAnnouncementService
{
    Task<List<Announcement>> GetAsync(bool activeOnly);
    Task<ServiceResult> SaveAsync(Announcement input);
    /// <summary>Dishes an announcement may link to, by name.</summary>
    Task<List<MenuItem>> GetDishChoicesAsync();
    /// <summary>
    /// Notifies customers about every published promotion that has started showing and was not announced for this showing.
    /// Returns how many were sent.
    /// </summary>
    Task<int> NotifyDueAsync();
}
