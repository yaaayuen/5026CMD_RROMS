using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Business rules for restaurant setup (WL-1). See <see cref="IRestaurantService"/> for what each method does.
/// </summary>
/// <remarks>
/// Validation happens in three layers: data annotations on the form (for the user), the checks in
/// this class (business rules such as "names are unique, ignoring case"), and the database
/// constraints in RestaurantSetupConfiguration (last line of defence).
/// </remarks>
public class RestaurantService : IRestaurantService //Implements
{
    //============================== 1. Restaurant Profile ==============================//
    /// <summary>Shown when the seeded profile row is missing.</summary>
    public const string ProfileMissingMessage = "The restaurant profile is missing. Apply the database migrations.";

    //============================== 2. Special Closure ==============================//
    /// <summary>Shown when opening time is not before closing time.</summary>
    public const string OpensBeforeClosesMessage = "Opening time must be earlier than closing time.";

    /// <summary>Shown when the weekday row does not exist.</summary>
    public const string DayNotFoundMessage = "That day of the week was not found.";

    /// <summary>Shown when a closure ends before it starts.</summary>
    public const string ClosureDatesMessage = "The closure cannot end before it starts.";

    /// <summary>Shown when a new closure overlaps an existing one.</summary>
    public const string ClosureOverlapMessage = "These dates overlap another closure.";

    /// <summary>Shown when the closure to delete does not exist.</summary>
    public const string ClosureNotFoundMessage = "That closure no longer exists.";

    //============================== 3. Dining Area ==============================//
    /// <summary>Shown when another area already uses the name.</summary>
    public const string DuplicateAreaNameMessage = "A dining area with this name already exists.";

    /// <summary>Shown when the area does not exist.</summary>
    public const string AreaNotFoundMessage = "That dining area does not exist.";

    /// <summary>Shown when a table is moved into a closed area.</summary>
    public const string InactiveAreaMessage = "Choose a dining area that is open for bookings.";

    //============================== 4. Dining Table ==============================//
    /// <summary>Shown when another table already uses the label.</summary>
    public const string DuplicateTableLabelMessage = "A table with this label already exists.";

    /// <summary>Shown when the table does not exist.</summary>
    public const string TableNotFoundMessage = "That table does not exist.";

    //============================== 5. Menu Category ==============================//
    /// <summary>Shown when another category already uses the name.</summary>
    public const string DuplicateCategoryNameMessage = "A menu category with this name already exists.";

    /// <summary>Shown when the category does not exist.</summary>
    public const string CategoryNotFoundMessage = "That menu category does not exist.";

    /// <summary>Shown when the database rejects a save that passed the checks above.</summary>
    public const string SaveRejectedMessage = "The change could not be saved because it breaks a database rule, for example a name another user saved at the same moment. Please check and try again.";

    /// <summary>
    /// Passed as "excludeId" when creating, because no saved row has Id 0.
    /// </summary>
    private const int NoExcludedId = 0;

    private readonly ApplicationDbContext _context;

    /// <summary>The database context is supplied by dependency injection (one per web request).</summary>
    public RestaurantService(ApplicationDbContext context)
    {
        _context = context;
    }

    //============================== 1. Restaurant profile ==============================//

    /// <inheritdoc />
    public async Task<RestaurantProfile> GetProfileAsync()
    {
        RestaurantProfile? profile = await _context.RestaurantProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == RestaurantProfile.SingletonId);

        if (profile == null)
        {
            // The row is created by the migration's seed data, so this only happens on a broken database.
            // That is an unexpected failure, not a business rule, so it is an exception (shared-contracts.md).
            throw new InvalidOperationException(ProfileMissingMessage);
        }

        return profile;
    }

    /// <inheritdoc />
    public async Task<ServiceResult> UpdateProfileAsync(RestaurantProfileInput input)
    {
        List<string> errors = ValidateAnnotations(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Failure(errors.ToArray());
        }

        RestaurantProfile? profile = await _context.RestaurantProfiles
            .SingleOrDefaultAsync(p => p.Id == RestaurantProfile.SingletonId);
        if (profile == null)
        {
            return ServiceResult.Failure(ProfileMissingMessage);
        }

        profile.Name = input.Name.Trim();
        profile.Address = input.Address.Trim();
        profile.Phone = input.Phone.Trim();
        profile.Email = input.Email.Trim();
        profile.UpdatedAtUtc = DateTime.UtcNow;

        return await SaveAsync();
    }

    //============================== 2. Opening hours and closures ==============================//

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperatingHour>> GetOperatingHoursAsync()
    {
        List<OperatingHour> hours = await _context.OperatingHours.AsNoTracking().ToListAsync();

        // Sorted in memory: there are only seven rows, and DayOfWeek is stored as text in the database.
        return hours.OrderBy(h => MondayFirstOrder(h.DayOfWeek)).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult> UpdateOperatingHourAsync(OperatingHourInput input)
    {
        // A closed day keeps whatever times were entered; they are simply ignored.
        if (!input.IsClosed && input.OpensAt >= input.ClosesAt)
        {
            return ServiceResult.Failure(OpensBeforeClosesMessage);
        }

        OperatingHour? hour = await _context.OperatingHours
            .SingleOrDefaultAsync(h => h.DayOfWeek == input.DayOfWeek);
        if (hour == null)
        {
            return ServiceResult.Failure(DayNotFoundMessage);
        }

        hour.OpensAt = input.OpensAt;
        hour.ClosesAt = input.ClosesAt;
        hour.IsClosed = input.IsClosed;

        return await SaveAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SpecialClosure>> GetSpecialClosuresAsync()
    {
        return await _context.SpecialClosures
            .AsNoTracking()
            .OrderBy(c => c.StartDate)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<int>> AddSpecialClosureAsync(SpecialClosureInput input)
    {
        List<string> errors = ValidateAnnotations(input);
        if (errors.Count > 0)
        {
            return ServiceResult<int>.Failure(errors.ToArray());
        }

        if (input.EndDate < input.StartDate)
        {
            return ServiceResult<int>.Failure(ClosureDatesMessage);
        }

        // Two ranges overlap when each one starts on or before the day the other one ends.
        bool overlaps = await _context.SpecialClosures
            .AnyAsync(c => c.StartDate <= input.EndDate && c.EndDate >= input.StartDate);
        if (overlaps)
        {
            return ServiceResult<int>.Failure(ClosureOverlapMessage);
        }

        var closure = new SpecialClosure
        {
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            Reason = input.Reason.Trim()
        };
        _context.SpecialClosures.Add(closure);

        ServiceResult saved = await SaveAsync();
        if (!saved.Succeeded)
        {
            return ServiceResult<int>.Failure(saved.Errors.ToArray());
        }

        return ServiceResult<int>.Success(closure.Id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult> DeleteSpecialClosureAsync(int id)
    {
        SpecialClosure? closure = await _context.SpecialClosures.SingleOrDefaultAsync(c => c.Id == id);
        if (closure == null)
        {
            return ServiceResult.Failure(ClosureNotFoundMessage);
        }

        // Closures are not referenced by any other table, so a real delete is safe here.
        _context.SpecialClosures.Remove(closure);
        return await SaveAsync();
    }

    /// <inheritdoc />
    public async Task<bool> IsClosedOnAsync(DateOnly date)
    {
        bool coveredByClosure = await _context.SpecialClosures
            .AnyAsync(c => c.StartDate <= date && c.EndDate >= date);
        if (coveredByClosure)
        {
            return true;
        }

        OperatingHour? hour = await _context.OperatingHours
            .AsNoTracking()
            .SingleOrDefaultAsync(h => h.DayOfWeek == date.DayOfWeek);

        // A day with no row is treated as closed: never take bookings for a day nobody configured.
        return hour == null || hour.IsClosed;
    }

    //============================== 3. Dining areas ==============================//

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiningArea>> GetDiningAreasAsync(bool includeInactive)
    {
        IQueryable<DiningArea> query = _context.DiningAreas.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(a => a.IsActive);
        }

        return await query.OrderBy(a => a.Name).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<DiningArea?> GetDiningAreaAsync(int id)
    {
        return await _context.DiningAreas.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<int>> CreateDiningAreaAsync(DiningAreaInput input)
    {
        List<string> errors = ValidateAnnotations(input);
        if (errors.Count > 0)
        {
            return ServiceResult<int>.Failure(errors.ToArray());
        }

        string name = input.Name.Trim();
        if (await IsAreaNameTakenAsync(name, NoExcludedId))
        {
            return ServiceResult<int>.Failure(DuplicateAreaNameMessage);
        }

        var area = new DiningArea
        {
            Name = name,
            Description = CleanOptional(input.Description),
            IsActive = input.IsActive
        };
        _context.DiningAreas.Add(area);

        ServiceResult saved = await SaveAsync();
        if (!saved.Succeeded)
        {
            return ServiceResult<int>.Failure(saved.Errors.ToArray());
        }

        return ServiceResult<int>.Success(area.Id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult> UpdateDiningAreaAsync(int id, DiningAreaInput input)
    {
        List<string> errors = ValidateAnnotations(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Failure(errors.ToArray());
        }

        DiningArea? area = await _context.DiningAreas.SingleOrDefaultAsync(a => a.Id == id);
        if (area == null)
        {
            return ServiceResult.Failure(AreaNotFoundMessage);
        }

        string name = input.Name.Trim();
        if (await IsAreaNameTakenAsync(name, id))
        {
            return ServiceResult.Failure(DuplicateAreaNameMessage);
        }

        // Deactivating an area is allowed: its tables simply stop appearing in GetBookableTablesAsync.
        area.Name = name;
        area.Description = CleanOptional(input.Description);
        area.IsActive = input.IsActive;

        return await SaveAsync();
    }

    //============================== 4. Dining Tables ==============================//

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiningTable>> GetTablesAsync(bool includeInactive)
    {
        IQueryable<DiningTable> query = _context.DiningTables
            .AsNoTracking()
            .Include(t => t.DiningArea);
        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        return await query
            .OrderBy(t => t.DiningArea!.Name)
            .ThenBy(t => t.Label)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<DiningTable?> GetTableAsync(int id)
    {
        return await _context.DiningTables
            .AsNoTracking()
            .Include(t => t.DiningArea)
            .SingleOrDefaultAsync(t => t.Id == id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<int>> CreateTableAsync(DiningTableInput input)
    {
        List<string> errors = ValidateAnnotations(input);
        if (errors.Count > 0)
        {
            return ServiceResult<int>.Failure(errors.ToArray());
        }

        DiningArea? area = await _context.DiningAreas
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == input.DiningAreaId);
        if (area == null)
        {
            return ServiceResult<int>.Failure(AreaNotFoundMessage);
        }

        if (!area.IsActive)
        {
            return ServiceResult<int>.Failure(InactiveAreaMessage);
        }

        string label = input.Label.Trim();
        if (await IsTableLabelTakenAsync(label, NoExcludedId))
        {
            return ServiceResult<int>.Failure(DuplicateTableLabelMessage);
        }

        var table = new DiningTable
        {
            Label = label,
            Capacity = input.Capacity,
            DiningAreaId = input.DiningAreaId,
            IsActive = input.IsActive
        };
        _context.DiningTables.Add(table);

        ServiceResult saved = await SaveAsync();
        if (!saved.Succeeded)
        {
            return ServiceResult<int>.Failure(saved.Errors.ToArray());
        }

        return ServiceResult<int>.Success(table.Id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult> UpdateTableAsync(int id, DiningTableInput input)
    {
        List<string> errors = ValidateAnnotations(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Failure(errors.ToArray());
        }

        DiningTable? table = await _context.DiningTables.SingleOrDefaultAsync(t => t.Id == id);
        if (table == null)
        {
            return ServiceResult.Failure(TableNotFoundMessage);
        }

        DiningArea? area = await _context.DiningAreas
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == input.DiningAreaId);
        if (area == null)
        {
            return ServiceResult.Failure(AreaNotFoundMessage);
        }

        // Moving a table INTO a closed area makes no sense, but editing a table that already sits
        // in a closed area (e.g. fixing its label) must still be possible.
        bool movingToAnotherArea = input.DiningAreaId != table.DiningAreaId;
        if (movingToAnotherArea && !area.IsActive)
        {
            return ServiceResult.Failure(InactiveAreaMessage);
        }

        string label = input.Label.Trim();
        if (await IsTableLabelTakenAsync(label, id))
        {
            return ServiceResult.Failure(DuplicateTableLabelMessage);
        }

        table.Label = label;
        table.Capacity = input.Capacity;
        table.DiningAreaId = input.DiningAreaId;
        table.IsActive = input.IsActive;

        return await SaveAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiningTable>> GetBookableTablesAsync()
    {
        // Both switches must be on: a working table in a closed area cannot be booked.
        return await _context.DiningTables
            .AsNoTracking()
            .Include(t => t.DiningArea)
            .Where(t => t.IsActive && t.DiningArea!.IsActive)
            .OrderBy(t => t.Capacity)
            .ThenBy(t => t.Label)
            .ToListAsync();
    }

    //============================== 5. Menu categories ==============================//

    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuCategory>> GetMenuCategoriesAsync(bool includeInactive)
    {
        IQueryable<MenuCategory> query = _context.MenuCategories.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<MenuCategory?> GetMenuCategoryAsync(int id)
    {
        return await _context.MenuCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<int>> CreateMenuCategoryAsync(MenuCategoryInput input)
    {
        List<string> errors = ValidateAnnotations(input);
        if (errors.Count > 0)
        {
            return ServiceResult<int>.Failure(errors.ToArray());
        }

        string name = input.Name.Trim();
        if (await IsCategoryNameTakenAsync(name, NoExcludedId))
        {
            return ServiceResult<int>.Failure(DuplicateCategoryNameMessage);
        }

        var category = new MenuCategory
        {
            Name = name,
            DisplayOrder = input.DisplayOrder,
            IsActive = input.IsActive
        };
        _context.MenuCategories.Add(category);

        ServiceResult saved = await SaveAsync();
        if (!saved.Succeeded)
        {
            return ServiceResult<int>.Failure(saved.Errors.ToArray());
        }

        return ServiceResult<int>.Success(category.Id);
    }

    /// <inheritdoc />
    public async Task<ServiceResult> UpdateMenuCategoryAsync(int id, MenuCategoryInput input)
    {
        List<string> errors = ValidateAnnotations(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Failure(errors.ToArray());
        }

        MenuCategory? category = await _context.MenuCategories.SingleOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return ServiceResult.Failure(CategoryNotFoundMessage);
        }

        string name = input.Name.Trim();
        if (await IsCategoryNameTakenAsync(name, id))
        {
            return ServiceResult.Failure(DuplicateCategoryNameMessage);
        }

        category.Name = name;
        category.DisplayOrder = input.DisplayOrder;
        category.IsActive = input.IsActive;

        return await SaveAsync();
    }

    //============================== 6. Helpers ==============================//

    /// <summary>
    /// Runs the [Required], [Range], [StringLength] ... rules on an input object.
    /// The page already does this through ModelState; repeating it here means the service stays safe
    /// even when it is called from somewhere else (tests, another module).
    /// </summary>
    private static List<string> ValidateAnnotations(object input)
    {
        var validationContext = new ValidationContext(input);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, validationContext, results, validateAllProperties: true);

        var messages = new List<string>();
        foreach (ValidationResult result in results)
        {
            messages.Add(result.ErrorMessage ?? "A value is not valid.");
        }

        return messages;
    }

    /// <summary>
    /// Saves pending changes. If the database refuses them (a unique index or check constraint),
    /// the failure is reported to the user instead of crashing the page.
    /// </summary>
    private async Task<ServiceResult> SaveAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
            return ServiceResult.Success();
        }
        catch (DbUpdateException)
        {
            // Example: two administrators add table "T09" at the same moment. Both pass the
            // "label taken?" check, but the unique index lets only the first insert through.
            // Forget the rejected change so this context can still be used.
            _context.ChangeTracker.Clear();
            return ServiceResult.Failure(SaveRejectedMessage);
        }
    }

    /// <summary>True when another area already has this name, ignoring upper/lower case.</summary>
    /// <param name="name">The trimmed name to check.</param>
    /// <param name="excludeId">The area being edited (its own name is not a clash), or 0 when creating.</param>
    private async Task<bool> IsAreaNameTakenAsync(string name, int excludeId)
    {
        string upperName = name.ToUpperInvariant();
        return await _context.DiningAreas.AnyAsync(a => a.Name.ToUpper() == upperName && a.Id != excludeId);
    }

    /// <summary>True when another table already has this label, ignoring upper/lower case.</summary>
    /// <param name="label">The trimmed label to check.</param>
    /// <param name="excludeId">The table being edited, or 0 when creating.</param>
    private async Task<bool> IsTableLabelTakenAsync(string label, int excludeId)
    {
        string upperLabel = label.ToUpperInvariant();
        return await _context.DiningTables.AnyAsync(t => t.Label.ToUpper() == upperLabel && t.Id != excludeId);
    }

    /// <summary>True when another category already has this name, ignoring upper/lower case.</summary>
    /// <param name="name">The trimmed name to check.</param>
    /// <param name="excludeId">The category being edited, or 0 when creating.</param>
    private async Task<bool> IsCategoryNameTakenAsync(string name, int excludeId)
    {
        string upperName = name.ToUpperInvariant();
        return await _context.MenuCategories.AnyAsync(c => c.Name.ToUpper() == upperName && c.Id != excludeId);
    }

    /// <summary>Turns an empty optional text box into null, and trims anything else.</summary>
    private static string? CleanOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    /// <summary>
    /// Sort key that puts Monday first and Sunday last. .NET numbers the week from Sunday (0),
    /// but a restaurant's opening-hours table is normally read from Monday.
    /// </summary>
    private static int MondayFirstOrder(DayOfWeek day)
    {
        if (day == DayOfWeek.Sunday)
        {
            return 7;
        }

        return (int)day;
    }
}
