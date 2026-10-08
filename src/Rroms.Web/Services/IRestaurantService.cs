using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Restaurant setup: contact details, opening hours, closures, dining areas, tables and menu categories. Owner: WL (Sprint 2, WL-1).
/// </summary>
/// <remarks>
/// Pages call this interface and never use ApplicationDbContext directly (shared-contracts.md, section 1).
/// Methods that can fail for a business reason return a <see cref="ServiceResult"/> whose
/// Errors the page copies into ModelState.
/// Other modules use the read methods: WL-2 booking uses <see cref="GetBookableTablesAsync"/> and
/// <see cref="IsClosedOnAsync"/>, YJ's menu items use <see cref="GetMenuCategoriesAsync"/>.
/// </remarks>
public interface IRestaurantService
{
    // ============================== 1. Restaurant info: profile ==============================//

    /// <summary>Returns the restaurant's contact details (the single profile row).</summary>
    Task<RestaurantProfile> GetProfileAsync();

    /// <summary>Saves new contact details.</summary>
    /// <param name="input">The values from the form.</param>
    Task<ServiceResult> UpdateProfileAsync(RestaurantProfileInput input);

    // ============================== 1. Restaurant info: opening hours and closures (Get/Update/Add/Delete) ==============================//
    /// OperatingHour must got, so no need Add/Delet: Only Get and Update
    /// SpecialClosure Get current close day, Add and Delete when need extra off day
  
    /// <summary>Returns the seven weekday rows, Monday first.</summary>
    Task<IReadOnlyList<OperatingHour>> GetOperatingHoursAsync();

    /// <summary>Changes the hours of one day. Opening must be before closing unless the day is closed.</summary>
    /// <param name="input">The day and its new hours.</param>
    Task<ServiceResult> UpdateOperatingHourAsync(OperatingHourInput input);

    /// <summary>Returns all special closures, earliest first.</summary> 
    Task<IReadOnlyList<SpecialClosure>> GetSpecialClosuresAsync();

    /// <summary>Adds a closed date range. It must not end before it starts or overlap another closure.</summary>
    /// <param name="input">The dates and reason.</param>
    /// <returns>The new closure's Id on success.</returns>
    Task<ServiceResult<int>> AddSpecialClosureAsync(SpecialClosureInput input);

    /// <summary>Removes a closure, for example one added by mistake.</summary>
    /// <param name="id">The closure to remove.</param>
    Task<ServiceResult> DeleteSpecialClosureAsync(int id);

    /// <summary>
    /// True when the restaurant does not open on this date, either because the weekday is marked
    /// closed or because a special closure covers it. Used by WL-2 booking.
    /// </summary>
    /// <param name="date">A Malaysia calendar date.</param>
    Task<bool> IsClosedOnAsync(DateOnly date);

    // ============================== 2. Dining layout: areas ==============================//

    /// <summary>Returns dining areas ordered by name.</summary>
    /// <param name="includeInactive">True for admin lists; false hides closed areas.</param>
    Task<IReadOnlyList<DiningArea>> GetDiningAreasAsync(bool includeInactive);
    
    /// <summary>Returns one area, or null when it does not exist.</summary>
    /// <param name="id">The area's Id.</param>
    Task<DiningArea?> GetDiningAreaAsync(int id);

    /// <summary>Creates a dining area with a unique name.</summary>
    /// <param name="input">The values from the form.</param>
    /// <returns>The new area's Id on success.</returns>
    Task<ServiceResult<int>> CreateDiningAreaAsync(DiningAreaInput input);

    /// <summary>Renames or (de)activates a dining area.</summary>
    /// <param name="id">The area to change.</param>
    /// <param name="input">The new values.</param>
    Task<ServiceResult> UpdateDiningAreaAsync(int id, DiningAreaInput input);

    // ============================== 2. Dining layout: tables ==============================//

    /// <summary>Returns tables with their area, ordered by area then label.</summary>
    /// <param name="includeInactive">True for admin lists; false hides tables out of use.</param>
    Task<IReadOnlyList<DiningTable>> GetTablesAsync(bool includeInactive);

    /// <summary>Returns one table with its area, or null when it does not exist.</summary>
    /// <param name="id">The table's Id.</param>
    Task<DiningTable?> GetTableAsync(int id);

    /// <summary>Creates a table with a unique label and 1-20 seats in an active area.</summary>
    /// <param name="input">The values from the form.</param>
    /// <returns>The new table's Id on success.</returns>
    Task<ServiceResult<int>> CreateTableAsync(DiningTableInput input);

    /// <summary>Changes a table's label, seats, area or active state.</summary>
    /// <param name="id">The table to change.</param>
    /// <param name="input">The new values.</param>
    Task<ServiceResult> UpdateTableAsync(int id, DiningTableInput input);

    /// <summary>
    /// Tables a customer may book: the table AND its area are both active.
    /// Ordered smallest first, so booking can offer the best-fitting table. Used by WL-2.
    /// </summary>
    Task<IReadOnlyList<DiningTable>> GetBookableTablesAsync();

    // ============================== 3. Menu categories ==============================//

    /// <summary>Returns menu categories in display order. Used by YJ's menu pages.</summary>
    /// <param name="includeInactive">True for admin lists; false gives only categories shown on the menu.</param>
    Task<IReadOnlyList<MenuCategory>> GetMenuCategoriesAsync(bool includeInactive);

    /// <summary>Returns one category, or null when it does not exist.</summary>
    /// <param name="id">The category's Id.</param>
    Task<MenuCategory?> GetMenuCategoryAsync(int id);

    /// <summary>Creates a menu category with a unique name.</summary>
    /// <param name="input">The values from the form.</param>
    /// <returns>The new category's Id on success.</returns>
    Task<ServiceResult<int>> CreateMenuCategoryAsync(MenuCategoryInput input);

    /// <summary>Renames, reorders or (de)activates a menu category.</summary>
    /// <param name="id">The category to change.</param>
    /// <param name="input">The new values.</param>
    Task<ServiceResult> UpdateMenuCategoryAsync(int id, MenuCategoryInput input);
}
