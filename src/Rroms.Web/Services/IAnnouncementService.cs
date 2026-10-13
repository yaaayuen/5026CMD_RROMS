using Rroms.Web.Common;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>Admin publishing and public active announcements.</summary>
public interface IAnnouncementService
{
    Task<List<Announcement>> GetAsync(bool activeOnly);
    Task<ServiceResult> SaveAsync(Announcement input);
}
