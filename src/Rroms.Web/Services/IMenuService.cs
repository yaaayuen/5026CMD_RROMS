using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Menu item administration: dishes, prices, availability and offers. 
/// Owner: YJ (Sprint 3, YJ-1).
/// </summary>
/// <remarks>
/// Pages call this interface and never use ApplicationDbContext directly (shared-contracts.md, section 1).
/// Methods that can fail for a business reason return a <see cref="ServiceResult"/> whose
/// Errors the page copies into ModelState.
/// Customer browsing methods (search, filters, current price) are added in YJ-2.
/// </remarks>
public interface IMenuService
{
    /// <summary>Returns menu items with their category, ordered by category then name. 
    /// Used by the admin list.</summary>
    /// <param name="categoryId">Only items in this category, or null for every item.</param>
    Task<IReadOnlyList<MenuItem>> GetMenuItemsAsync(int? categoryId);

    /// <summary>Returns one item with its category, or null when it does not exist.</summary>
    /// <param name="id">The item's Id.</param>
    Task<MenuItem?> GetMenuItemAsync(int id);

    /// <summary>
    /// Creates a menu item. The price must be above zero, the category must exist and be active,
    /// the name must be unique in the category, and an offer must be complete and cheaper than the normal price.
    /// </summary>
    /// <param name="input">The values from the form.</param>
    /// <returns>The new item's Id on success.</returns>
    Task<ServiceResult<int>> CreateMenuItemAsync(MenuItemInput input);

    /// <summary>Changes an existing item. The same rules as creating apply.</summary>
    /// <param name="id">The item to change.</param>
    /// <param name="input">The new values.</param>
    Task<ServiceResult> UpdateMenuItemAsync(int id, MenuItemInput input);

    /// <summary>Turns ordering on or off for an item without changing anything else.</summary>
    /// <param name="id">The item to change.</param>
    /// <param name="isAvailable">True to allow ordering, false to block it.</param>
    Task<ServiceResult> SetAvailabilityAsync(int id, bool isAvailable);

    /// <summary>Deletes an item. Once orders exist (YJ-0) an item that has been ordered must be made unavailable instead.</summary>
    /// <param name="id">The item to delete.</param>
    Task<ServiceResult> DeleteMenuItemAsync(int id);
}