using Rroms.Web.Common;
using Rroms.Web.Models;

namespace Rroms.Web.Services;

/// <summary>
/// Lets administrators add dietary tags, so the list is not fixed in code. Owner: YY (YY-10).
/// Customers pick the tags they avoid on their profile, and YJ's menu items use the same tags.
/// </summary>
public interface IDietaryTagService
{
    //All tags in display order (the same list the profile and menu pages show)
    Task<IReadOnlyList<DietaryTag>> GetAllAsync();

    //Adds a new tag at the end of the list. Refuses blank, too long or duplicate names
    Task<ServiceResult> AddAsync(string name);
}